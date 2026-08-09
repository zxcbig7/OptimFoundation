using System;
using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Integration
{
    /// <summary>
    /// Experiment 套件端到端：真 CPLEX 求解 → Trial.Capture 擷取設定+指標 → Save 輸出 CSV/JSON。
    /// 需要 CPLEX DLL；不存在則 Skip。
    /// </summary>
    public class ExperimentIntegrationTests
    {
        private static readonly bool CplexAvailable =
            File.Exists(@"C:\IBM\ILOG\CPLEX_Studio2211\cplex\bin\x64_win64\ILOG.CPLEX.dll");

        [Fact(DisplayName = "Trial.Capture 擷取設定快照 + 指標，Save 產出 CSV/JSON")]
        public void Experiment_CaptureAndSave_WritesCsvAndJson()
        {
            if (!CplexAvailable) return;

            var config = new CplexConfig { TimeLimit = 30 };
            var projectConfig = new ProjectConfig { EnableSolverLog = false };
            config.Seed = 7;        // 透過抽象旋鈕 tune（delegate 到 randomSeed）
            config.Emphasis = 2;    // delegate 到 mipEmphasis

            using var engine = new OptEngine(config, projectConfig);
            engine.Build();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateGreatEqual(3.0, "LB");
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateMinimize();

            string expName = "test-exp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var exp = new Experiment(expName, "unit test");

            var trial = Trial.Capture(engine, "seed=7,emph=2", () => engine.Solve());
            exp.AddTrial(trial);

            // 指標回填
            Assert.Equal(SolveStatus.Optimal, trial.Metrics.Status);
            Assert.Equal(3.0, trial.Metrics.ObjectiveValue, precision: 4);
            Assert.True(trial.Metrics.RunTimeMs >= 0);
            Assert.Equal(1, trial.Metrics.VarCount);

            // 設定快照（含抽象旋鈕 + solver 名）
            Assert.Equal("Cplex", trial.Config.Solver);
            Assert.Equal(7, Convert.ToInt32(trial.Config.Tunable["Seed"]));
            Assert.Equal(2, Convert.ToInt32(trial.Config.Tunable["Emphasis"]));

            exp.Save();

            string csv  = FolderDir.Experiment.GetFilePath(expName + ".csv");
            string json = FolderDir.Experiment.GetFilePath(expName + ".json");
            try
            {
                Assert.True(File.Exists(csv));
                Assert.True(File.Exists(json));
                Assert.Contains("seed=7,emph=2", File.ReadAllText(csv));
                Assert.Contains("\"solver\": \"Cplex\"", File.ReadAllText(json));
            }
            finally
            {
                if (File.Exists(csv))  File.Delete(csv);
                if (File.Exists(json)) File.Delete(json);
            }
        }

        [Fact(DisplayName = "EnableTrajectory：MILP 求解收集收斂軌跡（不破壞求解）")]
        public void Trajectory_Milp_CollectsConvergencePoints()
        {
            if (!CplexAvailable) return;

            var values  = new double[] { 41, 50, 49, 59, 55, 57, 60, 8, 12, 15, 33, 21, 18, 27, 44 };
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

            Assert.Equal(SolveStatus.Optimal, trial.Metrics.Status);   // 掛 callback 不破壞求解
            Assert.NotNull(trial.Metrics.Convergence);
            Assert.True(engine.SupportsTrajectory);
            // 軌跡點應結構良好
            foreach (var p in trial.Metrics.Convergence)
                Assert.True(p.TimeMs >= 0);
            // 探測：這個會 branch 的 knapsack 應至少收集到 1 點
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
                Experiment result = new OptExperiment(name, "2 x 3")
                    .AddModel(BuildModel("Model1"))
                    .AddModel(BuildModel("Model2"))
                    .AddConfig("baseline", baseline)
                    .AddConfig("emphasis", emphasis)
                    .AddConfig("threads", threads)
                    .Run();

                Assert.Equal(6, result.Trials.Count);
                Assert.Equal(
                    new[]
                    {
                        "Model1 | baseline", "Model1 | emphasis", "Model1 | threads",
                        "Model2 | baseline", "Model2 | emphasis", "Model2 | threads",
                    },
                    result.Trials.Select(t => t.Label));
                Assert.All(result.Trials, t => Assert.Equal(SolveStatus.Optimal, t.Metrics.Status));
            }
            finally
            {
                DeleteExperimentArtifacts(name);
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
                Experiment result = new OptExperiment(name, "one cell")
                    .AddTrial(model, "only-config", new CplexConfig { TimeLimit = 30 })
                    .Run();

                Trial trial = Assert.Single(result.Trials);
                Assert.Equal("OnlyModel | only-config", trial.Label);
                Assert.Equal(SolveStatus.Optimal, trial.Metrics.Status);
            }
            finally
            {
                DeleteExperimentArtifacts(name);
            }
        }

        [Fact]
        public void OptExperiment_AddTrialCollisionWithCrossProduct_Throws()
        {
            var model = new OptModel("Model1");
            var config = new CplexConfig { TimeLimit = 30 };
            var experiment = new OptExperiment("collision", "duplicate final label")
                .AddModel(model)
                .AddConfig("baseline", config)
                .AddTrial(model, "baseline", config);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => experiment.Run());
            Assert.Contains("Model1 | baseline", error.Message);
        }

        [Fact]
        public void OptExperiment_DuplicateAddConfigLabel_Throws()
        {
            var experiment = new OptExperiment("duplicate-config", "duplicate config label")
                .AddConfig("baseline", new CplexConfig());

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => experiment.AddConfig("baseline", new CplexConfig()));
            Assert.Contains("baseline", error.Message);
        }

        private static void DeleteExperimentArtifacts(string name)
        {
            foreach (string suffix in new[] { ".csv", ".json", "-trajectory.csv" })
            {
                string path = FolderDir.Experiment.GetFilePath(name + suffix);
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
