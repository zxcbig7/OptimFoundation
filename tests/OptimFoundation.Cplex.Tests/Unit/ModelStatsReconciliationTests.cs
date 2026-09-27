using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    /// <summary>
    /// 模型統計對帳（框架建模記帳 vs solver 模型實際）的判定與 log。
    /// MockEngine 模擬 CPLEX 的收錄規則（沒被引用的變數不算進模型），並能注入「solver 端有框架不知道的內容」。
    /// </summary>
    [Collection("Logging")]
    public class ModelStatsReconciliationTests
    {
        private static string StartLog(string prefix)
        {
            string tag = prefix + "_" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);
            return tag;
        }

        private static string ReadLog(string tag)
        {
            string file = Directory.GetFiles(FolderDir.Log.GetPath(), $"{tag}_*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }

        private static IReadOnlyList<string> MismatchWarnings(string log) =>
            log.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("| WARN  |", StringComparison.Ordinal)
                    && line.Contains("[MODEL_STATS_MISMATCH]", StringComparison.Ordinal))
                .ToList();

        // 3 binary + 1 integer + 1 continuous，全部被引用；一條一般限制式、一條範圍、一條軟性（多一個 Surplus 連續變數）、maximize 目標式
        private static MockEngine BuildConsistentModel()
        {
            var engine = new MockEngine();
            engine.Build();
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" });
            engine.BuildVars<VariableI_Cnt>(new[] { "A" });
            engine.BuildVars<VariableC_Amt>(new[] { "A" });

            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "B" });
            engine.AddRHS(1.0);
            engine.CreateLessEqual("PickLimit");

            engine.AddLHS(1.0, new VariableI_Cnt { S = "A" });
            engine.CreateRange(0, 5, "CntRange");

            // 目標式先建：軟性限制式的 penalty 正負號取決於當下的目標式方向
            engine.AddLHS(3.0, new VariableB_Pick { S = "C" });
            engine.AddLHS(1.0, new VariableC_Amt { S = "A" });
            engine.CreateMaximize();

            engine.AddLHS(1.0, new VariableC_Amt { S = "A" });
            engine.CreateLeSoft(10, 2.0, "AmtBudget");
            return engine;
        }

        [Fact(DisplayName = "情境 1：兩種統計完美符合 → 逐項相等、log 印對帳表與 MODEL_STATS_MATCH")]
        public void PerfectMatch_AllItemsEqual_LogsMatch()
        {
            string tag = StartLog("ModelStatsMatch");
            var engine = BuildConsistentModel();

            Assert.True(engine.Solve());

            var report = engine.ModelStats;
            Assert.NotNull(report);
            Assert.True(report!.IsMatch, report.Summary);
            Assert.Equal("MATCH", report.Summary);
            Assert.Equal("Authored", report.Source);

            Assert.Equal(6, report.Framework.Variables);
            Assert.Equal(3, report.Framework.Binary);
            Assert.Equal(1, report.Framework.Integer);
            Assert.Equal(2, report.Framework.Continuous);
            Assert.Equal(3, report.Framework.Constraints);
            Assert.Equal(ObjectiveSense.Maximize, report.Framework.Objective);

            Assert.Equal(report.Framework.Variables, report.IndexedVariables);
            Assert.Equal(report.Framework.Variables, report.Solver.Variables);
            Assert.Equal(report.Framework.Binary, report.Solver.Binary);
            Assert.Equal(report.Framework.Integer, report.Solver.Integer);
            Assert.Equal(report.Framework.Continuous, report.Solver.Continuous);
            Assert.Equal(report.Framework.Constraints, report.IndexedConstraints);
            Assert.Equal(report.Framework.Constraints, report.Solver.Constraints);
            Assert.Equal(report.Framework.Objective, report.Solver.Objective);

            string log = ReadLog(tag);
            Assert.Contains("[模型統計對帳] 來源=Authored 變數 記帳=6 索引=6 solver=6", log);
            Assert.Contains("[模型統計對帳] 限制式 記帳=3 索引=3 solver=3", log);
            Assert.Contains("[MODEL_STATS_MATCH]", log);
            Assert.Empty(MismatchWarnings(log));
        }

        [Fact(DisplayName = "對帳在 Solve 前也能手動呼叫，且不寫 log、不改狀態")]
        public void ReconcileModelStats_BeforeSolve_IsPure()
        {
            string tag = StartLog("ModelStatsPure");
            Logging.Info("marker");
            var engine = BuildConsistentModel();

            var report = engine.ReconcileModelStats();

            Assert.True(report.IsMatch);
            Assert.Null(engine.ModelStats);
            Assert.DoesNotContain("[模型統計對帳]", ReadLog(tag));
        }

        [Fact(DisplayName = "情境 2a：宣告沒引用的變數 → Variables / Binary 落差，WARN 點名未引用變數")]
        public void UnreferencedVariable_LogsMismatchWithSampleName()
        {
            string tag = StartLog("ModelStatsUnreferenced");
            var engine = new MockEngine();
            engine.Build();
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "B" });
            engine.CreateLessEqual("PickLimit");

            Assert.True(engine.Solve());

            var report = engine.ModelStats!;
            Assert.False(report.IsMatch);
            Assert.Equal("MISMATCH:Variables,Binary", report.Summary);
            var variables = report.Mismatches.Single(m => m.Item == "Variables");
            Assert.Equal("3", variables.Framework);
            Assert.Equal("3", variables.Index);
            Assert.Equal("2", variables.Solver);
            Assert.Contains("沒被任何限制式或目標式引用", variables.Reason);
            Assert.Contains("VariableB_Pick@C", variables.Reason);
            Assert.Contains("與 Variables 列同因", report.Mismatches.Single(m => m.Item == "Binary").Reason);

            var warnings = MismatchWarnings(ReadLog(tag));
            Assert.Equal(2, warnings.Count);
            string line = warnings.Single(w => w.Contains("item=Variables"));
            Assert.Contains("framework=3 index=3 solver=2", line);
            Assert.Contains("sample=VariableB_Pick@C", line);
            Assert.Contains("result=continued", line);
        }

        [Fact(DisplayName = "情境 2b：solver 有框架不認得的限制式 → Constraints 落差，WARN 指出繞過框架")]
        public void SolverOnlyConstraint_LogsBypassReason()
        {
            string tag = StartLog("ModelStatsSolverOnly");
            var engine = BuildConsistentModel();
            engine.ExtraSolverConstraints = 1;

            engine.Solve();

            var mismatch = Assert.Single(engine.ModelStats!.Mismatches);
            Assert.Equal("Constraints", mismatch.Item);
            Assert.Equal("3", mismatch.Framework);
            Assert.Equal("3", mismatch.Index);
            Assert.Equal("4", mismatch.Solver);
            Assert.Contains("繞過框架直接加進 solver 模型", mismatch.Reason);

            string line = Assert.Single(MismatchWarnings(ReadLog(tag)));
            Assert.Contains("item=Constraints framework=3 index=3 solver=4", line);
        }

        [Fact(DisplayName = "情境 2c：繞過 pool 直接呼叫 primitive → 索引比建模記帳多，WARN 指出沒經過建模入口")]
        public void UnledgeredConstraint_LogsLedgerIndexGap()
        {
            string tag = StartLog("ModelStatsUnledgered");
            var engine = BuildConsistentModel();
            engine.AddUnledgeredConstraint("RawRow", "VariableB_Pick@A");

            engine.Solve();

            var mismatch = Assert.Single(engine.ModelStats!.Mismatches);
            Assert.Equal("Constraints", mismatch.Item);
            Assert.Equal("3", mismatch.Framework);
            Assert.Equal("4", mismatch.Index);
            Assert.Equal("4", mismatch.Solver);
            Assert.Contains("框架索引比建模記帳多 1 條", mismatch.Reason);

            Assert.Contains("item=Constraints framework=3 index=4 solver=4", Assert.Single(MismatchWarnings(ReadLog(tag))));
        }

        [Fact(DisplayName = "情境 2d：solver 模型含框架不索引的元素 → SpecialElements 落差")]
        public void SpecialElements_LogsUncoveredElements()
        {
            string tag = StartLog("ModelStatsSpecial");
            var engine = BuildConsistentModel();
            engine.SolverSpecialElements = 2;

            engine.Solve();

            var mismatch = Assert.Single(engine.ModelStats!.Mismatches);
            Assert.Equal("SpecialElements", mismatch.Item);
            Assert.Contains("框架不建立也不索引", mismatch.Reason);
            Assert.Contains("SOS=2", mismatch.Reason);
            Assert.Contains("item=SpecialElements framework=0 solver=2", Assert.Single(MismatchWarnings(ReadLog(tag))));
        }

        [Fact(DisplayName = "情境 2e：目標式方向不一致 → Objective 落差")]
        public void ObjectiveSenseMismatch_Logged()
        {
            string tag = StartLog("ModelStatsObjective");
            var engine = BuildConsistentModel();
            engine.SolverObjectiveOverride = ObjectiveSense.Minimize;

            engine.Solve();

            var mismatch = Assert.Single(engine.ModelStats!.Mismatches);
            Assert.Equal("Objective", mismatch.Item);
            Assert.Equal("Maximize", mismatch.Framework);
            Assert.Equal("Minimize", mismatch.Solver);
            Assert.Contains("方向不一致", mismatch.Reason);
            Assert.Contains("item=Objective framework=Maximize solver=Minimize", Assert.Single(MismatchWarnings(ReadLog(tag))));
        }

        [Fact(DisplayName = "Build() 重來會清空建模記帳，前一輪的數量不會殘留")]
        public void Build_ResetsLedger()
        {
            var engine = BuildConsistentModel();
            engine.Build();

            var report = engine.ReconcileModelStats();

            Assert.Equal(0, report.Framework.Variables);
            Assert.Equal(0, report.Framework.Constraints);
            Assert.Null(report.Framework.Objective);
        }
    }
}
