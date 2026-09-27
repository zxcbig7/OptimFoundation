using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 每個 Trial 一列的扁平 CSV，給人用 Excel 直接看：這次跑了哪些設定、各自在基準上改了什麼、結果如何。
    ///
    /// 不再把一堆設定欄攤平在這裡——那樣改到沒被攤平的旋鈕時，兩列會長得一模一樣。
    /// 改成只寫「跟基準比差在哪」（DiffKnobs），基準本身的完整設定寫在同名的 -meta.csv。
    /// 收斂軌跡另外寫 -trajectory.csv。
    /// </summary>
    public sealed class CsvExperimentWriter
    {
        private static readonly string[] Header =
        {
            "RunId", "TrialId", "Model", "TrialLabel", "BasedOn", "DiffKnobs", "Seed", "RunAt",
            "Status", "ObjectiveValue", "BestBound", "MipGap", "RunTimeMs",
            "TFeasMs", "TStallMs", "DeltaBound",
            "NodeCount", "IterationCount", "TrajectoryPoints",
            "VarCount", "ConstraintCount", "Note"
        };

        /// <summary>把整個實驗寫成一列一 trial 的 CSV。</summary>
        public void Write(Experiment experiment, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Header));

            // 每一批（RunId）各自找自己的基準：呼叫端手動把不同批的 trial 加進同一個 Experiment 時，
            // 拿別批的基準來比會比出「跨批的設定差異」——那不是這批改了什麼，只會誤導人。
            var baselineOfRun = experiment.Trials
                .GroupBy(t => t.ExperimentId ?? "")
                .ToDictionary(g => g.Key, g => FindBaselineIn(g.ToList()));

            foreach (var t in experiment.Trials)
            {
                var m = t.Metrics;
                var baseline = baselineOfRun[t.ExperimentId ?? ""];
                bool isBaseline = ReferenceEquals(t, baseline);

                var cells = new List<string>
                {
                    Cell(t.ExperimentId),
                    t.TrialId > 0 ? t.TrialId.ToString(CultureInfo.InvariantCulture) : "",
                    Cell(t.Model),
                    Cell(t.Label),
                    isBaseline ? "" : Cell(baseline?.Label),
                    isBaseline ? "" : Cell(DiffAgainst(baseline, t)),
                    Setting(t, "Seed"),
                    Cell(t.RunTime.ToString("yyyy-MM-dd HH:mm:ss"))
                };

                if (m != null)
                {
                    cells.Add(Cell(m.Status.ToString()));
                    cells.Add(Num(m.ObjectiveValue));
                    cells.Add(Num(m.BestBound));
                    cells.Add(Num(m.MipGap));
                    cells.Add(Num(m.RunTimeMs));
                    cells.Add(NumOrBlank(m.TFeasMs));
                    cells.Add(NumOrBlank(m.TStallMs));
                    cells.Add(NumOrBlank(m.DeltaBound));
                    cells.Add(m.NodeCount?.ToString(CultureInfo.InvariantCulture) ?? "");
                    cells.Add(m.IterationCount?.ToString(CultureInfo.InvariantCulture) ?? "");
                    cells.Add(m.TrajectoryPoints.ToString(CultureInfo.InvariantCulture));
                    cells.Add(m.VarCount.ToString(CultureInfo.InvariantCulture));
                    cells.Add(m.ConstraintCount.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    for (int i = 0; i < 13; i++) cells.Add("");
                }

                cells.Add(Cell(t.Note));
                sb.AppendLine(string.Join(",", cells));
            }

            // UTF-8 with BOM：讓 zh-TW Excel 正確辨識中文，避免被當成 Big5 讀成亂碼
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// 找出當作比較基準的那一筆：標籤含 "baseline" 的第一筆；都沒有的話用第一筆。
        /// 一輪實驗裡只會有一組基準，所以這個規則夠用，也不必額外設定。
        /// </summary>
        internal static Trial FindBaselineIn(IList<Trial> trials)
        {
            if (trials == null || trials.Count == 0) return null;
            return trials.FirstOrDefault(t =>
                       (t.Label ?? "").IndexOf("baseline", System.StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? trials[0];
        }

        /// <summary>取整個實驗最後一批的基準，給說明檔用。</summary>
        internal static Trial FindBaseline(Experiment experiment)
        {
            var trials = experiment?.Trials;
            if (trials == null || trials.Count == 0) return null;
            var lastRunId = trials[trials.Count - 1].ExperimentId ?? "";
            return FindBaselineIn(trials.Where(t => (t.ExperimentId ?? "") == lastRunId).ToList());
        }

        /// <summary>
        /// 這筆跟基準比差在哪，寫成 "旋鈕=值"，多顆用分號隔開。
        /// Seed 不算差異——它是同一組設定重跑幾次用的，本身另有一欄。
        /// </summary>
        internal static string DiffAgainst(Trial baseline, Trial trial)
        {
            var base_ = baseline?.Config?.SolverSpecific;
            var mine = trial?.Config?.SolverSpecific;
            if (base_ == null || mine == null) return "";

            var keys = new SortedSet<string>(base_.Keys);
            keys.UnionWith(mine.Keys);

            var parts = new List<string>();
            foreach (var k in keys)
            {
                if (k == "Seed") continue;
                base_.TryGetValue(k, out var b);
                mine.TryGetValue(k, out var v);
                if (Equals(Text(b), Text(v))) continue;
                // 設回預設（本次沒設、基準有設）就寫成「旋鈕=預設」，讓人看得出是被拿掉了
                parts.Add($"{k}={(v == null ? "預設" : Text(v))}");
            }
            return string.Join(";", parts);
        }

        private static string Setting(Trial t, string key)
        {
            var c = t?.Config;
            if (c == null) return "";
            if (c.Tunable.TryGetValue(key, out var v)) return Cell(v);
            if (c.SolverSpecific.TryGetValue(key, out var v2)) return Cell(v2);
            return "";
        }

        private static string Text(object v) =>
            v == null ? null : System.Convert.ToString(v, CultureInfo.InvariantCulture);

        /// <summary>
        /// 數值轉字串。NaN 照實寫 "NaN"——空白代表「沒這個值 / 沒設定」，
        /// 跟「求解器算出來就是 NaN」是兩件事，混在一起會讓人把 NaN 當成 0 去平均。
        /// </summary>
        private static string Num(double d) =>
            double.IsNaN(d) ? "NaN"
            : double.IsPositiveInfinity(d) ? "Infinity"
            : double.IsNegativeInfinity(d) ? "-Infinity"
            : d.ToString("R", CultureInfo.InvariantCulture);

        private static string NumOrBlank(double? d) => d.HasValue ? Num(d.Value) : "";

        /// <summary>CSV 欄位值：含逗號、雙引號或換行時加引號並跳脫。</summary>
        private static string Cell(object v)
        {
            if (v == null) return "";
            string s = v is double d ? Num(d) : System.Convert.ToString(v, CultureInfo.InvariantCulture);
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
    /// <summary>
    /// 實驗的「說明檔」：整批實驗裡不會變的東西只寫一次。
    ///
    /// 包含：這批實驗是什麼時候跑的、跑的是哪個模型多大、求解環境、以及**基準的完整設定**。
    /// 主表因此只要記「跟基準差在哪」就好，不必每一列重抄一次設定。
    ///
    /// 格式是三欄的 Section / Key / Value，Excel 打開就是一張清單，加東西也不用改欄位。
    /// </summary>
    public sealed class MetaCsvWriter
    {
        /// <summary>格式版本。之後欄位有變動就加一，讀的人才知道自己在看哪一版。v2：新增 modelStats 區段。</summary>
        public const int SchemaVersion = 2;

        /// <summary>寫出說明檔。</summary>
        public void Write(Experiment experiment, string path)
        {
            var rows = new List<(string Section, string Key, string Value)>
            {
                ("schema", "version", SchemaVersion.ToString(CultureInfo.InvariantCulture)),
                ("experiment", "name", experiment?.Name),
                ("experiment", "description", experiment?.Description),
                ("experiment", "trialCount", (experiment?.Trials?.Count ?? 0).ToString(CultureInfo.InvariantCulture)),
                ("experiment", "writtenAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
            };

            var trials = experiment?.Trials ?? new List<Trial>();

            // 把每批的識別與開始時間都列出來（OptExperiment 一次 Run 只有一批；手動組的 Experiment 可能多批）
            foreach (var runId in trials.Select(t => t.ExperimentId).Where(r => !string.IsNullOrEmpty(r)).Distinct().OrderBy(r => r))
            {
                var first = trials.First(t => t.ExperimentId == runId);
                rows.Add(("run", $"{runId}.startedAt", first.RunTime.ToString("yyyy-MM-dd HH:mm:ss")));
                rows.Add(("run", $"{runId}.trialCount", trials.Count(t => t.ExperimentId == runId).ToString(CultureInfo.InvariantCulture)));
            }

            // 模型：跑了哪些、各自多大
            foreach (var model in trials.Select(t => t.Model).Where(m => !string.IsNullOrEmpty(m)).Distinct().OrderBy(m => m))
            {
                var sample = trials.First(t => t.Model == model && t.Metrics != null);
                if (sample?.Metrics == null) continue;
                rows.Add(("model", $"{model}.varCount", sample.Metrics.VarCount.ToString(CultureInfo.InvariantCulture)));
                rows.Add(("model", $"{model}.constraintCount", sample.Metrics.ConstraintCount.ToString(CultureInfo.InvariantCulture)));
            }

            // 模型統計對帳：框架建模記帳 vs solver 模型實際。同一模型各 trial 應該一致，有不一致的就拿那一筆來列原因
            foreach (var model in trials.Select(t => t.Model).Where(m => !string.IsNullOrEmpty(m)).Distinct().OrderBy(m => m))
            {
                var withStats = trials.Where(t => t.Model == model && t.Metrics?.ModelStats != null).ToList();
                if (withStats.Count == 0) continue;

                var report = (withStats.FirstOrDefault(t => !t.Metrics.ModelStats.IsMatch) ?? withStats[0]).Metrics.ModelStats;
                var f = report.Framework ?? new ModelCounts();
                var s = report.Solver ?? new ModelCounts();
                rows.Add(("modelStats", $"{model}.result", report.Summary));
                rows.Add(("modelStats", $"{model}.source", report.Source));
                rows.Add(("modelStats", $"{model}.variables", $"framework={f.Variables} index={report.IndexedVariables} solver={s.Variables}"));
                rows.Add(("modelStats", $"{model}.types", $"binary {f.Binary}/{s.Binary} integer {f.Integer}/{s.Integer} continuous {f.Continuous}/{s.Continuous}"));
                rows.Add(("modelStats", $"{model}.constraints", $"framework={f.Constraints} index={report.IndexedConstraints} solver={s.Constraints}"));
                rows.Add(("modelStats", $"{model}.specialElements", $"framework={f.SpecialElements} solver={s.SpecialElements} ({s.SpecialDetail})"));
                rows.Add(("modelStats", $"{model}.objective", $"framework={f.Objective?.ToString() ?? "None"} solver={s.Objective?.ToString() ?? "None"}"));
                rows.Add(("modelStats", $"{model}.mismatchTrials", $"{withStats.Count(t => !t.Metrics.ModelStats.IsMatch)}/{withStats.Count}"));
                foreach (var m in report.Mismatches ?? new List<ModelStatsMismatch>())
                    rows.Add(("modelStats", $"{model}.mismatch.{m.Item}", m.Reason));
            }

            var baseline = CsvExperimentWriter.FindBaseline(experiment);
            if (baseline?.Config != null)
            {
                rows.Add(("environment", "solver", baseline.Config.Solver));
                rows.Add(("environment", "machine", Environment.MachineName));

                rows.Add(("baseline", "label", baseline.Label));
                // 基準的完整設定：只有「真的有設」的旋鈕，沒列到的就是用求解器預設
                foreach (var kv in baseline.Config.SolverSpecific.OrderBy(k => k.Key))
                    rows.Add(("baseline", kv.Key, Convert.ToString(kv.Value, CultureInfo.InvariantCulture)));
            }

            var sb = new StringBuilder();
            sb.AppendLine("Section,Key,Value");
            foreach (var r in rows)
                sb.AppendLine($"{Cell(r.Section)},{Cell(r.Key)},{Cell(r.Value)}");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Cell(object v)
        {
            if (v == null) return "";
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }

    /// <summary>
    /// 長格式 CSV（1 列 / 收斂點）：把各 Trial 的 Convergence 軌跡攤平，供直接畫
    /// 「incumbent / bound / gap 隨時間」的收斂曲線。與 <see cref="CsvExperimentWriter"/>
    /// （1 列 / Trial 的摘要）互補。
    /// </summary>
    public sealed class TrajectoryCsvWriter
    {
        /// <summary>
        /// 把所有 Trial 的收斂軌跡攤平成長格式 CSV（欄位 RunAt, Label, PointIndex, TimeMs, Objective, Bound, Gap）。
        /// 沒有軌跡的 Trial 直接跳過；NaN / Infinity 寫成空白，畫圖時自然斷點。
        /// </summary>
        public void Write(Experiment experiment, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RunAt,Label,PointIndex,TimeMs,Objective,Bound,Gap");

            foreach (var t in experiment.Trials)
            {
                var pts = t.Metrics?.Convergence;
                if (pts == null) continue;

                int i = 0;
                foreach (var p in pts)
                {
                    var cells = new[]
                    {
                        Cell(t.RunTime.ToString("yyyy-MM-dd HH:mm:ss")),
                        Cell(t.Label),
                        i.ToString(CultureInfo.InvariantCulture),
                        Num(p.TimeMs),
                        Num(p.Objective),
                        Num(p.Bound),
                        Num(p.Gap)
                    };
                    sb.AppendLine(string.Join(",", cells));
                    i++;
                }
            }

            // UTF-8 with BOM：與 CsvExperimentWriter 一致，讓 zh-TW Excel 正確辨識中文 Label
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Num(double v)
            => double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("R", CultureInfo.InvariantCulture);

        private static string Cell(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
