using System;
using System.Linq;
using OptimFoundation.Cplex.Tests.Mocks;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Db.Oracle;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // 用測試用 IDbCtrl 檢查 OracleSolutionSink 是否把多次 Write 放在同一個交易中批次寫入，不連線到 Oracle。
    public class OracleSolutionSinkTests
    {
        private static MockEngine BuiltEngine()
        {
            var engine = new MockEngine();
            engine.BuildBVs<VarS>(new[] { "E1", "E2" });
            engine.BuildBVs<VarInt>(new[] { 1 });
            engine.BuildBVs<VarDG>(new[] { new DateTime(2026, 1, 1) }, new[] { "G1" });
            return engine;
        }

        // (1) 成功路徑：3 個變數型別 → Commit → 恰好一次 ExecuteInTransaction、每型別各一次 ExecuteBatch
        // （非逐列 Execute）、列數加總正確、已 commit。
        [Fact]
        public void Commit_MultipleVariableTypes_WritesAllInSingleTransaction()
        {
            var fake = new FakeDbCtrl();
            var sink = new OracleSolutionSink(fake, "T_SOLUTION");
            var engine = BuiltEngine();

            using (var batch = sink.BeginBatch("D1", "U1"))
            {
                batch.Write<VarS>(engine);
                batch.Write<VarInt>(engine);
                batch.Write<VarDG>(engine);
                batch.Commit();
            }

            Assert.Equal(1, fake.ExecuteInTransactionCallCount);
            Assert.True(fake.Committed);
            Assert.False(fake.RolledBack);
            Assert.Empty(fake.ExecutedCommands);
            Assert.Equal(3, fake.ExecutedBatches.Count); // VarS / VarInt / VarDG 各一次 ExecuteBatch
            Assert.Equal(2 + 1 + 1, fake.ExecutedBatches.Sum(b => b.rows.Length));
        }

        // (2) 失敗回滾：第 2 個變數型別的批次寫入丟例外 → Commit 例外傳出、假物件記錄 rollback、未 commit
        [Fact]
        public void Commit_FailureOnSecondWrite_RollsBackAndPropagatesException()
        {
            var fake = new FakeDbCtrl { ThrowOnBatchCallNumber = 2 };
            var sink = new OracleSolutionSink(fake, "T_SOLUTION");
            var engine = BuiltEngine();

            using var batch = sink.BeginBatch("D1", "U1");
            batch.Write<VarS>(engine);   // 第 1 次 ExecuteBatch
            batch.Write<VarInt>(engine); // 第 2 次 ExecuteBatch：觸發失敗

            Assert.Throws<InvalidOperationException>(() => batch.Commit());
            Assert.True(fake.RolledBack);
            Assert.False(fake.Committed);
        }

        // (3) 未 Commit 就 Dispose → 完全沒有 ExecuteInTransaction、零批次寫入
        [Fact]
        public void Dispose_WithoutCommit_NeverStartsTransactionOrWrites()
        {
            var fake = new FakeDbCtrl();
            var sink = new OracleSolutionSink(fake, "T_SOLUTION");
            var engine = BuiltEngine();

            using (var batch = sink.BeginBatch("D1", "U1"))
            {
                batch.Write<VarS>(engine);
                batch.Write<VarInt>(engine);
                // 刻意不呼叫 Commit()
            }

            Assert.Equal(0, fake.ExecuteInTransactionCallCount);
            Assert.Empty(fake.ExecutedBatches);
        }

        // (4a) Commit 後再 Write → 丟例外
        [Fact]
        public void Write_AfterCommit_Throws()
        {
            var fake = new FakeDbCtrl();
            var sink = new OracleSolutionSink(fake, "T_SOLUTION");
            var engine = BuiltEngine();

            using var batch = sink.BeginBatch("D1", "U1");
            batch.Write<VarS>(engine);
            batch.Commit();

            Assert.Throws<InvalidOperationException>(() => batch.Write<VarInt>(engine));
        }

        // (4b) 重複 Commit → 丟例外
        [Fact]
        public void Commit_CalledTwice_Throws()
        {
            var fake = new FakeDbCtrl();
            var sink = new OracleSolutionSink(fake, "T_SOLUTION");
            var engine = BuiltEngine();

            using var batch = sink.BeginBatch("D1", "U1");
            batch.Write<VarS>(engine);
            batch.Commit();

            Assert.Throws<InvalidOperationException>(() => batch.Commit());
        }

        // 此處只驗證一次 Commit 共用一個交易；巢狀交易另由 DbCtrlBaseTransactionTests 驗證。
        [Fact]
        public void Commit_OnlyInvokesExecuteInTransactionOnce_NotPerWrite()
        {
            var fake = new FakeDbCtrl();
            var sink = new OracleSolutionSink(fake, "T_SOLUTION");
            var engine = BuiltEngine();

            using var batch = sink.BeginBatch("D1", "U1");
            batch.Write<VarS>(engine);
            batch.Write<VarInt>(engine);
            batch.Write<VarDG>(engine);
            batch.Commit();

            Assert.Equal(1, fake.ExecuteInTransactionCallCount);
        }
    }
}
