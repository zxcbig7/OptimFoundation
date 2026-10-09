using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Integration
{
    /// <summary>
    /// 驗證專案資料夾、保留期、log 命名、Solve 紀錄、BeforeSolve 順序與軌跡開關。
    /// </summary>
    [Collection("Logging")]
    public class ProjectScopeIntegrationTests
    {
        private static readonly bool CplexAvailable =
            File.Exists(@"C:\IBM\ILOG\CPLEX_Studio2211\cplex\bin\x64_win64\ILOG.CPLEX.dll");

        private static string NewName(string prefix) => prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        // 使用需要分支搜尋的背包問題，啟用軌跡時應至少收到一個取樣點，據此確認開關是否生效。
        private static OptModel Knapsack(string name, List<string>? order = null)
        {
            var values = new double[] { 41, 50, 49, 59, 55, 57, 60, 8, 12, 15, 33, 21, 18, 27, 44 };
            var weights = new double[] { 40, 49, 50, 59, 55, 57, 60, 7, 11, 14, 32, 20, 17, 26, 43 };
            var items = Enumerable.Range(0, values.Length).Select(i => "i" + i).ToList();
            return new OptModel(name)
                .AddVariables(e => e.BuildBVs<VarS>(items))
                .AddObjective(e =>
                {
                    for (int i = 0; i < values.Length; i++) e.AddLHS(values[i], new VarS { S = "i" + i });
                    e.CreateMaximize();
                })
                .AddConstraints(e =>
                {
                    for (int i = 0; i < weights.Length; i++) e.AddLHS(weights[i], new VarS { S = "i" + i });
                    e.CreateLessEqual(170, "cap");
                    order?.Add("apply");
                });
        }

        private static string OldFile(FolderDir.ProjFolder folder, string name)
        {
            folder.CreateFolder();
            string path = folder.GetPathFile(name);
            File.WriteAllText(path, "old");
            File.SetLastWriteTime(path, DateTime.Now.AddDays(-40));
            return path;
        }

        private static void DeleteIfExists(params string[] paths)
        {
            foreach (string path in paths)
                if (File.Exists(path)) File.Delete(path);
        }

        private static string ReadLatestLog(string prefix)
        {
            string path = Directory.GetFiles(FolderDir.Log.GetPath(), $"{prefix}_*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        // ── AC1 / AC2：建立專案就建好全部資料夾並清舊檔 ─────────────────

        [Fact(DisplayName = "AC1：建立專案時 FolderDir 7 個資料夾全部存在")]
        public void Constructor_CreatesEveryFolder()
        {
            string iis = FolderDir.IIS.GetPath();
            if (Directory.Exists(iis) && !Directory.EnumerateFileSystemEntries(iis).Any())
                Directory.Delete(iis);

            _ = new OptProject(NewName("Folders"), retentionDays: 0);

            foreach (var folder in new[]
            {
                FolderDir.Input, FolderDir.Output, FolderDir.Log, FolderDir.Model,
                FolderDir.IIS, FolderDir.Solution, FolderDir.Experiment,
            })
                Assert.True(Directory.Exists(folder.GetPath()), folder.GetPath());
        }

        [Fact(DisplayName = "AC2：保留期清理刪輸出舊檔，但保留 Experiment 與 Input")]
        public void Constructor_PurgesOldOutputs_ButKeepsExperimentAndInput()
        {
            string tag = NewName("Purge");
            string model = OldFile(FolderDir.Model, $"{tag}.lp");
            string experiment = OldFile(FolderDir.Experiment, $"{tag}.csv");
            string input = OldFile(FolderDir.Input, $"{tag}.csv");

            try
            {
                _ = new OptProject(tag, retentionDays: 30);

                Assert.False(File.Exists(model));
                Assert.True(File.Exists(experiment));
                Assert.True(File.Exists(input));
            }
            finally
            {
                DeleteIfExists(model, experiment, input);
            }
        }

        [Fact(DisplayName = "AC2：retentionDays <= 0 不清理")]
        public void Constructor_RetentionZero_DoesNotPurge()
        {
            string tag = NewName("NoPurge");
            string model = OldFile(FolderDir.Model, $"{tag}.lp");

            try
            {
                _ = new OptProject(tag, retentionDays: 0);
                Assert.True(File.Exists(model));
            }
            finally
            {
                DeleteIfExists(model);
            }
        }

        [Theory(DisplayName = "專案名空白或含非法檔名字元丟 ArgumentException")]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("bad:name")]
        public void Constructor_InvalidName_Throws(string name)
        {
            Assert.Throws<ArgumentException>(() => new OptProject(name, retentionDays: 0));
        }

        // ── AC3：log 檔名只由框架決定 ─────────────────────────────────────

        [Fact(DisplayName = "AC3：建立專案寫進 {專案名} log，建立實驗立刻切到 {專案名}-{實驗名}_exp")]
        public void LogFileName_FollowsProjectThenExperiment()
        {
            string tag = NewName("LogScope");
            var project = new OptProject(tag, retentionDays: 0);
            Assert.Contains($"[專案初始化完成] 名稱={tag}", ReadLatestLog(tag));

            _ = project.Experiment("r1", "log scope");
            string marker = "marker-" + Guid.NewGuid().ToString("N");
            Logging.Info(marker);

            Assert.Contains(marker, ReadLatestLog($"{tag}-r1_exp"));
            Assert.DoesNotContain(marker, ReadLatestLog(tag));
        }

        // ── AC5 / AC6：Production 也留紀錄 ─────────────────────────────────────

        [Fact(DisplayName = "AC5/AC6：Production 記一筆不開軌跡的 Trial，寫成 {專案名}-production-trial.csv（每次 Production 覆寫），不寫 -summary.csv")]
        public void Production_RecordsTrialWithoutTrajectory()
        {
            if (!CplexAvailable) return;
            string tag = NewName("SolveRecord");

            try
            {
                using var project = new OptProject(tag, retentionDays: 0);

                project.Production()
                    .AddProjectConfig(ProjectConfig.Quiet())
                    .AddModel(Knapsack("Knapsack"))
                    .AddSolverConfig("production", new CplexConfig { TimeLimit = 30 })
                    .Run();
                Assert.True(project.IsSuccess);
                string firstRunId = project.Trial.ExperimentId;
                project.Production()
                    .AddProjectConfig(ProjectConfig.Quiet())
                    .AddModel(Knapsack("Knapsack"))
                    .AddSolverConfig("production", new CplexConfig { TimeLimit = 30 })
                    .Run();
                Assert.True(project.IsSuccess);

                Trial trial = project.Trial;
                Assert.NotNull(trial);
                Assert.Equal("production", trial.Label);
                Assert.Equal("Knapsack", trial.Model);
                Assert.Equal(1, trial.TrialId);
                Assert.False(string.IsNullOrEmpty(trial.ExperimentId));
                Assert.NotEqual(firstRunId, trial.ExperimentId);
                Assert.Equal(SolveStatus.Optimal, trial.Metrics.Status);
                Assert.Equal(0, trial.Metrics.TrajectoryPoints);

                // 第二次 Production 覆寫第一次的紀錄；正式環境沒有可比的設定，不寫彙總
                string prefix = $"{tag}-{OptProject.ProductionExperimentName}";
                string csv = FolderDir.Experiment.GetPathFile(prefix + "-trial.csv");
                Assert.True(File.Exists(FolderDir.Experiment.GetPathFile(prefix + "-meta.csv")));
                Assert.False(File.Exists(FolderDir.Experiment.GetPathFile(prefix + "-summary.csv")));
                string[] lines = File.ReadAllLines(csv);
                Assert.Equal(2, lines.Length);
                Assert.StartsWith("1,Knapsack,BP,production,", lines[1]);
            }
            finally
            {
                foreach (string kind in new[] { "trial", "meta", "summary", "trajectory" })
                    DeleteIfExists(FolderDir.Experiment.GetPathFile($"{tag}-{OptProject.ProductionExperimentName}-{kind}.csv"));
            }
        }

        // ── AC8：BeforeSolve 在套用模型後、求解前 ─────────────────────────

        [Fact(DisplayName = "AC8：BeforeSolve 在 ApplyTo 之後、Solve 之前；可在這裡開軌跡")]
        public void Production_BeforeSolve_RunsBetweenApplyAndSolve()
        {
            if (!CplexAvailable) return;
            var order = new List<string>();

            using var project = new OptProject(NewName("BeforeSolve"), retentionDays: 0);

            project.Production()
                .AddProjectConfig(ProjectConfig.Quiet())
                .AddModel(Knapsack("Knapsack", order))
                .AddSolverConfig("production", new CplexConfig { TimeLimit = 30 })
                .OnSolved(_ => order.Add("solved"))
                .BeforeSolve(e =>
                {
                    order.Add("before");
                    e.EnableTrajectory();
                })
                .Run();
            bool solved = project.IsSuccess;

            Assert.True(solved);
            Assert.Equal(new[] { "apply", "before", "solved" }, order);
            Assert.True(project.Trial.Metrics.TrajectoryPoints >= 1);
        }

        // ── AC7：實驗可關軌跡 ─────────────────────────────────────────────

        [Fact(DisplayName = "AC7：實驗預設記軌跡，CaptureTrajectory(false) 全部 Trial 無軌跡")]
        public void Experiment_CaptureTrajectory_TogglesConvergence()
        {
            if (!CplexAvailable) return;
            string tag = NewName("ExpTrajectory");
            var project = new OptProject(tag, retentionDays: 0);

            try
            {
                Experiment withTrajectory = project.Experiment("on", "default")
                    .AddTrial(Knapsack("Knapsack"), "base", new CplexConfig { TimeLimit = 30 })
                    .Run();
                Experiment withoutTrajectory = project.Experiment("off", "no trajectory")
                    .CaptureTrajectory(false)
                    .AddTrial(Knapsack("Knapsack"), "base", new CplexConfig { TimeLimit = 30 })
                    .Run();

                Assert.True(withTrajectory.Trials.Single().Metrics.TrajectoryPoints >= 1);
                Assert.All(withoutTrajectory.Trials, t => Assert.Equal(0, t.Metrics.TrajectoryPoints));
            }
            finally
            {
                foreach (string experimentName in new[] { "on", "off" })
                    foreach (string kind in new[] { "trial", "meta", "summary", "trajectory" })
                        DeleteIfExists(FolderDir.Experiment.GetPathFile($"{tag}-{experimentName}-{kind}.csv"));
            }
        }

        // ── 單組與多組同一套寫法 ─────────────────────────────────────────

        [Fact(DisplayName = "實驗也能掛 BeforeSolve / OnSolved：每組各執行一次，跑完釋放 engine，不動 project.Engine")]
        public void Experiment_BeforeSolveAndOnSolved_RunPerTrial()
        {
            if (!CplexAvailable) return;
            string tag = NewName("ExpHooks");
            var project = new OptProject(tag, retentionDays: 0);
            var order = new List<string>();

            try
            {
                project.Experiment("hooks")
                    .AddModel(Knapsack("Knapsack"))
                    .AddSolverConfig("a", new CplexConfig { TimeLimit = 30 })
                    .AddSolverConfig("b", new CplexConfig { TimeLimit = 30 })
                    .BeforeSolve(_ => order.Add("before"))
                    .OnSolved(_ => order.Add("solved"))
                    .Run();

                Assert.Equal(new[] { "before", "solved", "before", "solved" }, order);
                Assert.Null(project.Engine);
            }
            finally
            {
                foreach (string kind in new[] { "trial", "meta", "summary", "trajectory" })
                    DeleteIfExists(FolderDir.Experiment.GetPathFile($"{tag}-hooks-{kind}.csv"));
            }
        }

        [Fact(DisplayName = "正式環境超過一組：Run 直接丟例外，不建 engine、不寫紀錄")]
        public void Production_MultiplePairs_ThrowsBeforeRunning()
        {
            if (!CplexAvailable) return;
            string tag = NewName("SolveMulti");
            using var project = new OptProject(tag, retentionDays: 0);

            var ex = Assert.Throws<InvalidOperationException>(() => project.Production()
                .AddProjectConfig(ProjectConfig.Quiet())
                .AddModel(Knapsack("Knapsack"))
                .AddSolverConfig("first", new CplexConfig { TimeLimit = 30 })
                .AddSolverConfig("second", new CplexConfig { TimeLimit = 30 })
                .Run());

            Assert.Contains("project.Experiment(name)", ex.Message);
            Assert.Null(project.Engine);
            Assert.False(project.IsSuccess);
            Assert.False(File.Exists(FolderDir.Experiment.GetPathFile($"{tag}-{OptProject.ProductionExperimentName}-trial.csv")));
        }

        [Fact(DisplayName = "AddProjectConfig 加第二次：以後加入的為準並 WARN")]
        public void AddProjectConfig_Twice_LastWinsAndWarns()
        {
            string tag = NewName("ProjectConfigTwice");
            var project = new OptProject(tag, retentionDays: 0);

            project.Production()
                .AddProjectConfig(new ProjectConfig())
                .AddProjectConfig(ProjectConfig.Quiet());

            Assert.Contains("[專案設定重複加入]", ReadLatestLog(tag));
        }

        [Fact(DisplayName = "實驗可以一對一：單組照樣跑並寫 summary")]
        public void Experiment_SinglePair_Runs()
        {
            if (!CplexAvailable) return;
            string tag = NewName("ExpSingle");
            var project = new OptProject(tag, retentionDays: 0);

            try
            {
                Experiment result = project.Experiment("single")
                    .AddModel(Knapsack("Knapsack"))
                    .AddSolverConfig("base", new CplexConfig { TimeLimit = 30 })
                    .Run();

                Assert.Single(result.Trials);
                Assert.True(File.Exists(FolderDir.Experiment.GetPathFile($"{tag}-single-summary.csv")));
            }
            finally
            {
                foreach (string kind in new[] { "trial", "meta", "summary", "trajectory" })
                    DeleteIfExists(FolderDir.Experiment.GetPathFile($"{tag}-single-{kind}.csv"));
            }
        }

        [Fact(DisplayName = "OnSolved 丟例外：原樣拋出，已完成的紀錄照存")]
        public void Production_OnSolvedThrows_SavesCompletedRecord()
        {
            if (!CplexAvailable) return;
            string tag = NewName("SolveThrow");
            using var project = new OptProject(tag, retentionDays: 0);

            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => project.Production()
                    .AddProjectConfig(ProjectConfig.Quiet())
                    .AddModel(Knapsack("Knapsack"))
                    .AddSolverConfig("production", new CplexConfig { TimeLimit = 30 })
                    .OnSolved(_ => throw new InvalidOperationException("boom"))
                    .Run());

                Assert.Equal("boom", ex.Message);
                Assert.True(project.IsSuccess);
                string csv = FolderDir.Experiment.GetPathFile($"{tag}-{OptProject.ProductionExperimentName}-trial.csv");
                Assert.Equal(2, File.ReadAllLines(csv).Length);
                Assert.Contains("[試跑中斷]", ReadLatestLog(tag));
            }
            finally
            {
                foreach (string kind in new[] { "trial", "meta", "summary", "trajectory" })
                    DeleteIfExists(FolderDir.Experiment.GetPathFile($"{tag}-{OptProject.ProductionExperimentName}-{kind}.csv"));
            }
        }
    }
}
