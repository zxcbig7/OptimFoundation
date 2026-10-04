using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Integration
{
    /// <summary>
    /// 檢查解值能否匯出、讀回並作為下一次求解的起始解（ExportSolution → ReadSolution / GetSolution → AddMIPStart），以及目標式常數與 LP 的 MIP gap 處理。
    /// 需要 CPLEX DLL 才能執行。
    /// </summary>
    [Collection("Logging")]
    public class SolutionPipelineIntegrationTests
    {
        private static readonly bool CplexAvailable =
            File.Exists(@"C:\IBM\ILOG\CPLEX_Studio2211\cplex\bin\x64_win64\ILOG.CPLEX.dll");

        private static OptEngine NewEngine()
        {
            var engine = new OptEngine(new CplexConfig { TimeLimit = 30 }, new ProjectConfig { EnableSolverLog = false });
            engine.Build();
            return engine;
        }

        // max 3A + 2B + 4C + 10  s.t. 2A + B + 3C <= 4，A/B/C binary → B=C=1，obj = 16
        private static void BuildKnapsack(OptEngine engine)
        {
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" });
            engine.AddLHS(2.0, new VariableB_Pick { S = "A" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "B" });
            engine.AddLHS(3.0, new VariableB_Pick { S = "C" });
            engine.AddRHS(4.0);
            engine.CreateLessEqual("Capacity");
            engine.AddLHS(3.0, new VariableB_Pick { S = "A" });
            engine.AddLHS(2.0, new VariableB_Pick { S = "B" });
            engine.AddLHS(4.0, new VariableB_Pick { S = "C" });
            engine.AddLHS(10.0);
            engine.CreateMaximize();
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

        [Fact(DisplayName = "目標式常數項進 CPLEX：min x + 5 s.t. x >= 3 → 8")]
        public void ObjectiveConstant_IsPartOfSolverObjective()
        {
            if (!CplexAvailable) return;
            using var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.AddRHS(3.0);
            engine.CreateGreaterEqual("LB");
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.AddLHS(5.0);
            engine.CreateMinimize();

            Assert.True(engine.Solve());
            Assert.Equal(8.0, engine.GetObjectiveValue(), precision: 6);
            Assert.Equal(8.0, engine.LastMetrics.ObjectiveValue, precision: 6);
        }

        [Fact(DisplayName = "LP 不讀 MIP gap：gap = 0、bound = 目標值，並留 log")]
        public void LpSolve_SkipsMipGapAndLogs()
        {
            if (!CplexAvailable) return;
            string tag = "LpGap_" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);
            using var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.AddRHS(3.0);
            engine.CreateGreaterEqual("LB");
            engine.AddLHS(2.0, new VarS { S = "x" });
            engine.CreateMinimize();

            Assert.True(engine.Solve());
            Assert.Equal(0.0, engine.MIPGap);
            Assert.Equal(6.0, engine.BestObjValue, precision: 6);
            Assert.Contains("[Bound And Gap] 模型類型=LP BestBound=6 MIPGap=NA 原因=非MILP", ReadLog(tag));
        }

        [Fact(DisplayName = "MILP 讀 MIP gap 並留 log")]
        public void MipSolve_ReadsMipGapAndLogs()
        {
            if (!CplexAvailable) return;
            string tag = "MipGap_" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);
            using var engine = NewEngine();
            BuildKnapsack(engine);

            Assert.True(engine.Solve());
            Assert.Equal(16.0, engine.GetObjectiveValue(), precision: 6);
            Assert.Contains("[Bound And Gap] 模型類型=BP", ReadLog(tag));
        }

        [Fact(DisplayName = "檔案 pipeline：ExportSolution(.sol) → 下一個 engine ReadSolution 成為 MIP start")]
        public void ExportSolution_ThenReadSolution_LoadsMipStart()
        {
            if (!CplexAvailable) return;
            string fileName = $"PipelineStart_{Guid.NewGuid():N}.sol";

            using (var stage1 = NewEngine())
            {
                BuildKnapsack(stage1);
                Assert.True(stage1.Solve());
                string path = stage1.ExportSolution(fileName);
                Assert.True(File.Exists(path));
            }

            using var stage2 = NewEngine();
            BuildKnapsack(stage2);
            Assert.True(stage2.ReadSolution(fileName) >= 1);
            Assert.True(stage2.Solve());
            Assert.Equal(16.0, stage2.GetObjectiveValue(), precision: 6);
        }

        [Fact(DisplayName = "OptModel pipeline：ReadSolution 步驟在建模後套用")]
        public void OptModel_ReadSolutionStep_AppliesAfterModelSteps()
        {
            if (!CplexAvailable) return;
            string fileName = $"PipelineModel_{Guid.NewGuid():N}.sol";
            using (var stage1 = NewEngine())
            {
                BuildKnapsack(stage1);
                Assert.True(stage1.Solve());
                stage1.ExportSolution(fileName);
            }

            string project = "PipelineProject_" + Guid.NewGuid().ToString("N");
            var model = new OptModel("Knapsack")
                .AddVariables(e => e.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" }))
                .AddConstraints(e =>
                {
                    e.AddLHS(2.0, new VariableB_Pick { S = "A" });
                    e.AddLHS(1.0, new VariableB_Pick { S = "B" });
                    e.AddLHS(3.0, new VariableB_Pick { S = "C" });
                    e.AddRHS(4.0);
                    e.CreateLessEqual("Capacity");
                })
                .AddObjective(e =>
                {
                    e.AddLHS(3.0, new VariableB_Pick { S = "A" });
                    e.AddLHS(2.0, new VariableB_Pick { S = "B" });
                    e.AddLHS(4.0, new VariableB_Pick { S = "C" });
                    e.CreateMaximize();
                })
                .ReadSolution(fileName);

            using var optProject = new OptProject(project, retentionDays: 0)
                .LoadConfig(new ProjectConfig { EnableSolverLog = false });
            Assert.True(optProject.Solve(model, new CplexConfig()));
            Assert.Equal(6.0, optProject.Engine.GetObjectiveValue(), precision: 6);
            Assert.Contains("[解檔讀入完成]", ReadLog(project));
        }

        [Fact(DisplayName = "記憶體 pipeline：前段 GetSolution → 後段 AddMIPStart")]
        public void GetSolution_ThenAddMipStart_AppliesAllVariables()
        {
            if (!CplexAvailable) return;
            IReadOnlyDictionary<string, double> previous;
            using (var stage1 = NewEngine())
            {
                BuildKnapsack(stage1);
                Assert.True(stage1.Solve());
                previous = stage1.GetSolution();
            }

            using var stage2 = NewEngine();
            BuildKnapsack(stage2);
            stage2.MipStartEffort = ILOG.CPLEX.Cplex.MIPStartEffort.CheckFeas;
            Assert.Equal(3, stage2.AddMIPStart(previous, "fromStage1"));
            Assert.True(stage2.Solve());
            Assert.Equal(16.0, stage2.GetObjectiveValue(), precision: 6);
        }

        [Fact(DisplayName = "ReadSolution：Build 前 / 檔案不存在 / 無解時 ExportSolution 都丟例外")]
        public void SolutionFileApis_GuardInvalidState()
        {
            if (!CplexAvailable) return;
            using (var unbuilt = new OptEngine(new CplexConfig(), new ProjectConfig { EnableSolverLog = false }))
                Assert.Throws<InvalidOperationException>(() => unbuilt.ReadSolution("any.sol"));

            using var engine = NewEngine();
            BuildKnapsack(engine);
            Assert.Throws<FileNotFoundException>(() => engine.ReadSolution("no_such_start.sol"));
            Assert.Throws<InvalidOperationException>(() => engine.ExportSolution("unsolved.sol"));
        }
    }
}
