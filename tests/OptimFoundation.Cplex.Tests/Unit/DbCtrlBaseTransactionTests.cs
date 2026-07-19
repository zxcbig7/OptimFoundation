using System;
using System.Collections.Generic;
using System.Data;
using OptimFoundation.Core.IO;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    /// <summary>
    /// 假 IDbTransaction：記錄 Commit/Rollback/Dispose 呼叫次數，不碰真 DB。
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
    /// 假 IDbConnection：記錄 Open/Close/Dispose 呼叫，BeginTransaction 回傳可觀測的 FakeDbTransaction。
    /// </summary>
    public sealed class FakeDbConnection : IDbConnection
    {
        public bool OpenCalled;
        public bool DisposeCalled;
        public FakeDbTransaction LastTransaction;

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
    /// DBCtrlBase 的測試子類：只為了驅動交易編排（ExecuteInTransaction），
    /// 其餘抽象成員留最小實作，不碰真資料庫。
    /// </summary>
    public sealed class TestableDbCtrl : DBCtrlBase
    {
        private readonly Func<IDbConnection> _connectionFactory;

        public int CreateRawConnectionCallCount;
        // work 執行當下觀察到的 ambient 連線/交易，供測試斷言「內層操作共用同一條連線」。
        public IDbConnection ObservedAmbientConnectionDuringWork;
        public IDbTransaction ObservedAmbientTransactionDuringWork;

        public TestableDbCtrl(Func<IDbConnection> connectionFactory) : base("fake-connection-string")
        {
            _connectionFactory = connectionFactory;
        }

        // 暴露 base 的 ambient 狀態供測試斷言交易結束後已清空
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
            // 交易期間內層操作應共用 ambient 連線/交易——記錄下來供測試斷言。
            ObservedAmbientConnectionDuringWork = AmbientConnection;
            ObservedAmbientTransactionDuringWork = AmbientTransaction;
            return 0;
        }

        public override TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters) => default;

        public override void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows) { }
    }

    /// <summary>
    /// 驗證 DBCtrlBase.ExecuteInTransaction 真正的交易編排邏輯（與 Oracle 無關的部分）：
    /// 用假 IDbConnection/IDbTransaction 取代真連線，不需要真 Oracle 也能抓到編排邏輯被破壞。
    /// 這批測試就是「反向證明」的標的——把 Rollback() 呼叫拿掉時，下面應該要有測試失敗。
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

        // work 丟例外 → Rollback 被呼叫、例外原樣傳出、Commit 未被呼叫。
        // ★ 這是反向證明鎖定的測試：把 DBCtrlBase 的 tx.Rollback() 註解掉，本測試必須失敗。
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

        // 巢狀呼叫：外層已在交易中，內層再呼叫 ExecuteInTransaction → 只建立一次連線、只 begin/commit 一次，
        // 內層不提前 commit。
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

        // 交易期間內層操作（Execute）共用同一條 ambient 連線/交易，且該連線在 work 執行期間不被 dispose；
        // 交易結束後 ambient 狀態已清空（回到可再次獨立交易的狀態）。
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

            // 交易結束後：連線已釋放、ambient 狀態歸零
            Assert.True(conn.DisposeCalled);
            Assert.Null(ctrl.CurrentAmbientConnection);
            Assert.Null(ctrl.CurrentAmbientTransaction);
        }
    }
}
