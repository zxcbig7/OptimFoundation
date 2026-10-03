using System;
using System.Globalization;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Integration
{
    /// <summary>
    /// 驗證完整實驗流程：使用 CPLEX 求解，由 Trial.Capture 記錄設定與結果，再以 Save 輸出 CSV。
    /// 缺少 CPLEX DLL 時直接返回，不執行檢查。
    /// </summary>
    // 共用 Logging 的測試依序執行，避免互改 log 檔名。
    [Collection("Logging")]
    public class ExperimentIntegrationTests
    {
        private static readonly bool CplexAvailable =
            File.Exists(@"C:\IBM\ILOG\CPLEX_Studio2211\cplex\bin\x64_win64\ILOG.CPLEX.dll");

        [Fact(DisplayName = "Trial.Capture 擷取設定快照 + 指標，Save 寫進這個實驗的 -trial.csv + -meta.csv，不產 JSON")]
        public void Experiment_CaptureAndSave_WritesCsvAndMeta()
        {
            if (!CplexAvailable) return;

            var config = new CplexConfig { TimeLimit = 30 };
            var projectConfig = new ProjectConfig { EnableSolverLog = false };
            config.Seed = 7;        // Seed 會套用到 CPLEX 的 randomSeed 參數
            config.Emphasis = 2;    // Emphasis 會套用到 CPLEX 的 mipEmphasis 參數

            using var engine = new OptEngine(config, projectConfig);
            engine.Build();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateGreaterEqual(3.0, "LB");
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateMinimize();

            string projectName = "test-exp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var exp = new Experiment(projectName, "unit", "unit test");

            var trial = Trial.Capture(engine, "seed=7,emph=2", () => engine.Solve());
            exp.AddTrial(trial);

            Assert.Equal(SolveStatus.Optimal, trial.Metrics.Status);
            Assert.Equal(3.0, trial.Metrics.ObjectiveValue, precision: 4);
            Assert.True(trial.Metrics.SolveTimeMs >= 0);
            // 直接呼叫 Trial.Capture 不會量測建模時間，因此建模加求解耗時為 null（CSV 寫 n/a）。
            Assert.Null(trial.Metrics.BuildAndSolveTimeMs);
            Assert.Equal(1, trial.Metrics.VarCount);

            Assert.Equal("Cplex", trial.Config.Solver);
            Assert.Equal(7, Convert.ToInt32(trial.Config.Tunable["Seed"]));
            Assert.Equal(2, Convert.ToInt32(trial.Config.Tunable["Emphasis"]));

            exp.Save();

            string csv = FolderDir.Experiment.GetPathFile(projectName + "-unit-trial.csv");
            string meta = FolderDir.Experiment.GetPathFile(projectName + "-unit-meta.csv");
            try
            {
                Assert.True(File.Exists(csv));
                Assert.True(File.Exists(meta));
                Assert.Empty(Directory.GetFiles(FolderDir.Experiment.GetPath(), projectName + "*.json"));
                Assert.Contains("seed=7,emph=2", File.ReadAllText(csv));
                Assert.Contains("environment,solver,Cplex", File.ReadAllText(meta));
            }
            finally
            {
                DeleteProjectArtifacts(projectName);
            }
        }

        [Fact(DisplayName = "EnableTrajectory：MILP 求解收集收斂軌跡（不破壞求解）")]
        public void Trajectory_Milp_CollectsConvergencePoints()
        {
            if (!CplexAvailable) return;

            var values = new double[] { 41, 50, 49, 59, 55, 57, 60, 8, 12, 15, 33, 21, 18, 27, 44 };
            var weights = new double[] { 40, 49, 50, 59, 55, 57, 60, 7, 11, 14, 32, 20, 17, 26, 43 };
            const double cap = 170;

            using var engine = new OptEngine(
                new CplexConfig { TimeLimit = 30 },
                new ProjectConfig { EnableSolverLog = false });
            engine.Build();
            var items = System.Linq.Enumerable.Range(0, values.Length).Select(i => "i" + i).ToList();
            engine.BuildBVs<VarS>(items);

            for (int i = 0; i < values.Length; i++) engine.AddLHS(weights[i], new VarS { S = "i" + i });
            engine.CreateLessEqual(cap, "cap");
            for (int i = 0; i < values.Length; i++) engine.AddLHS(values[i], new VarS { S = "i" + i });
            engine.CreateMaximize();

            var trial = Trial.Capture(engine, "knapsack", () => engine.Solve());

            Assert.Equal(SolveStatus.Optimal, trial.Metrics.Status);
            Assert.NotNull(trial.Metrics.Convergence);
            Assert.True(engine.SupportsTrajectory);
            // 每個取樣點的耗時都應為非負值
            foreach (var p in trial.Metrics.Convergence)
                Assert.True(p.ElapsedMs >= 0);
            // 這個背包問題需要分支搜尋，應至少記錄到一個求解過程取樣點。
            Assert.True(trial.Metrics.Convergence.Count >= 1,
                $"trajectory points = {trial.Metrics.Convergence.Count}");
        }

        [Fact(DisplayName = "ConfigSnapshot：SolverSpecific 不含六個專案輸出開關")]
        public void ConfigSnapshot_CplexConfig_SolverSpecific_DoesNotContainProjectOutputKeys()
        {
            ConfigSnapshot snapshot = ConfigSnapshot.From(new CplexConfig());
            string[] projectKeys =
            {
                "enableLog", "exportLP", "exportMPS", "exportSol", "LogToConsole", "LogFilePath"
            };

            foreach (string key in projectKeys)
                Assert.DoesNotContain(key, snapshot.SolverSpecific.Keys);
        }

        [Fact]
        public void OptExperiment_CrossProduct_ProducesSixStableLabels()
        {
            if (!CplexAvailable) return;

            OptModel BuildModel(string name) => new OptModel(name)
                .AddVariables(e => e.BuildCVs<VarS>(new[] { "x" }))
                .AddObjective(e =>
                {
                    e.AddLHS(1.0, new VarS { S = "x" });
                    e.CreateMinimize();
                });

            string name = "cross-" + Guid.NewGuid().ToString("N");
            var baseline = new CplexConfig { TimeLimit = 30 };
            var emphasis = baseline.Clone(); emphasis.Emphasis = 2;
            var threads = baseline.Clone(); threads.Threads = 1;

            try
            {
                Experiment result = Project(name).Experiment("exp", "2 x 3")
                    .AddModel(BuildModel("Model1"))
                    .AddModel(BuildModel("Model2"))
                    .AddConfig("baseline", baseline)
                    .AddConfig("emphasis", emphasis)
                    .AddConfig("threads", threads)
                    .Run();

                Assert.Equal(6, result.Trials.Count);
                // Label 存設定名稱，Trial.Model 存模型名稱。
                Assert.Equal(
                    new[]
                    {
                        "baseline", "emphasis", "threads",
                        "baseline", "emphasis", "threads",
                    },
                    result.Trials.Select(t => t.Label));
                Assert.Equal(
                    new[] { "Model1", "Model1", "Model1", "Model2", "Model2", "Model2" },
                    result.Trials.Select(t => t.Model));
                // 同一批實驗共用一個 RunId，流水號從 1 開始遞增
                Assert.Single(result.Trials.Select(t => t.ExperimentId).Distinct());
                Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, result.Trials.Select(t => t.TrialId));
                Assert.All(result.Trials, t => Assert.Equal(SolveStatus.Optimal, t.Metrics.Status));
            }
            finally
            {
                DeleteProjectArtifacts(name);
            }
        }

        [Fact]
        public void OptExperiment_AddTrial_ProducesExactlyOneCell()
        {
            if (!CplexAvailable) return;

            string name = "one-cell-" + Guid.NewGuid().ToString("N");
            var model = new OptModel("OnlyModel")
                .AddVariables(e => e.BuildCVs<VarS>(new[] { "x" }))
                .AddObjective(e =>
                {
                    e.AddLHS(1.0, new VarS { S = "x" });
                    e.CreateMinimize();
                });

            try
            {
                Experiment result = Project(name).Experiment("exp", "one cell")
                    .AddTrial(model, "only-config", new CplexConfig { TimeLimit = 30 })
                    .Run();

                Trial trial = Assert.Single(result.Trials);
                Assert.Equal("only-config", trial.Label);
                Assert.Equal("OnlyModel", trial.Model);
                Assert.Equal(SolveStatus.Optimal, trial.Metrics.Status);
            }
            finally
            {
                DeleteProjectArtifacts(name);
            }
        }

        [Fact]
        public void OptExperiment_AddTrialCollisionWithCrossProduct_Throws()
        {
            var model = new OptModel("Model1");
            var config = new CplexConfig { TimeLimit = 30 };
            var experiment = Project("collision").Experiment("exp", "duplicate final label")
                .AddModel(model)
                .AddConfig("baseline", config)
                .AddTrial(model, "baseline", config);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => experiment.Run());
            Assert.Contains("Model1 | baseline", error.Message);
        }

        [Fact]
        public void OptExperiment_DuplicateAddConfigLabel_Throws()
        {
            var experiment = Project("duplicate-config").Experiment("exp", "duplicate config label")
                .AddConfig("baseline", new CplexConfig());

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => experiment.AddConfig("baseline", new CplexConfig()));
            Assert.Contains("baseline", error.Message);
        }

        // ── 輸出命名：以專案名稱開頭，實驗輸出再加上設定名稱 ─────────────

        private static OptProject Project(string name) => new OptProject(name, retentionDays: 0);

        private static OptModel TrivialModel(string name) => new OptModel(name)
            .AddVariables(e => e.BuildCVs<VarS>(new[] { "x" }))
            .AddObjective(e =>
            {
                e.AddLHS(1.0, new VarS { S = "x" });
                e.CreateMinimize();
            });

        private static string[] ExportedModelFiles(string pattern)
        {
            string dir = FolderDir.Model.GetPath();
            return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : Array.Empty<string>();
        }

        [Fact(DisplayName = "單一模型：輸出檔名 = 專案名-參數名，不插模型名")]
        public void Experiment_SingleModel_NamesOutputsByProjectAndLabel()
        {
            if (!CplexAvailable) return;

            string projectName = "NameAlign" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string expName = projectName + "-tuning-r1";

            try
            {
                Project(projectName).Experiment("tuning-r1", "單一模型命名")
                    .LoadConfig(new ProjectConfig { EnableSolverLog = false, ExportLP = true })
                    .AddModel(TrivialModel("Canonical"))
                    .AddConfig("r1-baseline", new CplexConfig { TimeLimit = 30 })
                    .Run();

                Assert.Single(ExportedModelFiles($"{projectName}-r1-baseline_LP_*.lp"));
                // 模型名不該出現在檔名裡：只有一個模型時它不提供任何辨識力
                Assert.Empty(ExportedModelFiles($"{projectName}*Canonical*"));
                // 實驗名的 -tuning-r1 段也不該進檔名，檔名的根是專案名
                Assert.Empty(ExportedModelFiles($"{expName}-*.lp"));
            }
            finally
            {
                DeleteProjectArtifacts(projectName);
            }
        }

        [Fact(DisplayName = "多模型：檔名插入模型名，各模型的輸出才不會互相覆蓋")]
        public void Experiment_MultiModel_InsertsModelNameIntoOutputName()
        {
            if (!CplexAvailable) return;

            string projectName = "NameAlignMulti" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                Project(projectName).Experiment("tuning-r1", "多模型命名")
                    .LoadConfig(new ProjectConfig { EnableSolverLog = false, ExportLP = true })
                    .AddModel(TrivialModel("ModelA"))
                    .AddModel(TrivialModel("ModelB"))
                    .AddConfig("r1-baseline", new CplexConfig { TimeLimit = 30 })
                    .Run();

                Assert.Single(ExportedModelFiles($"{projectName}-ModelA-r1-baseline_LP_*.lp"));
                Assert.Single(ExportedModelFiles($"{projectName}-ModelB-r1-baseline_LP_*.lp"));
            }
            finally
            {
                DeleteProjectArtifacts(projectName);
            }
        }

        [Fact(DisplayName = "實驗紀錄寫成 {FullName}-trial.csv 等四個檔，欄位固定、不帶 RecordedAt / Experiment / RunId")]
        public void Experiment_OutputFiles_AreNamedByFullName()
        {
            if (!CplexAvailable) return;

            string projectName = "ExpFiles" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string fullName = projectName + "-tuning-r1";

            try
            {
                OptExperiment experiment = Project(projectName).Experiment("tuning-r1", "檔名");
                Assert.Equal("tuning-r1", experiment.Name);
                Assert.Equal(fullName, experiment.FullName);

                Experiment result = experiment
                    .AddModel(TrivialModel("Canonical"))
                    .AddConfig("r1-baseline", new CplexConfig { TimeLimit = 30 })
                    .Run();

                Assert.Equal(projectName, result.Project);
                Assert.Equal("tuning-r1", result.Name);
                foreach (string kind in new[] { "trial", "meta", "summary" })
                {
                    List<string[]> rows = ReadExperimentCsv(fullName, kind);
                    Assert.DoesNotContain("RecordedAt", rows[0]);
                    Assert.DoesNotContain("Experiment", rows[0]);
                    Assert.DoesNotContain("RunId", rows[0]);
                    Assert.NotEmpty(rows.Skip(1));
                }
                Assert.False(File.Exists(FolderDir.Experiment.GetPathFile(projectName + "-trial.csv")));
            }
            finally
            {
                DeleteProjectArtifacts(projectName);
            }
        }

        [Fact(DisplayName = "同名實驗再跑一次：整組覆寫，檔裡只有最後一次的紀錄")]
        public void Experiment_RerunSameName_OverwritesFiles()
        {
            if (!CplexAvailable) return;

            string projectName = "ExpRerun" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                OptProject project = Project(projectName);
                Experiment RunOnce() => project.Experiment("tuning-r1", "重跑")
                    .AddModel(TrivialModel("Canonical"))
                    .AddConfig("r1-baseline", new CplexConfig { TimeLimit = 30 })
                    .Run();

                string fullName = projectName + "-tuning-r1";
                RunOnce();
                Experiment second = RunOnce();

                Assert.Single(ReadExperimentCsv(fullName, "trial").Skip(1));
                // 說明檔只有一份，是第二次的開始時間
                List<string[]> meta = ReadExperimentCsv(fullName, "meta");
                string startedAt = Assert.Single(meta, r => r[0] == "run" && r[1] == "startedAt")[2];
                Assert.Equal(second.Trials.Single().RunTime.ToString("yyyy-MM-dd HH:mm:ss"), startedAt);
            }
            finally
            {
                DeleteProjectArtifacts(projectName);
            }
        }

        [Fact(DisplayName = "log 檔名由框架設定為 專案名-實驗名_exp，不必在 Program.cs 手動補")]
        public void Experiment_SetsLogFileNameFromProjectName()
        {
            if (!CplexAvailable) return;

            string projectName = "NameAlignLog" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string expName = projectName + "-tuning-r1";

            try
            {
                Project(projectName).Experiment("tuning-r1", "log 命名")
                    .AddModel(TrivialModel("Canonical"))
                    .AddConfig("r1-baseline", new CplexConfig { TimeLimit = 30 })
                    .Run();

                Assert.NotEmpty(Directory.GetFiles(FolderDir.Log.GetPath(), $"{expName}_exp_*.txt"));
            }
            finally
            {
                DeleteProjectArtifacts(projectName);
            }
        }

        [Fact(DisplayName = "實驗主表、說明檔與彙總每一格都有值：缺值寫 off / none / n/a / baseline")]
        public void Experiment_Csv_HasNoBlankCells()
        {
            if (!CplexAvailable) return;

            string projectName = "ExpNoBlank" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                OptProject project = Project(projectName);
                project.Experiment("traj-on", "軌跡開")
                    .AddModel(TrivialModel("Canonical"))
                    .AddConfig("r1-baseline", new CplexConfig { TimeLimit = 30 })
                    .AddConfig("r1-same-config", new CplexConfig { TimeLimit = 30 })
                    .AddConfig("r1-symmetry0", new CplexConfig { TimeLimit = 30, Symmetry = 0 })
                    .Run();
                project.Experiment("traj-off", "軌跡關")
                    .CaptureTrajectory(false)
                    .AddModel(TrivialModel("Canonical"))
                    .AddConfig("r1-baseline", new CplexConfig { TimeLimit = 30 })
                    .Run();

                // 兩個實驗各一組檔
                List<string[]> onRows = ReadExperimentCsv(projectName + "-traj-on", "trial");
                string[] header = onRows[0];
                string Col(string[] row, string column) => row[Array.IndexOf(header, column)];
                string[] Row(List<string[]> rows, string label) => rows.Single(r => Col(r, "TrialLabel") == label);
                var on = onRows.Skip(1).ToList();
                var off = ReadExperimentCsv(projectName + "-traj-off", "trial").Skip(1).ToList();
                Assert.Equal(3, on.Count);
                Assert.Single(off);

                foreach (string experimentName in new[] { "traj-on", "traj-off" })
                    foreach (string kind in new[] { "trial", "meta", "summary" })
                        foreach (var row in ReadExperimentCsv($"{projectName}-{experimentName}", kind).Skip(1))
                            Assert.All(row, cell => Assert.False(string.IsNullOrEmpty(cell), $"{experimentName} {kind} 有空白格：{string.Join(",", row)}"));
                // 這個小模型是 LP，CPLEX 不會呼叫 MIPInfoCallback：沒有軌跡點就不寫 -trajectory.csv，框架也不補點
                Assert.False(File.Exists(FolderDir.Experiment.GetPathFile(projectName + "-traj-on-trajectory.csv")));

                string[] baseline = Row(on, "r1-baseline");
                Assert.Equal(CsvExperimentWriter.Baseline, Col(baseline, "ConfigChanges"));
                Assert.Equal("LP", Col(baseline, "ModelType"));
                Assert.Equal(CsvExperimentWriter.None, Col(Row(on, "r1-same-config"), "ConfigChanges"));
                Assert.Equal("Symmetry=0", Col(Row(on, "r1-symmetry0"), "ConfigChanges"));
                Assert.Equal(CsvExperimentWriter.Baseline, Col(baseline, "VsBaseline"));
                Assert.Contains(Col(Row(on, "r1-symmetry0"), "VsBaseline"),
                    new[] { CsvExperimentWriter.Win, CsvExperimentWriter.Lose, CsvExperimentWriter.Tie });
                Assert.True(int.TryParse(Col(baseline, "Seed"), out _), "沒明設 Seed 時要寫 CPLEX 實際用的種子");
                // 軌跡只含 CPLEX 實際呼叫 callback 的點：這個小 LP 在 root 就解完，callback 一次都沒被呼叫，照實寫 n/a
                Assert.Equal(CsvExperimentWriter.NotAvailable, Col(baseline, "FirstSolutionMs"));
                Assert.Equal(CsvExperimentWriter.NotAvailable, Col(baseline, "LastBoundChangeMs"));
                Assert.Equal(CsvExperimentWriter.NotAvailable, Col(baseline, "BoundChange"));
                // 經由 OptExperiment 執行：建模 + 求解 = 建模時間 + 純求解時間，不會小於純求解
                double solve = double.Parse(Col(baseline, "SolveTimeMs"), CultureInfo.InvariantCulture);
                double buildAndSolve = double.Parse(Col(baseline, "BuildAndSolveTimeMs"), CultureInfo.InvariantCulture);
                Assert.True(buildAndSolve >= solve, $"BuildAndSolveTimeMs={buildAndSolve} < SolveTimeMs={solve}");

                string[] offRow = Row(off, "r1-baseline");
                Assert.Equal(CsvExperimentWriter.Off, Col(offRow, "FirstSolutionMs"));
                Assert.Equal(CsvExperimentWriter.Off, Col(offRow, "LastBoundChangeMs"));
                Assert.Equal(CsvExperimentWriter.Off, Col(offRow, "BoundChange"));
            }
            finally
            {
                DeleteProjectArtifacts(projectName);
            }
        }

        [Fact(DisplayName = "模型類型（主表）與各類變數 / 限制式數量（-meta.csv）取自 CPLEX 模型，不是框架統計")]
        public void Experiment_Csv_RecordsSolverModelStructure()
        {
            if (!CplexAvailable) return;

            string projectName = "ExpStructure" + Guid.NewGuid().ToString("N").Substring(0, 8);

            // x 連續、b1 / b2 Binary、n Integer；unused 有建但沒被任何式子引用，CPLEX 不收，所以不該計入
            var model = new OptModel("Mixed")
                .AddVariables(e =>
                {
                    e.BuildCVs<VarS>(new[] { "x", "unused" });
                    e.BuildBVs<VarS>(new[] { "b1", "b2" });
                    e.BuildIVs<VarS>(new[] { "n" });
                })
                .AddObjective(e =>
                {
                    e.AddLHS(1.0, new VarS { S = "x" });
                    e.AddLHS(1.0, new VarS { S = "n" });
                    e.CreateMinimize();
                })
                .AddConstraints(e =>
                {
                    e.AddLHS(1.0, new VarS { S = "b1" });
                    e.AddLHS(1.0, new VarS { S = "b2" });
                    e.CreateGreaterEqual(1, "PickOne");
                    e.AddLHS(1.0, new VarS { S = "x" });
                    e.AddLHS(1.0, new VarS { S = "n" });
                    e.CreateGreaterEqual(2, "Floor");
                });

            try
            {
                Project(projectName).Experiment("structure", "模型結構")
                    .AddModel(model)
                    .AddConfig("r1-baseline", new CplexConfig { TimeLimit = 30 })
                    .Run();

                List<string[]> rows = ReadExperimentCsv(projectName + "-structure", "trial");
                string Col(string column) => rows[1][Array.IndexOf(rows[0], column)];
                Assert.Equal("MILP", Col("ModelType"));
                // 同一模型每列都一樣的數量不在主表重抄，只寫在 -meta.csv 的 model 區段
                Assert.DoesNotContain("VarCount", rows[0]);
                Assert.DoesNotContain("ConstraintCount", rows[0]);

                string meta = File.ReadAllText(FolderDir.Experiment.GetPathFile(projectName + "-structure-meta.csv"));
                Assert.Contains("model,Mixed.modelType,MILP", meta);
                Assert.Contains("model,Mixed.varCount,4", meta);
                Assert.Contains("model,Mixed.binaryVarCount,2", meta);
                Assert.Contains("model,Mixed.integerVarCount,1", meta);
                Assert.Contains("model,Mixed.continuousVarCount,1", meta);
                Assert.Contains("model,Mixed.semiContinuousVarCount,0", meta);
                Assert.Contains("model,Mixed.constraintCount,2", meta);
                Assert.Contains("model,Mixed.quadraticConstraintCount,0", meta);
            }
            finally
            {
                DeleteProjectArtifacts(projectName);
            }
        }

        // 每個實驗一組檔：{專案}-{實驗}-trial.csv / -meta.csv / -summary.csv / -trajectory.csv
        private static List<string[]> ReadExperimentCsv(string fullName, string kind)
        {
            using var reader = new StreamReader(FolderDir.Experiment.GetPathFile($"{fullName}-{kind}.csv"), System.Text.Encoding.UTF8);
            return CsvCtrl.ParseCsv(reader).ToList();
        }

        private static void DeleteProjectArtifacts(string projectName)
        {
            string dir = FolderDir.Experiment.GetPath();
            if (!Directory.Exists(dir)) return;
            foreach (string path in Directory.GetFiles(dir, $"{projectName}-*.csv"))
                File.Delete(path);
        }
    }
}
