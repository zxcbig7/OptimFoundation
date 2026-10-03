using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // Save 會寫 log（[Experiment] Saved ...、WARN）；與其他讀 log 斷言的測試同一 collection 才不會互相污染
    [Collection("Logging")]
    public class ExperimentSaveTests
    {
        [Fact(DisplayName = "每個實驗各寫一組四個檔：{專案}-{實驗}-trial / -meta / -summary / -trajectory，欄位固定、BOM 只在檔頭，不產 JSON")]
        public void Save_WritesOneFileSetPerExperiment()
        {
            string project = $"SaveFiles_{Guid.NewGuid():N}";
            try
            {
                var first = new Experiment(project, "r1", "first");
                first.AddTrial(NewTrial("a", "run1", 1, withTrajectory: true));
                first.AddTrial(NewTrial("b", "run1", 2, withTrajectory: false));
                first.Save();

                var second = new Experiment(project, "r2", "second");
                second.AddTrial(NewTrial("c", "run2", 1, withTrajectory: false));
                second.Save();

                byte[] bytes = File.ReadAllBytes(Artifact(project, "r1", "trial"));
                int[] bomAt = Enumerable.Range(0, bytes.Length - 2)
                    .Where(i => bytes[i] == 0xEF && bytes[i + 1] == 0xBB && bytes[i + 2] == 0xBF)
                    .ToArray();
                Assert.Equal(new[] { 0 }, bomAt);

                var trial = Read(project, "r1", "trial");
                // 主表只留調參會判讀的欄位；整個實驗每列一樣的（基準是誰、模型結構數量）在 -meta.csv
                Assert.Equal(new[]
                {
                    "TrialId", "Model", "ModelType", "TrialLabel", "ConfigChanges", "Seed", "VsBaseline",
                    "Status", "ObjectiveValue", "BestBound", "Gap", "BuildAndSolveTimeMs", "SolveTimeMs", "FirstSolutionMs", "LastBoundChangeMs", "BoundChange", "NodeCount", "IterationCount"
                }, trial[0]);
                Assert.Equal(new[] { "1|a", "2|b" }, trial.Skip(1).Select(r => $"{r[0]}|{r[3]}"));
                Assert.Equal(new[] { "1|c" }, Read(project, "r2", "trial").Skip(1).Select(r => $"{r[0]}|{r[3]}"));

                // 說明檔沒有格式版本：欄位只有一種
                var meta = Read(project, "r1", "meta");
                Assert.Equal(new[] { "Section", "Key", "Value" }, meta[0]);
                Assert.DoesNotContain(meta, r => r[0] == "schema");
                Assert.Contains(meta, r => r[0] == "run" && r[1] == "trialCount" && r[2] == "2");
                Assert.Contains(Read(project, "r2", "meta"), r => r[0] == "run" && r[1] == "trialCount" && r[2] == "1");

                // 彙總每組設定一列（label 不同就是不同設定）
                Assert.Equal(new[] { "a", "b" }, Read(project, "r1", "summary").Skip(1).Select(r => r[1]));

                // 軌跡只有 a 有點；TrialId 對回主表那一列；r2 沒有軌跡就沒有檔
                var trajectory = Read(project, "r1", "trajectory");
                Assert.Equal(new[] { "TrialId", "TrialLabel", "PointIndex", "ElapsedMs", "ObjectiveValue", "BestBound", "Gap" }, trajectory[0]);
                Assert.Equal("1|a", string.Join("|", Assert.Single(trajectory.Skip(1)).Take(2)));
                Assert.False(File.Exists(Artifact(project, "r2", "trajectory")));

                Assert.Empty(Directory.GetFiles(FolderDir.Experiment.GetPath(), $"{project}*.json"));
            }
            finally
            {
                DeleteArtifacts(project);
            }
        }

        [Fact(DisplayName = "同名實驗再跑一次：整組覆寫，這次沒寫到的舊檔（上次的軌跡、彙總）一併刪掉")]
        public void Save_SameName_OverwritesWholeSet()
        {
            string project = $"SaveOverwrite_{Guid.NewGuid():N}";
            try
            {
                var first = new Experiment(project, "r1", "first");
                first.AddTrial(NewTrial("a", "run1", 1, withTrajectory: true));
                first.AddTrial(NewTrial("b", "run1", 2, withTrajectory: false));
                first.Save();
                Assert.True(File.Exists(Artifact(project, "r1", "trajectory")));

                var second = new Experiment(project, "r1", "second") { WriteSummary = false };
                second.AddTrial(NewTrial("c", "run2", 1, withTrajectory: false));
                second.Save();

                Assert.Equal("c", Assert.Single(Read(project, "r1", "trial").Skip(1))[3]);
                Assert.Contains(Read(project, "r1", "meta"), r => r[0] == "experiment" && r[1] == "description" && r[2] == "second");
                Assert.False(File.Exists(Artifact(project, "r1", "summary")));
                Assert.False(File.Exists(Artifact(project, "r1", "trajectory")));
            }
            finally
            {
                DeleteArtifacts(project);
            }
        }

        [Fact(DisplayName = "實驗檔寫不進去（例：被 Excel 開著）：這次改寫到 -locked-<時間> 檔，原檔不動、紀錄不丟")]
        public void Save_FileLocked_WritesToFallback()
        {
            string project = $"SaveLocked_{Guid.NewGuid():N}";
            try
            {
                var first = new Experiment(project, "r1", "first");
                first.AddTrial(NewTrial("a", "run1", 1, withTrajectory: false));
                first.Save();
                string before = File.ReadAllText(Artifact(project, "r1", "trial"));

                // 模擬 Excel：檔案開著，別人可以讀、不能寫
                using (new FileStream(Artifact(project, "r1", "trial"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    var second = new Experiment(project, "r1", "locked");
                    second.AddTrial(NewTrial("b", "run2", 1, withTrajectory: false));
                    second.Save();
                }

                Assert.Equal(before, File.ReadAllText(Artifact(project, "r1", "trial")));
                string locked = Assert.Single(Directory.GetFiles(FolderDir.Experiment.GetPath(), $"{project}-r1-trial-locked-*.csv"));
                var rows = ReadFile(locked);
                Assert.Equal("TrialId", rows[0][0]);
                Assert.Equal("b", Assert.Single(rows.Skip(1))[3]);
                // 沒被鎖住的檔照常覆寫
                Assert.Contains(Read(project, "r1", "meta"), r => r[0] == "experiment" && r[2] == "locked");
            }
            finally
            {
                DeleteArtifacts(project);
            }
        }

        [Fact(DisplayName = "實驗名決定檔名：含非法檔名字元就拒絕")]
        public void Constructor_InvalidFileNameChar_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Experiment("SaveInvalid", "r1/bad", "invalid"));
        }

        [Fact(DisplayName = "沒有任何 trial：不寫任何檔")]
        public void Save_NoTrials_WritesNothing()
        {
            string project = $"SaveEmpty_{Guid.NewGuid():N}";
            new Experiment(project, "r1", "empty").Save();
            Assert.Empty(Directory.GetFiles(FolderDir.Experiment.GetPath(), $"{project}-*"));
        }

        [Fact(DisplayName = "WriteSummary = false：不寫 -summary.csv（正式求解紀錄用）")]
        public void Save_WithoutSummary_SkipsSummaryFile()
        {
            string project = $"SaveNoSummary_{Guid.NewGuid():N}";
            try
            {
                var experiment = new Experiment(project, "solve", "production") { WriteSummary = false };
                experiment.AddTrial(NewTrial("solve", "run1", 1, withTrajectory: false));
                experiment.Save();

                Assert.True(File.Exists(Artifact(project, "solve", "trial")));
                Assert.True(File.Exists(Artifact(project, "solve", "meta")));
                Assert.False(File.Exists(Artifact(project, "solve", "summary")));
            }
            finally
            {
                DeleteArtifacts(project);
            }
        }

        private static string Artifact(string project, string experiment, string kind) =>
            FolderDir.Experiment.GetPathFile($"{project}-{experiment}-{kind}.csv");

        private static List<string[]> Read(string project, string experiment, string kind) => ReadFile(Artifact(project, experiment, kind));

        private static List<string[]> ReadFile(string path)
        {
            using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
            return CsvCtrl.ParseCsv(reader).ToList();
        }

        private static void DeleteArtifacts(string project)
        {
            foreach (string path in Directory.GetFiles(FolderDir.Experiment.GetPath(), $"{project}-*"))
                File.Delete(path);
        }

        private static Trial NewTrial(string label, string runId, int trialId, bool withTrajectory)
        {
            var metrics = new SolveMetrics { Status = SolveStatus.Optimal, ObjectiveValue = 1, BestBound = 1, Gap = 0 };
            if (withTrajectory)
                metrics.Convergence.Add(new ConvergencePoint { ElapsedMs = 1, ObjectiveValue = 1, BestBound = 1, Gap = 0 });
            return new Trial { Label = label, ExperimentId = runId, TrialId = trialId, RunTime = DateTime.Now, Config = new ConfigSnapshot(), Metrics = metrics };
        }
    }
}
