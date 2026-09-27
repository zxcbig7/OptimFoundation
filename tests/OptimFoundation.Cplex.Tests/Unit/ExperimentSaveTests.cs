using System;
using System.IO;
using OptimFoundation.Core;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // Save 會寫 log（[Experiment] Saved ...）；與其他讀 log 斷言的測試同一 collection 才不會互相污染
    [Collection("Logging")]
    public class ExperimentSaveTests
    {
        [Fact(DisplayName = "同名實驗 Save 直接覆寫：只留本次 trials、沒有軌跡就刪掉舊 -trajectory.csv、不產 JSON")]
        public void Save_SameName_OverwritesInsteadOfAppending()
        {
            string name = $"SaveOverwrite_{Guid.NewGuid():N}";
            try
            {
                var first = new Experiment(name, "first");
                first.AddTrial(NewTrial("a", withTrajectory: true));
                first.AddTrial(NewTrial("b", withTrajectory: false));
                first.Save();
                Assert.True(File.Exists(Artifact(name, "-trajectory.csv")));

                var second = new Experiment(name, "second");
                second.AddTrial(NewTrial("c", withTrajectory: false));
                second.Save();

                string[] lines = File.ReadAllLines(Artifact(name, ".csv"));
                Assert.Equal(2, lines.Length);
                Assert.Contains(",c,", lines[1]);
                Assert.Contains("experiment,trialCount,1", File.ReadAllText(Artifact(name, "-meta.csv")));
                Assert.False(File.Exists(Artifact(name, "-trajectory.csv")));
                Assert.Empty(Directory.GetFiles(FolderDir.Experiment.GetPath(), $"{name}*.json"));
            }
            finally
            {
                foreach (string suffix in new[] { ".csv", "-meta.csv", "-trajectory.csv" })
                    File.Delete(Artifact(name, suffix));
            }
        }

        private static string Artifact(string name, string suffix) => FolderDir.Experiment.GetPathFile(name + suffix);

        private static Trial NewTrial(string label, bool withTrajectory)
        {
            var metrics = new SolveMetrics { Status = SolveStatus.Optimal, ObjectiveValue = 1, BestBound = 1, MipGap = 0 };
            if (withTrajectory)
                metrics.Convergence.Add(new ConvergencePoint { TimeMs = 1, Objective = 1, Bound = 1, Gap = 0 });
            return new Trial { Label = label, ExperimentId = "run", RunTime = DateTime.Now, Config = new ConfigSnapshot(), Metrics = metrics };
        }
    }
}
