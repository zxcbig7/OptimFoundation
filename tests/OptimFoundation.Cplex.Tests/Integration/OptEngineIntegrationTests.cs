using System.Reflection;
using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Integration
{
    /// <summary>
    /// 缺少 CPLEX DLL 時直接返回，不執行檢查。
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

        [Fact(DisplayName = "Production：engine 名稱與 log 檔名都以專案名為根，建立專案時印出專案設定")]
        public void Production_UsesProjectNameForEngineAndLog()
        {
            if (!CplexAvailable) return;

            string projectName = "SolveNaming_" + Guid.NewGuid().ToString("N");

            var model = new OptModel(projectName)
                .AddVariables(engine => engine.BuildBVs<VarS>(new[] { "x" }))
                .AddObjective(engine =>
                {
                    engine.AddLHS(1.0, new VarS { S = "x" });
                    engine.CreateMinimize();
                });
            using var project = new OptProject(projectName, retentionDays: 0);

            project.Production()
                .AddProjectConfig(new ProjectConfig { EnableSolverLog = false })
                .AddModel(model)
                .AddSolverConfig("production", new CplexConfig { TimeLimit = 30 })
                .Run();
            Assert.True(project.IsSuccess);
            Assert.Equal(projectName, project.Engine.ModelName);

            string logFile = Directory.GetFiles(FolderDir.Log.GetPath(), $"{projectName}_*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();
            using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string log = reader.ReadToEnd();
            Assert.Contains($"[專案初始化完成] 名稱={projectName} 保留天數=0 ", log);
            Assert.Contains("[CPLEX 求解日誌設定] 只寫出到框架日誌檔", log);
        }

        [Fact(DisplayName = "CplexConfig 傳 null：沿用 CPLEX 預設值，Build 不會因 Config 為 null 失敗")]
        public void NullCplexConfig_FallsBackToDefaults()
        {
            if (!CplexAvailable) return;

            using var engine = new OptEngine(null, new ProjectConfig { EnableSolverLog = false });

            Assert.IsType<CplexConfig>(engine.SolverConfig);
            engine.Build();
        }


        [Fact(DisplayName = "簡單 LP：min x s.t. x >= 3，解 = 3")]
        public void SimpleLP_MinX_GreaterEqual3_SolvesOptimal()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();

            engine.BuildCVs<VarS>(new List<string> { "x" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateGreaterEqual(3.0, "LB");

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateMinimize();

            bool solved = engine.Solve();

            Assert.True(solved);
            Assert.Equal(SolveStatus.Optimal, engine.Status);
            Assert.Equal(3.0, engine.GetObjectiveValue(), precision: 5);
            Assert.Equal(ModelType.LP, engine.ModelType);
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
            Assert.Contains("[CPLEX 求解日誌]", reader.ReadToEnd());
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
            Assert.Equal(ModelType.BP, engine.ModelType);
        }

        [Fact(DisplayName = "ModelType 取自 solver 模型：宣告後未被任何限制式 / 目標式引用的變數不計入")]
        public void ModelType_DeclaredButUnusedVariables_AreNotInTheModel()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();

            engine.BuildBVs<VarS>(new List<string> { "x" });

            // 框架已登記變數，但尚未有目標式或限制式使用它，因此 CPLEX 模型還不包含它。
            Assert.Equal(1, engine.VariableCount);
            Assert.Equal(ModelType.LP, engine.ModelType);

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateLessEqual(1.0, "UB");

            Assert.Equal(ModelType.BP, engine.ModelType);
        }


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
            engine.CreateMinimize();

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateGreaterEqualSoft(5.0, 10.0);                          // 軟性 x >= 5，penalty 10

            bool solved = engine.Solve();

            // dn = 5 - x，obj = x + 10·dn = 50 - 9x，min → x=1 → obj = 41
            Assert.True(solved);
            Assert.Equal(SolveStatus.Optimal, engine.Status);
            Assert.Equal(41.0, engine.GetObjectiveValue(), precision: 4);
            Assert.Equal(1.0, engine.GetVariableValue("VarS@x"), precision: 4);
        }


        [Fact(DisplayName = "BuildBVs 建立正確數量的變數")]
        public void BuildBVs_Creates_CorrectVarCount()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();

            var dates = Enumerable.Range(1, 10).Select(d => new DateTime(2026, 1, d)).ToList();
            var emps = Enumerable.Range(1, 5).Select(i => $"E{i}").ToList();

            engine.BuildBVs<VarDG>(dates, emps);

            Assert.Equal(50, engine.VariableCount);
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

        [Fact(DisplayName = "AddVariables<T>(sets) 與 AddVariables(e => e.BuildVars<T>(sets)) 建出同一批變數（含多維與零維）")]
        public void AddVariablesGeneric_MatchesLambdaForm()
        {
            if (!CplexAvailable) return;
            var nodes = new List<string> { "A", "B" };
            var dates = new List<DateTime> { new DateTime(2026, 1, 1), new DateTime(2026, 1, 2) };

            var shortForm = new OptModel("Short")
                .AddVariables<VariableC_ArcFlowByDate>(nodes, nodes, dates)
                .AddVariables<VariableC_ZeroDim>();
            var lambdaForm = new OptModel("Lambda")
                .AddVariables(e => e.BuildVars<VariableC_ArcFlowByDate>(nodes, nodes, dates))
                .AddVariables(e => e.BuildVars<VariableC_ZeroDim>());

            using var shortEngine = BuildEngine();
            using var lambdaEngine = BuildEngine();
            ApplyTo(shortForm, shortEngine);
            ApplyTo(lambdaForm, lambdaEngine);

            Assert.Equal(9, shortEngine.VariableCount);
            Assert.Equal(lambdaEngine.GetSetVarNames<VariableC_ArcFlowByDate>(), shortEngine.GetSetVarNames<VariableC_ArcFlowByDate>());
            Assert.Equal(lambdaEngine.GetSetVarNames<VariableC_ZeroDim>(), shortEngine.GetSetVarNames<VariableC_ZeroDim>());
        }

        private static void ApplyTo(OptModel model, OptEngine engine)
            => typeof(OptModel).GetMethod("ApplyTo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, new object[] { engine });


        [Fact(DisplayName = "Infeasible 模型 → Status = Infeasible")]
        public void Infeasible_Model_ReturnsInfeasibleStatus()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();

            engine.BuildCVs<VarS>(new List<string> { "x" });

            // x >= 10 AND x <= 1 → 矛盾
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateGreaterEqual(10.0, "LB");

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateLessEqual(1.0, "UB");

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateMinimize();

            bool solved = engine.Solve();

            Assert.False(solved);
            Assert.Equal(SolveStatus.Infeasible, engine.Status);
        }


        [Fact(DisplayName = "TimeLimit 命中 → Status 不是 Error")]
        public void TimeLimit_Hit_StatusIsNotError()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine(timeLimit: 0.001);  // 時間上限設為 1 毫秒，用來測試極短時限下的處理

            engine.BuildBVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateMinimize();

            engine.Solve();

            Assert.NotEqual(SolveStatus.Error, engine.Status);
        }


        [Fact(DisplayName = "GetSetVarValues 在 Optimal 後回傳解值")]
        public void GetSetVarValues_AfterSolve_ReturnsValues()
        {
            if (!CplexAvailable) return;
            using var engine = BuildEngine();

            engine.BuildBVs<VarS>(new List<string> { "a", "b" });

            // a + b >= 1, min a + b
            engine.AddLHS(1.0, new VarS { S = "a" });
            engine.AddLHS(1.0, new VarS { S = "b" });
            engine.CreateGreaterEqual(1.0, "Sum");

            engine.AddLHS(1.0, new VarS { S = "a" });
            engine.AddLHS(1.0, new VarS { S = "b" });
            engine.CreateMinimize();

            engine.Solve();

            var values = engine.GetSetVarValues<VarS>();
            Assert.Equal(2, values.Count);
            Assert.Equal(1.0, values.Values.Sum(), precision: 5);
        }

        [Fact]
        public void OptProjectProduction_OnSolved_RunsExactlyOnceAfterSuccessfulSolve()
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

            using var project = new OptProject("on-solved-success", retentionDays: 0);

            project.Production()
                .AddProjectConfig(new ProjectConfig { EnableSolverLog = false })
                .AddModel(model)
                .AddSolverConfig("production", new CplexConfig())
                .OnSolved(_ => calls++)
                .Run();
            Assert.True(project.IsSuccess);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void OptProjectProduction_OnSolved_DoesNotRunAfterFailedSolve()
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
                    e.CreateGreaterEqual(1.0, "LB");
                    e.AddLHS(1.0, new VarS { S = "x" });
                    e.CreateLessEqual(0.0, "UB");
                });

            using var project = new OptProject("on-solved-failure", retentionDays: 0);

            project.Production()
                .AddProjectConfig(new ProjectConfig { EnableSolverLog = false })
                .AddModel(model)
                .AddSolverConfig("production", new CplexConfig())
                .OnSolved(_ => calls++)
                .Run();
            Assert.False(project.IsSuccess);
            Assert.Equal(SolveStatus.Infeasible, project.Engine.Status);
            Assert.Equal(0, calls);
            // 失敗也照記一筆 Trial
            Assert.NotNull(project.Trial);
            Assert.Equal(SolveStatus.Infeasible, project.Trial.Metrics.Status);
        }

        [Fact]
        public void EmptyOptModel_Execute_WarnsAndStillSolves()
        {
            if (!CplexAvailable) return;
            string tag = "empty-execute-" + Guid.NewGuid().ToString("N");
            using var project = new OptProject(tag, retentionDays: 0);

            project.Production()
                .AddProjectConfig(new ProjectConfig { EnableSolverLog = false })
                .AddModel(new OptModel(tag))
                .AddSolverConfig("production", new CplexConfig())
                .Run();
            Assert.True(project.IsSuccess);
            Assert.Equal(SolveStatus.Optimal, project.Engine.Status);
            Assert.Contains("[模型為空]", ReadLatestLog(tag));
        }

        [Fact]
        public void EmptyOptModel_Run_WarnsAndStillSolves()
        {
            if (!CplexAvailable) return;
            string tag = "empty-run-" + Guid.NewGuid().ToString("N");

            try
            {
                Experiment result = new OptProject(tag, retentionDays: 0).Experiment("exp", "empty model")
                    .AddTrial(new OptModel("Empty"), "default", new CplexConfig { TimeLimit = 30 })
                    .Run();

                Trial trial = Assert.Single(result.Trials);
                Assert.Equal(SolveStatus.Optimal, trial.Metrics.Status);
                Assert.Contains("[模型為空]", ReadLatestLog($"{tag}-exp_exp"));
            }
            finally
            {
                foreach (string kind in new[] { "trial", "meta", "summary", "trajectory" })
                {
                    string path = FolderDir.Experiment.GetPathFile($"{tag}-{kind}.csv");
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
