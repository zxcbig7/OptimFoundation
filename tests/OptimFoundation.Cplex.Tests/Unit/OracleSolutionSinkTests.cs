using System;
using System.Linq;
using OptimFoundation.Cplex.Tests.Mocks;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Db.Oracle;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // OracleSolutionSink 的批次/transaction 語意：用假 IDbCtrl 驗證 wiring，不碰真 Oracle。
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
            Assert.Empty(fake.ExecutedCommands); // 不再逐列呼叫 Execute
            Assert.Equal(3, fake.ExecutedBatches.Count); // VarS / VarInt / VarDG 各一次 ExecuteBatch
            Assert.Equal(2 + 1 + 1, fake.ExecutedBatches.Sum(b => b.rows.Length)); // 列數加總不變
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

        // 巢狀 ExecuteInTransaction：已在交易中再次呼叫 → 直接執行 work，不重複開始/提交（用真 OracleDBCtrl
        // 反而需要真連線，這裡只驗證 FakeDbCtrl 本身沒有這條語意——交由下方針對 OracleDBCtrl 的行為另行人工複查，
        // 本測試改用 FakeDbCtrl 驗證「同一批次只呼叫一次」已由測試 (1) 覆蓋，此處補頂層呼叫不重入的計數穩定性。
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
