using ILOG.Concert;
using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;
using ObjectiveSense = OptimFoundation.Core.ObjectiveSense;

namespace OptimFoundation.Cplex.Tests.Integration
{
    /// <summary>
    /// 模型統計對帳接真的 CPLEX：建模階段（OptEngine / 匯入）與 tuning 階段（OptExperiment 的 Trial / 說明檔 / log）。
    /// 需要 CPLEX DLL，不存在時全部直接結束。
    /// </summary>
    [Collection("Logging")]
    public class ModelStatsIntegrationTests
    {
        private static readonly bool CplexAvailable =
            File.Exists(@"C:\IBM\ILOG\CPLEX_Studio2211\cplex\bin\x64_win64\ILOG.CPLEX.dll");

        private static CplexConfig Config() => new CplexConfig { TimeLimit = 30 };

        private static ProjectConfig Quiet() => new ProjectConfig { EnableSolverLog = false };

        private static OptEngine NewEngine()
        {
            var engine = new OptEngine(Config(), Quiet());
            engine.Build();
            return engine;
        }

        // 繞過框架直接動 CPLEX 模型：模擬「solver 端有框架不知道的內容」
        private sealed class BypassEngine : OptEngine
        {
            public BypassEngine() : base(Config(), Quiet()) { }

            public void AddRawRow()
            {
                INumVar raw = Model.NumVar(0, 5, NumVarType.Float, "RawVar");
                ILinearNumExpr expr = Model.LinearNumExpr();
                expr.AddTerm(1.0, raw);
                Model.AddLe(expr, 3.0).Name = "RawRow";
            }
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

        private static List<string> MismatchWarnings(string log) =>
            log.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("| WARN  |", StringComparison.Ordinal)
                    && line.Contains("[MODEL_STATS_MISMATCH]", StringComparison.Ordinal))
                .ToList();

        // 3 binary + 1 integer + 1 continuous + 1 軟性 Surplus；一般 / 範圍 / 軟性限制式各一條；maximize
        // max 3·Pick_C + Amt_A − 2·Surplus  s.t. Pick_A + Pick_B ≤ 1, 0 ≤ Cnt_A ≤ 5, Amt_A − Surplus ≤ 10 → obj 13
        private static void BuildConsistentModel(OptEngine engine)
        {
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" });
            engine.BuildVars<VariableI_Cnt>(new[] { "A" });
            engine.BuildVars<VariableC_Amt>(new[] { "A" });

            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "B" });
            engine.AddRHS(1.0);
            engine.CreateLessEqual("PickLimit");

            engine.AddLHS(1.0, new VariableI_Cnt { S = "A" });
            engine.CreateRange(0, 5, "CntRange");

            engine.AddLHS(3.0, new VariableB_Pick { S = "C" });
            engine.AddLHS(1.0, new VariableC_Amt { S = "A" });
            engine.CreateMaximize();

