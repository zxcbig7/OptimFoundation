using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 每筆 Trial 一列；ConfigChanges 只列相對基準的差異，VsBaseline 記逐 seed 比較結果。
    /// 完整基準與模型資訊寫入 -meta.csv，軌跡寫入 -trajectory.csv；缺值使用下列標記。
    /// </summary>
    public sealed class CsvExperimentWriter
    {
        #region 缺值標記

        /// <summary>沒有收集：這次求解沒開收斂軌跡（FirstSolutionMs / LastBoundChangeMs / BoundChange）。</summary>
        public const string Off = "off";

        /// <summary>有收集但事件沒發生：沒找到可行解、界從未變動、與基準無差異、求解前就失敗。</summary>
        public const string None = "none";

        /// <summary>資料未提供、未量測或無法比較；包含啟用軌跡卻沒有 callback 取樣的情況。</summary>
        public const string NotAvailable = "n/a";

        /// <summary>基準列的 ConfigChanges 與 VsBaseline：這一列就是基準。</summary>
        public const string Baseline = "baseline";

        #endregion

        #region 比較結果

        /// <summary>VsBaseline：比同一個 seed 的基準好。</summary>
        public const string Win = "win";

        /// <summary>VsBaseline：比同一個 seed 的基準差（本身求解失敗也算輸）。</summary>
        public const string Lose = "lose";

        /// <summary>VsBaseline：跟同一個 seed 的基準一樣（例：兩邊都沒找到解）。</summary>
        public const string Tie = "tie";

        #endregion

        #region 寫出主表

        private static readonly string[] Header =
        {
            "TrialId", "Model", "ModelType", "TrialLabel", "ConfigChanges", "Seed", "VsBaseline",
            "Status", "ObjectiveValue", "BestBound", "Gap", "BuildAndSolveTimeMs", "SolveTimeMs",
            "FirstSolutionMs", "LastBoundChangeMs", "BoundChange",
            "NodeCount", "IterationCount"
        };

        /// <summary>把實驗的每個 trial 各寫成一列，整檔寫到 path（同名舊檔覆寫）。</summary>
        /// <param name="experiment">要寫出的實驗。</param>
        /// <param name="path">輸出檔路徑。</param>
        public void Write(Experiment experiment, string path)
        {
            var rows = new List<string>();

            // 每批（RunId）各自選基準，避免手動合併不同批次時混入跨批的設定差異。
            var baselineOfRun = experiment.Trials
                .GroupBy(t => t.ExperimentId ?? "")
                .ToDictionary(g => g.Key, g => FindBaselineIn(g.ToList()));
            var comparisons = BaselineComparer.CompareAll(experiment.Trials);

            foreach (var t in experiment.Trials)
            {
                var m = t.Metrics;
                var baseline = baselineOfRun[t.ExperimentId ?? ""];
                bool isBaseline = ReferenceEquals(t, baseline);

                var cells = new List<string>
                {
                    t.TrialId > 0 ? t.TrialId.ToString(CultureInfo.InvariantCulture) : None,
                    OrNone(t.Model),
                    m == null ? None : m.ModelType?.ToString() ?? NotAvailable,
                    OrNone(t.Label),
                    isBaseline ? Baseline : OrNone(ConfigChanges(baseline, t)),
                    SeedOf(t),
                    ComparisonText(comparisons[t])
                };

                if (m != null)
                {
                    cells.Add(Cell(m.Status.ToString()));
                    cells.Add(Num(m.ObjectiveValue));
                    cells.Add(Num(m.BestBound));
                    cells.Add(Num(m.Gap));
                    cells.Add(m.BuildAndSolveTimeMs.HasValue ? Num(m.BuildAndSolveTimeMs.Value) : NotAvailable);
                    cells.Add(Num(m.SolveTimeMs));
                    cells.Add(TrajectoryValue(m, m.FirstSolutionMs));
                    cells.Add(TrajectoryValue(m, m.LastBoundChangeMs));
                    cells.Add(TrajectoryValue(m, m.BoundChange));
                    cells.Add(m.NodeCount?.ToString(CultureInfo.InvariantCulture) ?? NotAvailable);
                    cells.Add(m.IterationCount?.ToString(CultureInfo.InvariantCulture) ?? NotAvailable);
                }
                else
                {
                    // 求解前就失敗，沒有任何求解指標
                    while (cells.Count < Header.Length) cells.Add(None);
                }

                rows.Add(string.Join(",", cells));
            }

            ExperimentCsv.Write(path, Header, rows);
        }

        #endregion

        #region 基準與設定差異

        /// <summary>
        /// 取第一筆非暖機且 label 含 baseline 的 trial；沒有時取第一筆非暖機 trial。
        /// </summary>
        internal static Trial FindBaselineIn(IList<Trial> trials)
        {
            if (trials == null || trials.Count == 0) return null;
            var candidates = trials.Where(t => !ConfigSummary.IsWarmup(t)).ToList();
            if (candidates.Count == 0) return trials[0];
            return candidates.FirstOrDefault(t =>
                       (t.Label ?? "").IndexOf("baseline", System.StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? candidates[0];
        }

        /// <summary>
        /// 以分號分隔「參數=值」列出相對基準的差異；Seed 另有欄位，不列入。
        /// </summary>
        internal static string ConfigChanges(Trial baseline, Trial trial)
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
                // 基準有設、這次沒設的參數，寫成「參數=預設」，表示此次恢復求解器預設值。
                parts.Add($"{k}={(v == null ? "預設" : Text(v))}");
            }
            return string.Join(";", parts);
        }

        private static string Text(object v) =>
            v == null ? null : System.Convert.ToString(v, CultureInfo.InvariantCulture);

        #endregion

        #region 儲存格取值與格式

        private static string Setting(Trial t, string key)
        {
            var c = t?.Config;
            if (c == null) return "";
            if (c.Tunable.TryGetValue(key, out var v)) return Cell(v);
            if (c.SolverSpecific.TryGetValue(key, out var v2)) return Cell(v2);
            return "";
        }

        /// <summary>
        /// 數值轉字串，NaN 保留為 "NaN"，與缺值區分，避免統計時誤當成 0。
        /// </summary>
        internal static string Num(double d) =>
            double.IsNaN(d) ? "NaN"
            : double.IsPositiveInfinity(d) ? "Infinity"
            : double.IsNegativeInfinity(d) ? "-Infinity"
            : d.ToString("R", CultureInfo.InvariantCulture);

        // 空字串一律寫 none，不留空白格
        private static string OrNone(string s) => string.IsNullOrEmpty(s) ? None : Cell(s);

        // 未開軌跡為 off；無 callback 取樣為 n/a；有取樣但事件未發生為 none。
        private static string TrajectoryValue(SolveMetrics m, double? v)
        {
            if (!m.TrajectoryEnabled && m.TrajectoryPoints == 0) return Off;
            if (m.TrajectoryPoints == 0) return NotAvailable;
            return v.HasValue ? Num(v.Value) : None;
        }

        // 明設的 Seed 優先；沒設就用 engine 回報的實際種子（求解器預設值）
        internal static string SeedOf(Trial t)
        {
            string configured = Setting(t, "Seed");
            if (configured.Length > 0) return configured;
            return t.Metrics?.Seed?.ToString(CultureInfo.InvariantCulture) ?? NotAvailable;
        }

        internal static string ComparisonText(BaselineComparison result) => result switch
        {
            BaselineComparison.Baseline => Baseline,
            BaselineComparison.Win => Win,
            BaselineComparison.Lose => Lose,
            BaselineComparison.Tie => Tie,
            _ => NotAvailable,
        };

        /// <summary>CSV 欄位值：含逗號、雙引號或換行時加引號並跳脫。</summary>
        internal static string Cell(object v)
        {
            if (v == null) return "";
            string s = v is double d ? Num(d) : System.Convert.ToString(v, CultureInfo.InvariantCulture);
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        #endregion
    }

    /// <summary>
    /// 以 Section / Key / Value 記錄實驗、模型、環境與完整基準設定。
    /// </summary>
    public sealed class MetaCsvWriter
    {
        #region 寫出說明檔

        private static readonly string[] Header = { "Section", "Key", "Value" };

        /// <summary>把實驗的完整說明整檔寫到 path（同名舊檔覆寫）。</summary>
        /// <param name="experiment">要寫出的實驗。</param>
        /// <param name="path">輸出檔路徑。</param>
        public void Write(Experiment experiment, string path)
        {
            var lines = new List<string>();
            var trials = (experiment?.Trials ?? new List<Trial>()).Where(t => t != null).ToList();
            if (trials.Count > 0)
                foreach (var (section, key, value) in RowsOf(experiment, trials))
                    lines.Add(string.Join(",", Cell(section), Cell(key), Cell(string.IsNullOrEmpty(value) ? CsvExperimentWriter.None : value)));

            ExperimentCsv.Write(path, Header, lines);
        }

        private static List<(string Section, string Key, string Value)> RowsOf(Experiment experiment, List<Trial> trials)
        {
            var rows = new List<(string Section, string Key, string Value)>
            {
                ("legend", CsvExperimentWriter.Off, "沒有收集：這次求解沒開收斂軌跡"),
                ("legend", CsvExperimentWriter.None, "有收集但沒發生：沒找到可行解、界從未變動、與基準無差異、求解前就失敗"),
                ("legend", CsvExperimentWriter.NotAvailable, "求解器不提供（例：軌跡開了但 CPLEX 沒呼叫 callback，presolve 或 root 就解完）、框架沒量到（自己呼叫 Trial.Capture 時的 BuildAndSolveTimeMs），或無法跟基準比較（暖機、同一個 seed 沒有基準 trial、基準本身求解失敗）"),
                ("legend", CsvExperimentWriter.Baseline, "這一列就是基準：主表的 ConfigChanges 與 VsBaseline、-summary.csv 的 Wins / Losses / Ties / NotCompared"),
                ("legend", CsvExperimentWriter.Win, "VsBaseline：同一個 seed 比基準好。依序比：有沒有找到解 → 有沒有證明最佳 → 都證明最佳比 SolveTimeMs → 都沒證明比 Gap"),
                ("legend", CsvExperimentWriter.Lose, "VsBaseline：同一個 seed 比基準差（本身求解失敗也算輸）"),
                ("legend", CsvExperimentWriter.Tie, "VsBaseline：同一個 seed 跟基準一樣（例：兩邊都沒找到解）"),
                ("legend", "#N/A", "-trajectory.csv：該時刻還沒有值（例：尚無可行解時的 ObjectiveValue / Gap）"),
                ("experiment", "description", experiment?.Description),
                ("run", "startedAt", trials[0].RunTime.ToString("yyyy-MM-dd HH:mm:ss")),
                ("run", "trialCount", trials.Count.ToString(CultureInfo.InvariantCulture)),
            };

            foreach (var model in trials.Select(t => t.Model).Where(m => !string.IsNullOrEmpty(m)).Distinct().OrderBy(m => m))
            {
                var sample = trials.FirstOrDefault(t => t.Model == model && t.Metrics != null);
                if (sample?.Metrics == null) continue;
                var m = sample.Metrics;
                rows.Add(("model", $"{model}.modelType", m.ModelType?.ToString() ?? CsvExperimentWriter.NotAvailable));
                rows.Add(("model", $"{model}.objectiveSense", m.ObjectiveSense?.ToString() ?? CsvExperimentWriter.None));
                rows.Add(("model", $"{model}.varCount", m.VarCount.ToString(CultureInfo.InvariantCulture)));
                rows.Add(("model", $"{model}.binaryVarCount", Count(m.BinaryVarCount)));
                rows.Add(("model", $"{model}.integerVarCount", Count(m.IntegerVarCount)));
                rows.Add(("model", $"{model}.continuousVarCount", Count(m.ContinuousVarCount)));
                rows.Add(("model", $"{model}.semiContinuousVarCount", Count(m.SemiContinuousVarCount)));
                rows.Add(("model", $"{model}.semiIntegerVarCount", Count(m.SemiIntegerVarCount)));
                rows.Add(("model", $"{model}.constraintCount", m.ConstraintCount.ToString(CultureInfo.InvariantCulture)));
                rows.Add(("model", $"{model}.quadraticConstraintCount", Count(m.QuadraticConstraintCount)));
                rows.Add(("model", $"{model}.indicatorConstraintCount", Count(m.IndicatorConstraintCount)));
                rows.Add(("model", $"{model}.sosCount", Count(m.SosCount)));
                rows.Add(("model", $"{model}.lazyConstraintCount", Count(m.LazyConstraintCount)));
                rows.Add(("model", $"{model}.userCutCount", Count(m.UserCutCount)));
            }

            var baseline = CsvExperimentWriter.FindBaselineIn(trials);
            if (baseline?.Config != null)
            {
                rows.Add(("environment", "solver", baseline.Config.Solver));
                rows.Add(("environment", "machine", Environment.MachineName));

                rows.Add(("baseline", "label", baseline.Label));
                foreach (var kv in baseline.Config.SolverSpecific.OrderBy(k => k.Key))
                    rows.Add(("baseline", kv.Key, Convert.ToString(kv.Value, CultureInfo.InvariantCulture)));
            }

            return rows;
        }

        #endregion

        #region 儲存格格式

        private static string Count(int? n) => n?.ToString(CultureInfo.InvariantCulture) ?? CsvExperimentWriter.NotAvailable;

        private static string Cell(object v)
        {
            if (v == null) return "";
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        #endregion
    }

    /// <summary>
    /// 每批、模型、設定一列彙總，計數取自 Experiment.Summaries；基準列勝負欄填 baseline。
    /// </summary>
    public sealed class SummaryCsvWriter
    {
        #region 寫出彙總

        private static readonly string[] Header =
        {
            "Model", "Config", "IsBaseline", "Trials", "Seeds",
            "Optimal", "Feasible", "NoSolution", "Failed", "FoundSolution",
            "Wins", "Losses", "Ties", "NotCompared"
        };

        /// <summary>把實驗的彙總（每組設定一列）整檔寫到 path（同名舊檔覆寫）。</summary>
        /// <param name="experiment">要寫出的實驗。</param>
        /// <param name="path">輸出檔路徑。</param>
        public void Write(Experiment experiment, string path)
        {
            var rows = new List<string>();
            foreach (var s in experiment.Summaries)
            {
                var cells = new[]
                {
                    Text(s.Model),
                    Text(s.Config),
                    s.IsBaseline ? "true" : "false",
                    s.Trials.ToString(CultureInfo.InvariantCulture),
                    Text(s.Seeds),
                    s.Optimal.ToString(CultureInfo.InvariantCulture),
                    s.Feasible.ToString(CultureInfo.InvariantCulture),
                    s.NoSolution.ToString(CultureInfo.InvariantCulture),
                    s.Failed.ToString(CultureInfo.InvariantCulture),
                    s.FoundSolution.ToString(CultureInfo.InvariantCulture),
                    Versus(s.Wins),
                    Versus(s.Losses),
                    Versus(s.Ties),
                    Versus(s.NotCompared),
                };
                rows.Add(string.Join(",", cells));
            }

            ExperimentCsv.Write(path, Header, rows);
        }

        #endregion

        #region 儲存格取值與格式

        private static string Text(string s) => string.IsNullOrEmpty(s) ? CsvExperimentWriter.None : CsvExperimentWriter.Cell(s);

        // 勝負計數只有基準列是 null：基準不跟自己比
        private static string Versus(int? count) =>
            count?.ToString(CultureInfo.InvariantCulture) ?? CsvExperimentWriter.Baseline;

        #endregion
    }

    /// <summary>
    /// 每個 Trial 軌跡點一列，記錄目標值、最佳界與 gap 隨時間的變化。
    /// </summary>
    public sealed class TrajectoryCsvWriter
    {
        #region 寫出軌跡

        private static readonly string[] Header =
        {
            "TrialId", "TrialLabel", "PointIndex", "ElapsedMs", "ObjectiveValue", "BestBound", "Gap"
        };

        /// <summary>
        /// 每個軌跡點一列，TrialId 對應 -trial.csv；無軌跡 trial 略過，全無軌跡則刪除同名舊檔。
        /// NaN / Infinity 寫為 #N/A，避免圖表將缺值畫成 0。
        /// </summary>
        /// <param name="experiment">要寫出的實驗。</param>
        /// <param name="path">輸出檔路徑。</param>
        public void Write(Experiment experiment, string path)
        {
            var rows = new List<string>();
            foreach (var t in experiment.Trials)
            {
                var pts = t.Metrics?.Convergence;
                if (pts == null) continue;

                int i = 0;
                foreach (var p in pts)
                {
                    var cells = new[]
                    {
                        t.TrialId > 0 ? t.TrialId.ToString(CultureInfo.InvariantCulture) : CsvExperimentWriter.None,
                        Cell(t.Label),
                        i.ToString(CultureInfo.InvariantCulture),
                        Num(p.ElapsedMs),
                        Num(p.ObjectiveValue),
                        Num(p.BestBound),
                        Num(p.Gap)
                    };
                    rows.Add(string.Join(",", cells));
                    i++;
                }
            }

            ExperimentCsv.Write(path, Header, rows);
        }

        #endregion

        #region 儲存格格式

        private static string Num(double v)
            => double.IsNaN(v) || double.IsInfinity(v) ? "#N/A" : v.ToString("R", CultureInfo.InvariantCulture);

        private static string Cell(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        #endregion
    }

    /// <summary>
    /// 實驗 CSV 的寫法：一個實驗一組檔，每次整檔重寫，檔案裡只有這一次的紀錄。
    /// <list type="bullet">
    /// <item>有資料 → UTF-8 BOM + 表頭 + 這次的列，同名舊檔直接覆寫（BOM 讓 zh-TW Excel 正確辨識中文，避免被當成 Big5 讀成亂碼）</item>
    /// <item>這次沒有任何列 → 不建只有表頭的空殼；同名舊檔刪掉，免得留下上一次的紀錄</item>
    /// <item>寫不進去（例：檔案被 Excel 開著）→ 這次改寫到 {檔名}-locked-{時間}.csv，留 WARN，紀錄不會丟</item>
    /// </list>
    /// </summary>
    internal static class ExperimentCsv
    {
        private static readonly Encoding FileEncoding = new UTF8Encoding(true);

        internal static void Write(string path, IReadOnlyList<string> header, IReadOnlyList<string> rows)
        {
            if (rows.Count == 0)
            {
                Delete(path);
                return;
            }

            var body = new StringBuilder();
            body.AppendLine(string.Join(",", header));
            foreach (string row in rows) body.AppendLine(row);
            try
            {
                File.WriteAllText(path, body.ToString(), FileEncoding);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                string fallback = StampedPath(path, "locked");
                Logging.Warn($"[EXPERIMENT_FILE_LOCKED] 實驗檔寫不進去，這次改寫到另一個檔 | value={path} fallback={fallback} reason={ex.GetBaseException().Message} result=written_to_fallback");
                File.WriteAllText(fallback, body.ToString(), FileEncoding);
            }
        }

        /// <summary>刪掉這次沒有資料的同名舊檔；刪不掉（例：被 Excel 開著）就留著並留 WARN。</summary>
        internal static void Delete(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Logging.Warn($"[EXPERIMENT_FILE_LOCKED] 上一次的實驗檔刪不掉，檔裡不是這次的紀錄 | value={path} reason={ex.GetBaseException().Message} result=stale_file_kept");
            }
        }

        // {檔名}-{tag}-{yyyyMMdd-HHmmss}.csv；同一秒已有同名檔時再加 -2、-3
        private static string StampedPath(string path, string tag)
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            string stem = $"{Path.GetFileNameWithoutExtension(path)}-{tag}-{DateTime.Now:yyyyMMdd-HHmmss}";
            string candidate = Path.Combine(dir, stem + ".csv");
            for (int n = 2; File.Exists(candidate); n++)
                candidate = Path.Combine(dir, $"{stem}-{n}.csv");
            return candidate;
        }
    }
}
