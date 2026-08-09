using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Integration
{
    /// <summary>
    /// 需要 CPLEX DLL 才能執行。若 DLL 不存在，全部 Skip。
    /// 執行：dotnet test --filter Category=Integration
    /// </summary>
    [Collection("Logging")]
    public class OptEngineIntegrationTests
    {
        private static readonly bool CplexAvailable =
            File.Exists(@"C:\IBM\ILOG\CPLEX_Studio2211\cplex\bin\x64_win64\ILOG.CPLEX.dll");

        private static OptEngine BuildEngine(double? timeLimit = 30)
        {
            var config = new CplexConfig
            {
                TimeLimit = timeLimit,
            };
            var projectConfig = new ProjectConfig { EnableSolverLog = false };
            var engine = new OptEngine(config, projectConfig);
            engine.Build();
            return engine;
        }

        [Fact(DisplayName = "OptModel 設定解析：ctor 專案名與保留天數優先於 ProjectConfig")]
        public void OptModel_CtorProjectNameAndRetentionDays_TakePrecedenceOverProjectConfig()
        {
            if (!CplexAvailable) return;

            string projectName = "CtorPriority_" + Guid.NewGuid().ToString("N");
            var projectConfig = new ProjectConfig
            {
                ProjectName = "ConfigName",
                RetentionDays = 9999,
                EnableSolverLog = false,
            };

            var model = new OptModel(projectName)
                .AddVariables(engine => engine.BuildBVs<VarS>(new[] { "x" }))
                .AddObjective(engine =>
                {
                    engine.AddLHS(1.0, new VarS { S = "x" });
                    engine.CreateMinimize();
                });
            using var project = new OptProject(model, projectName, retentionDays: 0)
                .UseConfig(() => new CplexConfig { TimeLimit = 30 })
                .UseConfig(() => projectConfig);

            Assert.True(project.Execute());
            Assert.Equal(projectName, project.Engine.ModelName);

            string logFile = Directory.GetFiles(FolderDir.Log.GetPath(), $"{projectName}_*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();
            using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            Assert.Contains(
                $"[EffectiveConfig] ProjectName={projectName}(ctor) RetentionDays=0* " +
                "SolverLog=OFF* ExportLP=OFF ExportMPS=OFF ExportSol=OFF",
                reader.ReadToEnd());
        }

        // ── 基本求解 ────────────────────────────────────────────────────────

        [Fact(DisplayName = "簡單 LP：min x s.t. x >= 3，解 = 3")]
        public void SimpleLP_MinX_GreaterEqual3_SolvesOptimal()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();

            engine.BuildCVs<VarS>(new List<string> { "x" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateGreatEqual(3.0, "LB");

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateMinimize();

            bool solved = engine.Solve();

            Assert.True(solved);
            Assert.Equal(SolveStatus.Optimal, engine.Status);
            Assert.Equal(3.0, engine.GetObjectiveValue(), precision: 5);
        }

        [Fact]
        public void SolverLog_ConsoleDisabled_IsStillPersisted()
        {
            if (!CplexAvailable) return;
            string tag = "SolverLogFileOnly_" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);
            using var engine = BuildEngine();
            engine.BuildCVs<VarS>(new[] { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateMinimize();

            engine.Solve();

            string file = Directory.GetFiles(FolderDir.Log.GetPath(), $"{tag}_*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            Assert.Contains("[CPLEX Log]", reader.ReadToEnd());
        }

        [Fact(DisplayName = "簡單 MILP：Binary x，min -x s.t. x <= 1，解 = 1")]
        public void SimpleMILP_BinaryVar_SolvesOptimal()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();

            engine.BuildBVs<VarS>(new List<string> { "x" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateLessEqual(1.0, "UB");

            engine.AddLHS(-1.0, new VarS { S = "x" });  // minimize -x → x = 1
            engine.CreateMinimize();

            bool solved = engine.Solve();

            Assert.True(solved);
            Assert.Equal(SolveStatus.Optimal, engine.Status);
            Assert.Equal(-1.0, engine.GetObjectiveValue(), precision: 5);
            Assert.Equal(1.0, engine.GetVariableValue("VarS@x"), precision: 5);
        }

        // ── SupportsSoftConstraints ────────────────────────────────────────

        [Fact(DisplayName = "OptEngine 支援 SoftConstraints")]
        public void OptEngine_SupportsSoftConstraints_IsTrue()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();
            Assert.True(engine.SupportsSoftConstraints);
        }

        [Fact(DisplayName = "軟性 Ge：違反量以 penalty 計入目標式（通用實作）")]
        public void SoftConstraint_Ge_PenalizesViolation()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();

            engine.BuildCVs<VarS>(0, 1, new List<string> { "x" });   // x ∈ [0,1]

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateMinimize();                                 // 先建目標式 min x

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateGeSoft(5.0, 10.0);                          // 軟性 x >= 5，penalty 10

            bool solved = engine.Solve();

            // dn = 5 - x，obj = x + 10·dn = 50 - 9x，min → x=1 → obj = 41
            Assert.True(solved);
            Assert.Equal(SolveStatus.Optimal, engine.Status);
            Assert.Equal(41.0, engine.GetObjectiveValue(), precision: 4);
            Assert.Equal(1.0, engine.GetVariableValue("VarS@x"), precision: 4);
        }

        // ── 變數建立 ───────────────────────────────────────────────────────

        [Fact(DisplayName = "BuildBVs 建立正確數量的變數")]
        public void BuildBVs_Creates_CorrectVarCount()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();

            var dates = Enumerable.Range(1, 10).Select(d => new DateTime(2026, 1, d)).ToList();
            var emps  = Enumerable.Range(1, 5).Select(i => $"E{i}").ToList();

            engine.BuildBVs<VarDG>(dates, emps);

            Assert.Equal(50, engine.VariableCount);  // 10 × 5
        }

        [Fact(DisplayName = "GetSetVarNames 回傳正確格式")]
        public void GetSetVarNames_ReturnsExpectedKeys()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();

            engine.BuildBVs<VarS>(new List<string> { "A", "B" });
            var names = engine.GetSetVarNames<VarS>();

            Assert.Contains("VarS@A", names);
            Assert.Contains("VarS@B", names);
        }

        // ── Infeasible + IIS ───────────────────────────────────────────────

        [Fact(DisplayName = "Infeasible 模型 → Status = Infeasible")]
        public void Infeasible_Model_ReturnsInfeasibleStatus()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();

            engine.BuildCVs<VarS>(new List<string> { "x" });

            // x >= 10 AND x <= 1 → 矛盾
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateGreatEqual(10.0, "LB");

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateLessEqual(1.0, "UB");

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateMinimize();

            bool solved = engine.Solve();

            Assert.False(solved);
            Assert.Equal(SolveStatus.Infeasible, engine.Status);
        }

        // ── TimeLimit ──────────────────────────────────────────────────────

        [Fact(DisplayName = "TimeLimit 命中 → Status 不是 Error")]
        public void TimeLimit_Hit_StatusIsNotError()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine(timeLimit: 0.001);  // 1ms → 一定 timeout

            engine.BuildBVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateMinimize();

            engine.Solve();

            Assert.NotEqual(SolveStatus.Error, engine.Status);
        }

        // ── GetSetVarValues ────────────────────────────────────────────────

        [Fact(DisplayName = "GetSetVarValues 在 Optimal 後回傳解值")]
        public void GetSetVarValues_AfterSolve_ReturnsValues()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();

            engine.BuildBVs<VarS>(new List<string> { "a", "b" });

            // a + b >= 1, min a + b
            engine.AddLHS(1.0, new VarS { S = "a" });
            engine.AddLHS(1.0, new VarS { S = "b" });
            engine.CreateGreatEqual(1.0, "Sum");

            engine.AddLHS(1.0, new VarS { S = "a" });
            engine.AddLHS(1.0, new VarS { S = "b" });
            engine.CreateMinimize();

            engine.Solve();

            var values = engine.GetSetVarValues<VarS>();
            Assert.Equal(2, values.Count);
            Assert.Equal(1.0, values.Values.Sum(), precision: 5);  // 最優解總和 = 1
        }

        [Fact]
        public void OptProject_OnSolved_RunsExactlyOnceAfterSuccessfulSolve()
        {
            if (!CplexAvailable) return;
            int calls = 0;
            var model = new OptModel("on-solved-success")
                .AddVariables(e => e.BuildCVs<VarS>(new[] { "x" }))
                .AddObjective(e =>
                {
                    e.AddLHS(1.0, new VarS { S = "x" });
                    e.CreateMinimize();
                });

            using var project = new OptProject(model, retentionDays: 0)
                .UseConfig(() => new ProjectConfig { EnableSolverLog = false })
                .OnSolved(_ => calls++);

            Assert.True(project.Execute());
            Assert.Equal(1, calls);
        }

        [Fact]
        public void OptProject_OnSolved_DoesNotRunAfterFailedSolve()
        {
            if (!CplexAvailable) return;
            int calls = 0;
            var model = new OptModel("on-solved-failure")
                .AddVariables(e => e.BuildCVs<VarS>(new[] { "x" }))
                .AddObjective(e =>
                {
                    e.AddLHS(1.0, new VarS { S = "x" });
                    e.CreateMinimize();
                })
                .AddConstraints(e =>
                {
                    e.AddLHS(1.0, new VarS { S = "x" });
                    e.CreateGreatEqual(1.0, "LB");
                    e.AddLHS(1.0, new VarS { S = "x" });
                    e.CreateLessEqual(0.0, "UB");
                });

            using var project = new OptProject(model, retentionDays: 0)
                .UseConfig(() => new ProjectConfig { EnableSolverLog = false })
                .OnSolved(_ => calls++);

            Assert.False(project.Execute());
            Assert.Equal(SolveStatus.Infeasible, project.Engine.Status);
            Assert.Equal(0, calls);
        }

        [Fact]
        public void EmptyOptModel_Execute_WarnsAndStillSolves()
        {
            if (!CplexAvailable) return;
            string tag = "empty-execute-" + Guid.NewGuid().ToString("N");
            using var project = new OptProject(new OptModel(tag), tag, retentionDays: 0)
                .UseConfig(() => new ProjectConfig { EnableSolverLog = false });

            Assert.True(project.Execute());
            Assert.Equal(SolveStatus.Optimal, project.Engine.Status);
            Assert.Contains("[MODEL_EMPTY]", ReadLatestLog(tag));
        }

        [Fact]
        public void EmptyOptModel_Run_WarnsAndStillSolves()
        {
            if (!CplexAvailable) return;
            string tag = "empty-run-" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);

            try
            {
                Experiment result = new OptExperiment(tag, "empty model")
                    .AddTrial(new OptModel("Empty"), "default", new CplexConfig { TimeLimit = 30 })
                    .Run();

                Trial trial = Assert.Single(result.Trials);
                Assert.Equal(SolveStatus.Optimal, trial.Metrics.Status);
                Assert.Contains("[MODEL_EMPTY]", ReadLatestLog(tag));
            }
            finally
            {
                foreach (string suffix in new[] { ".csv", ".json", "-trajectory.csv" })
                {
                    string path = FolderDir.Experiment.GetFilePath(tag + suffix);
                    if (File.Exists(path)) File.Delete(path);
                }
            }
        }

        private static string ReadLatestLog(string tag)
        {
            string path = Directory.GetFiles(FolderDir.Log.GetPath(), $"{tag}_*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
