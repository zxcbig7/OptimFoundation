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

            var config = new CplexConfig { timeLimit = 30, enableLog = false };
            config.Seed = 7;        // 透過抽象旋鈕 tune（delegate 到 randomSeed）
            config.Emphasis = 2;    // delegate 到 mipEmphasis

            using var engine = new OptEngine(config);
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
            Assert.True(trial.Metrics.WallTimeMs >= 0);
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

            using var engine = new OptEngine(new CplexConfig { timeLimit = 30, enableLog = false });
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
    }
}
