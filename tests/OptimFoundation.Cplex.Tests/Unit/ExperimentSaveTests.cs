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
        [Fact(DisplayName = "Save 接在專案累積檔的檔尾：表頭只寫一次、BOM 只在檔頭，靠 Experiment + RunId 分辨，不產 JSON")]
        public void Save_AppendsToProjectFiles()
        {
            string project = $"SaveAppend_{Guid.NewGuid():N}";
            try
            {
                var first = new Experiment(project, "r1", "first");
                first.AddTrial(NewTrial("a", "run1", 1, withTrajectory: true));
                first.AddTrial(NewTrial("b", "run1", 2, withTrajectory: false));
                first.Save();

                var second = new Experiment(project, "r2", "second");
                second.AddTrial(NewTrial("c", "run2", 1, withTrajectory: false));
                second.Save();

                byte[] bytes = File.ReadAllBytes(Artifact(project, "trial"));
                int[] bomAt = Enumerable.Range(0, bytes.Length - 2)
                    .Where(i => bytes[i] == 0xEF && bytes[i + 1] == 0xBB && bytes[i + 2] == 0xBF)
                    .ToArray();
                Assert.Equal(new[] { 0 }, bomAt);

                var trial = Read(project, "trial");
                // 主表只留調參會判讀的欄位；同批每列一樣的（基準是誰、模型結構數量）在 -meta.csv
                Assert.Equal(new[]
                {
                    "RecordedAt", "Experiment", "RunId", "TrialId", "Model", "ModelType", "TrialLabel", "ConfigChanges", "Seed", "VsBaseline",
                    "Status", "ObjectiveValue", "BestBound", "Gap", "BuildAndSolveTimeMs", "SolveTimeMs", "FirstSolutionMs", "LastBoundChangeMs", "BoundChange", "NodeCount", "IterationCount"
                }, trial[0]);
                Assert.Equal(new[] { "r1|run1|1", "r1|run1|2", "r2|run2|1" }, trial.Skip(1).Select(r => $"{r[1]}|{r[2]}|{r[3]}"));

                // 說明檔每批一份：run 區段的 key 不再帶 RunId
                var meta = Read(project, "meta");
                Assert.Equal(new[] { "RecordedAt", "Experiment", "RunId", "Section", "Key", "Value" }, meta[0]);
                Assert.Contains(meta, r => r[1] == "r1" && r[2] == "run1" && r[3] == "schema" && r[5] == MetaCsvWriter.SchemaVersion.ToString());
                Assert.Contains(meta, r => r[1] == "r1" && r[2] == "run1" && r[3] == "run" && r[4] == "trialCount" && r[5] == "2");
                Assert.Contains(meta, r => r[1] == "r2" && r[2] == "run2" && r[3] == "run" && r[4] == "trialCount" && r[5] == "1");

                // 彙總每組設定一列（label 不同就是不同設定）
                Assert.Equal(new[] { "r1|run1|a", "r1|run1|b", "r2|run2|c" }, Read(project, "summary").Skip(1).Select(r => $"{r[1]}|{r[2]}|{r[4]}"));

                // 軌跡只有 a 有點；TrialId 對回主表那一列
                var trajectory = Read(project, "trajectory");
                Assert.Equal(new[] { "RecordedAt", "Experiment", "RunId", "TrialId", "TrialLabel", "PointIndex", "ElapsedMs", "ObjectiveValue", "BestBound", "Gap" }, trajectory[0]);
                string[] point = Assert.Single(trajectory.Skip(1));
                Assert.Equal("r1|run1|1|a", $"{point[1]}|{point[2]}|{point[3]}|{point[4]}");

                // 同一次 Save 的四個檔寫入時間相同
                var firstSave = new[] { "trial", "meta", "summary", "trajectory" }
                    .SelectMany(kind => Read(project, kind).Skip(1).Where(r => r[1] == "r1"))
                    .Select(r => r[0]);
                Assert.Single(firstSave.Distinct());

                Assert.Empty(Directory.GetFiles(FolderDir.Experiment.GetPath(), $"{project}*.json"));
            }
            finally
            {
                DeleteArtifacts(project);
            }
        }

        [Fact(DisplayName = "累積檔的表頭跟這一版不同：舊檔改名成 -old-<時間> 原樣保留，另開新檔")]
        public void Save_HeaderChanged_RenamesOldFile()
        {
            string project = $"SaveHeader_{Guid.NewGuid():N}";
            try
            {
                FolderDir.Experiment.CreateFolder();
                const string oldContent = "RunId,TrialId\nold,1\n";
                File.WriteAllText(Artifact(project, "trial"), oldContent);

                var experiment = new Experiment(project, "r1", "header changed");
                experiment.AddTrial(NewTrial("a", "run1", 1, withTrajectory: false));
                experiment.Save();

                string old = Assert.Single(Directory.GetFiles(FolderDir.Experiment.GetPath(), $"{project}-trial-old-*.csv"));
                Assert.Equal(oldContent, File.ReadAllText(old));
                var rows = Read(project, "trial");
                Assert.Equal("RecordedAt", rows[0][0]);
                Assert.Equal("a", Assert.Single(rows.Skip(1))[6]);
            }
            finally
            {
                DeleteArtifacts(project);
            }
        }

        [Fact(DisplayName = "累積檔寫不進去（例：被 Excel 開著）：這次改寫到 -locked-<時間> 檔，原檔不動、紀錄不丟")]
        public void Save_FileLocked_WritesToFallback()
        {
            string project = $"SaveLocked_{Guid.NewGuid():N}";
            try
            {
                var first = new Experiment(project, "r1", "first");
                first.AddTrial(NewTrial("a", "run1", 1, withTrajectory: false));
                first.Save();
                string before = File.ReadAllText(Artifact(project, "trial"));

                // 模擬 Excel：檔案開著，別人可以讀、不能寫
                using (new FileStream(Artifact(project, "trial"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    var second = new Experiment(project, "r2", "locked");
                    second.AddTrial(NewTrial("b", "run2", 1, withTrajectory: false));
                    second.Save();
                }

                Assert.Equal(before, File.ReadAllText(Artifact(project, "trial")));
                string locked = Assert.Single(Directory.GetFiles(FolderDir.Experiment.GetPath(), $"{project}-trial-locked-*.csv"));
                var rows = ReadFile(locked);
                Assert.Equal("RecordedAt", rows[0][0]);
                Assert.Equal("r2", Assert.Single(rows.Skip(1))[1]);
                // 沒被鎖住的檔照常接在檔尾
                Assert.Contains(Read(project, "meta").Skip(1), r => r[1] == "r2");
            }
            finally
            {
                DeleteArtifacts(project);
            }
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

                Assert.True(File.Exists(Artifact(project, "trial")));
                Assert.True(File.Exists(Artifact(project, "meta")));
                Assert.False(File.Exists(Artifact(project, "summary")));
            }
            finally
            {
                DeleteArtifacts(project);
            }
        }

        private static string Artifact(string project, string kind) => FolderDir.Experiment.GetPathFile($"{project}-{kind}.csv");

        private static List<string[]> Read(string project, string kind) => ReadFile(Artifact(project, kind));

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
