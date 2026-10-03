using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using OptimFoundation.Core.IO;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    /// <summary>
    /// 測試用 IDbTransaction：只記錄 Commit、Rollback、Dispose 次數，不連線到資料庫。
    /// </summary>
    public sealed class FakeDbTransaction : IDbTransaction
    {
        public int CommitCallCount;
        public int RollbackCallCount;
        public int DisposeCallCount;

        public FakeDbTransaction(IDbConnection connection) => Connection = connection;

        public IDbConnection Connection { get; }
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public void Commit() => CommitCallCount++;
        public void Rollback() => RollbackCallCount++;
        public void Dispose() => DisposeCallCount++;
    }

    /// <summary>
    /// 測試用 IDbConnection：記錄 Open、Close、Dispose 的呼叫，BeginTransaction 回傳能記錄提交與回滾次數的 FakeDbTransaction。
    /// </summary>
    public sealed class FakeDbConnection : IDbConnection
    {
        public bool OpenCalled;
        public bool DisposeCalled;
        public FakeDbTransaction LastTransaction = null!;

        [AllowNull]
        public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => string.Empty;
        public ConnectionState State { get; private set; } = ConnectionState.Closed;

        public IDbTransaction BeginTransaction()
        {
            LastTransaction = new FakeDbTransaction(this);
            return LastTransaction;
        }

        public IDbTransaction BeginTransaction(IsolationLevel il) => BeginTransaction();
        public void ChangeDatabase(string databaseName) { }
        public void Close() => State = ConnectionState.Closed;
        public IDbCommand CreateCommand() => throw new NotSupportedException("測試不需要真的 command");
        public void Open() { OpenCalled = true; State = ConnectionState.Open; }
        public void Dispose() => DisposeCalled = true;
    }

    /// <summary>
    /// DbCtrlBase 的測試子類，用來執行並檢查 ExecuteInTransaction 的開始、提交與回滾流程，
    /// 其餘抽象成員僅供測試，不連線到資料庫。
    /// </summary>
    public sealed class TestableDbCtrl : DbCtrlBase
    {
        private readonly Func<IDbConnection> _connectionFactory;

        public int CreateRawConnectionCallCount;
        // 記錄 work 執行時共用的連線與交易，讓測試確認內層操作沒有另開連線。
        public IDbConnection? ObservedAmbientConnectionDuringWork;
        public IDbTransaction? ObservedAmbientTransactionDuringWork;

        public TestableDbCtrl(Func<IDbConnection> connectionFactory) : base("fake-connection-string")
        {
            _connectionFactory = connectionFactory;
        }

        // 提供基底類別目前的連線與交易，讓測試確認交易結束後已清空。
        public IDbConnection CurrentAmbientConnection => AmbientConnection;
        public IDbTransaction CurrentAmbientTransaction => AmbientTransaction;

        protected override IDbConnection CreateRawConnection()
        {
            CreateRawConnectionCallCount++;
            return _connectionFactory();
        }

        public override void Open() { }
        public override void Close() { }
        public override DataTable Query(string sql, params (string name, object value)[] parameters) => new DataTable();

        public override int Execute(string sql, params (string name, object value)[] parameters)
        {
            // 記下內層操作取得的連線與交易，確認與外層交易使用的是同一組物件。
            ObservedAmbientConnectionDuringWork = AmbientConnection;
            ObservedAmbientTransactionDuringWork = AmbientTransaction;
            return 0;
        }

        public override TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters) => default!;

        public override void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows) { }
    }

    /// <summary>
    /// 以假連線驗證交易開始、提交、回滾與巢狀交易，不連線 Oracle。
    /// </summary>
    public class DbCtrlBaseTransactionTests
    {
        // 成功路徑 → Commit 恰好一次，Rollback 從未被呼叫。
        [Fact]
        public void ExecuteInTransaction_Success_CommitsOnce_NeverRollsBack()
        {
            var conn = new FakeDbConnection();
            var ctrl = new TestableDbCtrl(() => conn);

            ctrl.ExecuteInTransaction(c => c.Execute("INSERT INTO T VALUES (1)"));

            Assert.Equal(1, conn.LastTransaction.CommitCallCount);
            Assert.Equal(0, conn.LastTransaction.RollbackCallCount);
        }

        // work 失敗須回滾、保留原例外且不得提交。
        [Fact]
        public void ExecuteInTransaction_WorkThrows_RollsBack_RethrowsOriginalException_NeverCommits()
        {
            var conn = new FakeDbConnection();
            var ctrl = new TestableDbCtrl(() => conn);

            var thrown = Assert.Throws<InvalidOperationException>(() =>
                ctrl.ExecuteInTransaction(c => throw new InvalidOperationException("boom")));

            Assert.Equal("boom", thrown.Message);
            Assert.Equal(1, conn.LastTransaction.RollbackCallCount);
            Assert.Equal(0, conn.LastTransaction.CommitCallCount);
        }

        // 巢狀交易共用連線；僅由外層提交。
        [Fact]
        public void ExecuteInTransaction_Nested_OnlyCreatesConnectionOnce_OnlyCommitsOnce()
        {
            var conn = new FakeDbConnection();
            var ctrl = new TestableDbCtrl(() => conn);

            ctrl.ExecuteInTransaction(outer =>
            {
                outer.Execute("outer op");
                ctrl.ExecuteInTransaction(inner =>
                {
                    inner.Execute("inner op");
                });
            });

            Assert.Equal(1, ctrl.CreateRawConnectionCallCount);
            Assert.Equal(1, conn.LastTransaction.CommitCallCount);
            Assert.Equal(0, conn.LastTransaction.RollbackCallCount);
        }

        // 內層共用連線與交易；外層結束後釋放並清空參照。
        [Fact]
        public void ExecuteInTransaction_InnerOperationsShareAmbientConnection_NotDisposedDuringWork_ClearedAfter()
        {
            var conn = new FakeDbConnection();
            var ctrl = new TestableDbCtrl(() => conn);
            bool connectionDisposedDuringWork = false;

            ctrl.ExecuteInTransaction(c =>
            {
                c.Execute("op");
                connectionDisposedDuringWork = conn.DisposeCalled;
            });

            Assert.Same(conn, ctrl.ObservedAmbientConnectionDuringWork);
            Assert.Same(conn.LastTransaction, ctrl.ObservedAmbientTransactionDuringWork);
            Assert.False(connectionDisposedDuringWork);

            // 交易結束後，連線已釋放，共用連線與交易參照也已清空。
            Assert.True(conn.DisposeCalled);
            Assert.Null(ctrl.CurrentAmbientConnection);
            Assert.Null(ctrl.CurrentAmbientTransaction);
        }
    }
}