            engine.AddLHS(1.0, new VariableC_Amt { S = "A" });
            engine.CreateLeSoft(10, 2.0, "AmtBudget");
        }

        [Fact(DisplayName = "建模階段 情境 1：自建模型（含範圍 / 軟性 / maximize）兩種統計完美符合")]
        public void Authored_PerfectMatch()
        {
            if (!CplexAvailable) return;
            string tag = StartLog("StatsAuthoredMatch");

            using var engine = NewEngine();
            BuildConsistentModel(engine);
            Assert.True(engine.Solve());
            Assert.Equal(13, engine.GetObjectiveValue(), 6);

            var report = engine.ModelStats!;
            Assert.True(report.IsMatch, string.Join(" | ", report.Mismatches.Select(m => $"{m.Item}:{m.Reason}")));
            Assert.Equal(6, report.Solver.Variables);
            Assert.Equal(3, report.Solver.Binary);
            Assert.Equal(1, report.Solver.Integer);
            Assert.Equal(2, report.Solver.Continuous);
            Assert.Equal(3, report.Solver.Constraints);
            Assert.Equal(0, report.Solver.SpecialElements);
            Assert.Equal(ObjectiveSense.Maximize, report.Solver.Objective);
            Assert.Same(report, engine.LastMetrics!.ModelStats);

            string log = ReadLog(tag);
            Assert.Contains("[模型統計對帳] 來源=Authored 變數 記帳=6 索引=6 solver=6", log);
            Assert.Contains("[MODEL_STATS_MATCH]", log);
            Assert.Empty(MismatchWarnings(log));
        }

        // MPS 沒有目標式方向欄位：CPLEX 把 maximize 模型寫成「係數取負的 minimize」，讀回來就是 minimize、目標值反號
        [Theory(DisplayName = "建模階段 情境 1：匯入模型檔兩種統計完美符合，且 ObjectiveSense 依檔案內容同步")]
        [InlineData(".lp", ObjectiveSense.Maximize, 13.0)]
        [InlineData(".mps", ObjectiveSense.Minimize, -13.0)]
        [InlineData(".sav", ObjectiveSense.Maximize, 13.0)]
        public void Imported_PerfectMatch_AndObjectiveSenseSynced(string extension, ObjectiveSense fileSense, double objective)
        {
            if (!CplexAvailable) return;
            string fileName = $"StatsImport_{Guid.NewGuid():N}{extension}";
            using (var source = NewEngine())
            {
                BuildConsistentModel(source);
                source.ExportModelFile(fileName);
            }
            string tag = StartLog("StatsImportMatch");

            using var engine = NewEngine();
            engine.ImportModel(fileName);
            Assert.True(engine.Solve());

            var report = engine.ModelStats!;
            Assert.True(report.IsMatch, string.Join(" | ", report.Mismatches.Select(m => $"{m.Item}:{m.Reason}")));
            Assert.Equal("Imported", report.Source);
            Assert.Equal(fileSense, engine.ObjectiveSense);
            Assert.Equal(fileSense, report.Solver.Objective);
            Assert.Equal(objective, engine.GetObjectiveValue(), 6);
            if (extension == ".sav")
            {
                // .sav 是 CPLEX 原生格式，結構原樣保留；文字格式的範圍限制式會多一個 Rg 輔助變數，不比絕對數
                Assert.Equal(6, report.Solver.Variables);
                Assert.Equal(3, report.Solver.Constraints);
            }

            string log = ReadLog(tag);
            Assert.Contains("[模型統計對帳] 來源=Imported", log);
            Assert.Contains("[MODEL_STATS_MATCH]", log);
            Assert.Empty(MismatchWarnings(log));
        }

        [Fact(DisplayName = "建模階段 情境 2：宣告沒引用的變數 → CPLEX 不收，WARN 點名該變數")]
        public void UnreferencedVariable_RealCplex_LogsMismatch()
        {
            if (!CplexAvailable) return;
            string tag = StartLog("StatsUnreferenced");

            using var engine = NewEngine();
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "B" });
            engine.AddRHS(1.0);
            engine.CreateLessEqual("PickLimit");
            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.CreateMaximize();
            Assert.True(engine.Solve());

            var report = engine.ModelStats!;
            Assert.Equal("MISMATCH:Variables,Binary", report.Summary);
            Assert.Equal(3, report.IndexedVariables);
            Assert.Equal(2, report.Solver.Variables);

            var warnings = MismatchWarnings(ReadLog(tag));
            string line = warnings.Single(w => w.Contains("item=Variables"));
            Assert.Contains("framework=3 index=3 solver=2", line);
            Assert.Contains("沒被任何限制式或目標式引用", line);
            Assert.Contains("sample=VariableB_Pick@C", line);
            Assert.Contains(warnings, w => w.Contains("item=Binary framework=3 solver=2"));
        }

        [Fact(DisplayName = "建模階段 情境 2：繞過框架直接加進 CPLEX 的變數與限制式 → WARN 指出 solver 有框架不認得的內容")]
        public void RawSolverBypass_LogsMismatch()
        {
            if (!CplexAvailable) return;
            string tag = StartLog("StatsBypass");

            using var engine = new BypassEngine();
            engine.Build();
            BuildConsistentModel(engine);
            engine.AddRawRow();
            Assert.True(engine.Solve());

            var report = engine.ModelStats!;
            Assert.Equal("MISMATCH:Variables,Continuous,Constraints", report.Summary);

            var warnings = MismatchWarnings(ReadLog(tag));
            Assert.Contains(warnings, w => w.Contains("item=Variables framework=6 index=6 solver=7") && w.Contains("框架不認得的變數"));
            Assert.Contains(warnings, w => w.Contains("item=Constraints framework=3 index=3 solver=4") && w.Contains("框架不認得的限制式"));
            Assert.Contains(warnings, w => w.Contains("item=Continuous framework=2 solver=3"));
        }

        [Fact(DisplayName = "建模階段 情境 2：模型重建（再次 Configuration）後索引殘留 → WARN 指出索引沒清")]
        public void ReconfiguredModel_StaleIndex_LogsMismatch()
        {
            if (!CplexAvailable) return;
            string tag = StartLog("StatsReconfigured");

            using var engine = NewEngine();
            BuildConsistentModel(engine);
            engine.Configuration(engine.Config);
            engine.Solve();

            var report = engine.ModelStats!;
            var variables = report.Mismatches.Single(m => m.Item == "Variables");
            Assert.Equal("6", variables.Index);
            Assert.Equal("0", variables.Solver);
            Assert.Contains("模型重建", variables.Reason);
            Assert.Contains(report.Mismatches, m => m.Item == "Objective" && m.Solver == "None");

            Assert.Contains(MismatchWarnings(ReadLog(tag)), w => w.Contains("item=Variables") && w.Contains("模型重建"));
        }

        // 實測：CPLEX 移除限制式後仍保留已收進模型的欄，所以 Benders 式的 ResetConstraint + 部分重建兩邊依然一致，不會誤報
        [Fact(DisplayName = "建模階段 情境 1：ResetConstraint 後只用部分變數重建 → CPLEX 保留已收錄的欄，兩種統計仍一致")]
        public void ResetConstraint_PartialRebuild_StaysConsistent()
        {
            if (!CplexAvailable) return;

            using var engine = NewEngine();
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" });
            foreach (var s in new[] { "A", "B", "C" }) engine.AddLHS(1.0, new VariableB_Pick { S = s });
            engine.AddRHS(2.0);
            engine.CreateLessEqual("PickLimit");
            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.CreateMaximize();
            Assert.True(engine.Solve());
            Assert.True(engine.ModelStats!.IsMatch);

            engine.ResetConstraint();
            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "B" });
            engine.AddRHS(1.0);
            engine.CreateLessEqual("PickLimit");
            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.CreateMaximize();
            string tag = StartLog("StatsResetConstraint");
            Assert.True(engine.Solve());

            var report = engine.ModelStats!;
            Assert.True(report.IsMatch, report.Summary);
            Assert.Equal(3, report.Solver.Variables);
            Assert.Equal(1, report.Solver.Constraints);

            string log = ReadLog(tag);
            Assert.Contains("[MODEL_STATS_MATCH]", log);
            Assert.Empty(MismatchWarnings(log));
        }

        [Fact(DisplayName = "建模階段 情境 2：匯入含 SOS 的模型檔 → SpecialElements 落差，WARN 後照常求解")]
        public void ImportedSos_LogsSpecialElements()
        {
            if (!CplexAvailable) return;
            string path = Path.Combine(FolderDir.Model.GetPath(), $"StatsSos_{Guid.NewGuid():N}.lp");
            FolderDir.Model.CreateFolder();
            File.WriteAllText(path, string.Join("\n",
                "Maximize",
                " obj: x1 + 2 x2 + 3 x3",
                "Subject To",
                " c1: x1 + x2 + x3 <= 2",
                "Bounds",
                " 0 <= x1 <= 1",
                " 0 <= x2 <= 1",
                " 0 <= x3 <= 1",
                "SOS",
                " s1: S1:: x1:1 x2:2 x3:3",
                "End",
                ""));
            string tag = StartLog("StatsSos");

            using var engine = NewEngine();
            engine.ImportModel(path);
            Assert.True(engine.Solve());

            var mismatch = Assert.Single(engine.ModelStats!.Mismatches);
            Assert.Equal("SpecialElements", mismatch.Item);
            Assert.Contains("SOS=1", mismatch.Reason);
            Assert.Equal(3, engine.GetObjectiveValue(), 6);

            Assert.Contains("item=SpecialElements framework=0 solver=1", Assert.Single(MismatchWarnings(ReadLog(tag))));
        }

        [Fact(DisplayName = "tuning 階段：OptExperiment 的 Trial、說明檔與 log 都帶得到對帳結果")]
        public void Experiment_RecordsModelStats()
        {
            if (!CplexAvailable) return;

            var consistent = new OptModel("Consistent")
                .AddVariables(e => e.BuildVars<VariableB_Pick>(new[] { "A", "B" }))
                .AddConstraints(e =>
                {
                    e.AddLHS(1.0, new VariableB_Pick { S = "A" });
                    e.AddLHS(1.0, new VariableB_Pick { S = "B" });
                    e.AddRHS(1.0);
                    e.CreateLessEqual("PickLimit");
                })
                .AddObjective(e =>
                {
                    e.AddLHS(1.0, new VariableB_Pick { S = "A" });
                    e.CreateMaximize();
                });
            var unreferenced = new OptModel("Unreferenced")
                .AddVariables(e => e.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" }))
                .AddConstraints(e =>
                {
                    e.AddLHS(1.0, new VariableB_Pick { S = "A" });
                    e.AddLHS(1.0, new VariableB_Pick { S = "B" });
                    e.AddRHS(1.0);
                    e.CreateLessEqual("PickLimit");
                })
                .AddObjective(e =>
                {
                    e.AddLHS(1.0, new VariableB_Pick { S = "A" });
                    e.CreateMaximize();
                });

            string name = $"StatsExp_{Guid.NewGuid():N}";
            var result = new OptExperiment(name, "model stats reconciliation")
                .AddModel(consistent)
                .AddModel(unreferenced)
                .AddConfig("base", Config())
                .Run();

            Assert.True(result.Trials.Single(t => t.Model == "Consistent").Metrics.ModelStats.IsMatch);
            var unreferencedStats = result.Trials.Single(t => t.Model == "Unreferenced").Metrics.ModelStats;
            Assert.False(unreferencedStats.IsMatch);
            Assert.Contains(unreferencedStats.Mismatches, m => m.Item == "Variables" && m.Solver == "2");

            string meta = File.ReadAllText(FolderDir.Experiment.GetPathFile($"{name}-meta.csv"));
            Assert.Contains("schema,version,2", meta);
            Assert.Contains("modelStats,Consistent.result,MATCH", meta);
            Assert.Contains("modelStats,Unreferenced.result,\"MISMATCH:Variables,Binary\"", meta);
            Assert.Contains("modelStats,Unreferenced.variables,framework=3 index=3 solver=2", meta);
            Assert.Contains("modelStats,Unreferenced.mismatchTrials,1/1", meta);
            Assert.Contains("modelStats,Unreferenced.mismatch.Variables,", meta);

            string log = ReadLog($"{name}_exp");
            Assert.Contains("[MODEL_STATS_MISMATCH] 實驗中有 cell 的框架建模統計與 solver 模型不一致", log);
            Assert.Contains("cells=1/2", log);
            Assert.Contains("Unreferenced(MISMATCH:Variables,Binary)", log);
        }
    }
}
