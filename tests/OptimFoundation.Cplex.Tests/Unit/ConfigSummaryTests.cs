using System;
using System.IO;
using System.Linq;
using OptimFoundation.Core;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    /// <summary>
    /// 驗證逐 seed 跟基準比大小（<see cref="ConfigSummary"/> 與主表 VsBaseline 欄）：
    /// 依序比有沒有找到解、有沒有證明最佳、都證明最佳比時間、都沒證明比 gap，都沒找到解算平手。
    /// 用假造的 Trial，不需要 CPLEX。
    /// </summary>
    public class ConfigSummaryTests
    {
        [Fact]
        public void Summaries_CompareEachSeedWithBaseline()
        {
            var experiment = new Experiment("ConfigSummaryTests", "summary", "per-seed comparison");
            experiment.AddTrial(Trial("r1-warmup-exclude", 11, SolveStatus.Optimal, 9000, 0));
            experiment.AddTrial(Trial("r1-baseline-s11", 11, SolveStatus.Optimal, 1000, 0));
            experiment.AddTrial(Trial("r1-baseline-s22", 22, SolveStatus.Optimal, 2000, 0));
            experiment.AddTrial(Trial("r1-baseline-s33", 33, SolveStatus.Feasible, 30000, 0.02));
            experiment.AddTrial(Trial("r1-baseline-s44", 44, SolveStatus.TimeLimit, 30000, double.NaN));
            experiment.AddTrial(Trial("r1-baseline-s55", 55, SolveStatus.Optimal, 3000, 0));
            // 都證明最佳比時間：s11 快 → 贏、s22 慢 → 輸；都沒證明比 gap：s33 → 贏；都沒找到解：s44 → 平手；基準證明最佳而自己沒有：s55 → 輸
            experiment.AddTrial(Trial("r1-emphasis1-s11", 11, SolveStatus.Optimal, 500, 0));
            experiment.AddTrial(Trial("r1-emphasis1-s22", 22, SolveStatus.Optimal, 2500, 0));
            experiment.AddTrial(Trial("r1-emphasis1-s33", 33, SolveStatus.Feasible, 30000, 0.01));
            experiment.AddTrial(Trial("r1-emphasis1-s44", 44, SolveStatus.TimeLimit, 30000, double.NaN));
            experiment.AddTrial(Trial("r1-emphasis1-s55", 55, SolveStatus.Feasible, 30000, 0.001));
            // 自己求解失敗：s11 → 輸；s33 證明最佳而基準沒有 → 贏；s44 找到解而基準沒有 → 贏；s66 沒有同 seed 的基準 → 無法比較
            experiment.AddTrial(Trial("r1-cuts2-s11", 11, SolveStatus.Error, 10, double.NaN));
            experiment.AddTrial(Trial("r1-cuts2-s22", 22, SolveStatus.Optimal, 1500, 0));
            experiment.AddTrial(Trial("r1-cuts2-s33", 33, SolveStatus.Optimal, 9000, 0));
            experiment.AddTrial(Trial("r1-cuts2-s44", 44, SolveStatus.Feasible, 30000, 0.3));
            experiment.AddTrial(Trial("r1-cuts2-s66", 66, SolveStatus.Optimal, 100, 0));

            var summaries = experiment.Summaries;
            Assert.Equal(3, summaries.Count);

            var baseline = summaries[0];
            Assert.True(baseline.IsBaseline);
            Assert.Equal("r1-baseline", baseline.Config);
            Assert.Equal(5, baseline.Trials);
            Assert.Equal("11 22 33 44 55", baseline.Seeds);
            Assert.Equal(3, baseline.Optimal);
            Assert.Equal(1, baseline.Feasible);
            Assert.Equal(1, baseline.NoSolution);
            Assert.Equal(4, baseline.FoundSolution);
            Assert.Null(baseline.Wins);
            Assert.Null(baseline.Losses);
            Assert.Null(baseline.Ties);
            Assert.Null(baseline.NotCompared);

            var emphasis = summaries[1];
            Assert.Equal("r1-emphasis1", emphasis.Config);
            Assert.Equal(2, emphasis.Wins);
            Assert.Equal(2, emphasis.Losses);
            Assert.Equal(1, emphasis.Ties);
            Assert.Equal(0, emphasis.NotCompared);

            var cuts = summaries[2];
            Assert.Equal("r1-cuts2", cuts.Config);
            Assert.Equal(1, cuts.Failed);
            Assert.Equal(3, cuts.Wins);
            Assert.Equal(1, cuts.Losses);
            Assert.Equal(0, cuts.Ties);
            Assert.Equal(1, cuts.NotCompared);
        }

        [Fact]
        public void Summaries_BaselineFailure_IsNotCompared()
        {
            var experiment = new Experiment("ConfigSummaryTests", "summary", "baseline failed");
            experiment.AddTrial(Trial("r1-baseline-s11", 11, SolveStatus.Error, 10, double.NaN));
            experiment.AddTrial(Trial("r1-variant-s11", 11, SolveStatus.Optimal, 1000, 0));

            var variant = experiment.Summaries.Single(s => !s.IsBaseline);
            Assert.Equal(0, variant.Wins);
            Assert.Equal(1, variant.NotCompared);
        }

        [Fact]
        public void Summaries_BaselineSkipsWarmupEvenWhenNoLabelSaysBaseline()
        {
            var experiment = new Experiment("ConfigSummaryTests", "summary", "warm-up first");
            experiment.AddTrial(Trial("warmup-exclude", 11, SolveStatus.Optimal, 9000, 0));
            experiment.AddTrial(Trial("r0-s1", 1, SolveStatus.Optimal, 1000, 0));
            experiment.AddTrial(Trial("r0-s2", 2, SolveStatus.Optimal, 1200, 0));

            var summary = Assert.Single(experiment.Summaries);
            Assert.Equal("r0", summary.Config);
            Assert.True(summary.IsBaseline);
            Assert.Equal(2, summary.Trials);
        }

        [Fact]
        public void Summaries_CompareWithTheSameModelsBaseline()
        {
            var experiment = new Experiment("ConfigSummaryTests", "summary", "two models");
            experiment.AddTrial(Trial("r1-baseline-s11", 11, SolveStatus.Optimal, 1000, 0, "Small"));
            experiment.AddTrial(Trial("r1-baseline-s11", 11, SolveStatus.Optimal, 1000, 0, "Large"));
            experiment.AddTrial(Trial("r1-variant-s11", 11, SolveStatus.Optimal, 500, 0, "Small"));
            experiment.AddTrial(Trial("r1-variant-s11", 11, SolveStatus.Optimal, 2000, 0, "Large"));

            var summaries = experiment.Summaries;
            Assert.Equal(new[] { "Small", "Small", "Large", "Large" }, summaries.Select(s => s.Model));
            Assert.Equal(1, summaries.Single(s => s.Model == "Small" && !s.IsBaseline).Wins);
            Assert.Equal(1, summaries.Single(s => s.Model == "Large" && !s.IsBaseline).Losses);
        }

        [Fact]
        public void MainCsv_WritesVsBaselinePerTrial()
        {
            var experiment = new Experiment("ConfigSummaryTests", "summary", "main csv");
            experiment.AddTrial(Trial("r1-warmup-exclude", 11, SolveStatus.Optimal, 9000, 0));
            experiment.AddTrial(Trial("r1-baseline-s11", 11, SolveStatus.Optimal, 1000, 0));
            experiment.AddTrial(Trial("r1-baseline-s22", 22, SolveStatus.Optimal, 1000, 0));
            experiment.AddTrial(Trial("r1-variant-s11", 11, SolveStatus.Optimal, 500, 0));
            experiment.AddTrial(Trial("r1-variant-s22", 22, SolveStatus.Optimal, 1000, 0));
            experiment.AddTrial(Trial("r1-variant-s33", 33, SolveStatus.Optimal, 500, 0));

            string path = Path.Combine(Path.GetTempPath(), $"vs-baseline-{Guid.NewGuid():N}.csv");
            try
            {
                new CsvExperimentWriter().Write(experiment, path);
                var rows = File.ReadAllLines(path).Select(line => line.Split(',')).ToList();
                int label = Array.IndexOf(rows[0], "TrialLabel");
                int vs = Array.IndexOf(rows[0], "VsBaseline");
                var result = rows.Skip(1).ToDictionary(r => r[label], r => r[vs]);

                Assert.Equal(CsvExperimentWriter.NotAvailable, result["r1-warmup-exclude"]);
                Assert.Equal(CsvExperimentWriter.Baseline, result["r1-baseline-s11"]);
                Assert.Equal(CsvExperimentWriter.Baseline, result["r1-baseline-s22"]);
                Assert.Equal(CsvExperimentWriter.Win, result["r1-variant-s11"]);
                Assert.Equal(CsvExperimentWriter.Tie, result["r1-variant-s22"]);
                Assert.Equal(CsvExperimentWriter.NotAvailable, result["r1-variant-s33"]);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>假造一筆 Trial；有找到解時目標值 10、界 9，沒找到解時三者皆為 NaN。</summary>
        private static Trial Trial(string label, int seed, SolveStatus status, double solveTimeMs, double gap, string model = "Canonical")
        {
            bool hasIncumbent = status is SolveStatus.Optimal or SolveStatus.Feasible;
            var config = new ConfigSnapshot { Solver = "Cplex" };
            config.Tunable["Seed"] = seed;
            config.Tunable["TimeLimit"] = 30.0;

            var metrics = new SolveMetrics
            {
                Status = status,
                ObjectiveValue = hasIncumbent ? 10 : double.NaN,
                BestBound = hasIncumbent ? 9 : double.NaN,
                Gap = hasIncumbent ? gap : double.NaN,
                SolveTimeMs = solveTimeMs,
            };

            return new Trial { Label = label, ExperimentId = "20260930-120000", Model = model, Config = config, Metrics = metrics };
        }
    }
}
