using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    /// <summary>檢查目標式常數是否保留、忽略右側暫存算式時是否警告、無界限值的表示方式，以及 AddMIPStart 的共用處理。</summary>
    [Collection("Logging")]
    public class PoolSemanticsAndMipStartTests
    {
        private static MockEngine NewEngine()
        {
            var e = new MockEngine();
            e.Build();
            return e;
        }

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

        // ── 目標式常數項 ───────────────────────────────────────────────────

        [Fact(DisplayName = "目標式常數項：AddLHS(常數) 帶進 SetObjective")]
        public void CreateMinimize_PassesLhsConstantToSolver()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.AddLHS(5.0);
            engine.AddLHS(2.5);
            engine.CreateMinimize();

            Assert.Equal(7.5, engine.ObjectiveConstant);
            Assert.Equal(7.5, engine.ObjectiveConstantResult);
            Assert.False(engine.HasPool);
        }

        [Fact(DisplayName = "目標式常數項：soft penalty 重設目標式時常數項保留")]
        public void SoftPenalty_KeepsObjectiveConstant()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.AddLHS(4.0);
            engine.CreateMinimize();

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateGreaterEqualSoft(3.0, 10.0);

            Assert.Equal(4.0, engine.ObjectiveConstantResult);
        }

        [Fact(DisplayName = "目標式遇到 RHS pool：warn 並捨棄")]
        public void CreateMinimize_WithRhsPool_WarnsAndDiscards()
        {
            string tag = StartLog("ObjectiveRhsIgnored");
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x", "y" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.AddRHS(2.0, new VarS { S = "y" });
            engine.AddRHS(3.0);
            engine.CreateMinimize();

            Assert.Equal(0, engine.ObjectiveConstantResult);
            Assert.False(engine.HasPool);
            Assert.Contains("[POOL_RHS_IGNORED] 右側 pool 不被採用 | operation=CreateMinimize name=<objective> rhsTerms=1 rhsConst=3 reason=objective_uses_lhs_only result=rhs_discarded", ReadLog(tag));
        }

        // ── CreateRange 與 RHS pool ────────────────────────────────────────

        [Fact(DisplayName = "CreateRange 遇到 RHS pool：warn、仍建立、pool 清空")]
        public void CreateRange_WithRhsPool_WarnsAndStillBuilds()
        {
            string tag = StartLog("RangeRhsIgnored");
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x", "y" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.AddRHS(1.0, new VarS { S = "y" });
            Assert.True(engine.CreateRange(0, 10, "Band@x"));

            Assert.Contains("Band@x", engine.BuiltConstraints);
            Assert.False(engine.HasPool);
            Assert.Contains("[POOL_RHS_IGNORED] 右側 pool 不被採用 | operation=CreateRange name=Band@x rhsTerms=1 rhsConst=0 reason=range_uses_lhs_only result=rhs_discarded", ReadLog(tag));
        }

        [Fact(DisplayName = "CreateRange 沒有 RHS pool：不發 warn")]
        public void CreateRange_WithoutRhsPool_NoWarn()
        {
            string tag = StartLog("RangeRhsClean");
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateRange(0, 10, "Band@x");

            Assert.DoesNotContain("[POOL_RHS_IGNORED]", ReadLog(tag));
        }

        // ── 無上限統一 1E20 ────────────────────────────────────────────────

        [Fact(DisplayName = "無上限統一為 OptBounds.Infinity = 1E20（BuildVars 與 soft 彈性變數）")]
        public void UnboundedVariables_UseCplexInfinity()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableC_Amt>(new[] { "A" });
            engine.BuildVars<VariableI_Cnt>(new[] { "A" });
            engine.AddLHS(1.0, new VariableC_Amt { S = "A" });
            engine.CreateLessEqualSoft(5.0, 1.0, "Cap@A");

            Assert.Equal(1E20, OptBounds.Infinity);
            Assert.All(engine.BuiltVars, v => Assert.True(v.Ub == OptBounds.Infinity, $"{v.Name} ub={v.Ub}"));
        }

        // ── AddMIPStart ────────────────────────────────────────────────────

        [Fact(DisplayName = "AddMIPStart：名稱對應到變數後交給 solver，未知名稱略過")]
        public void AddMIPStart_ResolvesNamesAndSkipsUnknown()
        {
            string tag = StartLog("MipStartUnknown");
            var engine = NewEngine();
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B" });

            int applied = engine.AddMIPStart(new Dictionary<string, double>
            {
                ["VariableB_Pick@A"] = 1,
                ["VariableB_Pick@B"] = 0,
                ["VariableB_Pick@Z"] = 1,
            }, "warm");

            Assert.Equal(2, applied);
            var start = Assert.Single(engine.MipStarts);
            Assert.Equal("warm", start.Name);
            Assert.Equal(new[] { ("VariableB_Pick@A", 1.0), ("VariableB_Pick@B", 0.0) }, start.Entries.OrderBy(e => e.Var));
            Assert.Contains("[MIP_START_UNKNOWN_VARIABLE] MIP start 含模型內不存在的變數 | name=warm unknown=1 sample=VariableB_Pick@Z", ReadLog(tag));
        }

        [Fact(DisplayName = "AddMIPStart：LP 模型 warn 後略過")]
        public void AddMIPStart_LpModel_Skips()
        {
            string tag = StartLog("MipStartLp");
            var engine = NewEngine();
            engine.BuildVars<VariableC_Amt>(new[] { "A" });

            int applied = engine.AddMIPStart(new Dictionary<string, double> { ["VariableC_Amt@A"] = 3 });

            Assert.Equal(0, applied);
            Assert.Empty(engine.MipStarts);
            Assert.Contains("[MIP_START_SKIPPED] 略過 MIP start | name=<auto> values=1 reason=model_is_lp result=skipped", ReadLog(tag));
        }

        [Fact(DisplayName = "AddMIPStart：沒有任何名稱對得上 → 略過不呼叫 solver")]
        public void AddMIPStart_NoMatchingVariable_Skips()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableB_Pick>(new[] { "A" });

            int applied = engine.AddMIPStart(new Dictionary<string, double> { ["Other@A"] = 1 });

            Assert.Equal(0, applied);
            Assert.Empty(engine.MipStarts);
        }

        [Fact(DisplayName = "AddMIPStart：values 為 null 丟 ArgumentNullException")]
        public void AddMIPStart_NullValues_Throws()
        {
            var engine = NewEngine();
            Assert.Throws<ArgumentNullException>(() => engine.AddMIPStart(null));
        }
    }
}
