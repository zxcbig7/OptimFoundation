using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 把每筆 Trial 寫成一列 CSV，方便用 Excel 比較設定差異與求解結果。
    ///
    /// 設定差異集中寫在 ConfigChanges 欄，避免新增參數時因缺少專用欄位而漏掉變更。
    /// 只留調參會拿來判讀的欄位；同一批每列都一樣的東西（基準是誰、各類變數 / 限制式數量）寫在 -meta.csv，不每列重抄。
    /// ModelType 取自求解器模型（CPLEX），留在主表是因為它決定這一列怎麼讀（LP 沒有 gap、沒有軌跡）。
    /// 每列只列出與基準不同的參數；基準完整設定寫入同一專案的 -meta.csv（同一個 Experiment + RunId）。
    /// VsBaseline 欄寫這一列跟同一個 seed 的基準比大小的結果（win / lose / tie），比法見 <see cref="ConfigSummary"/>。
    /// 收斂軌跡另外寫 -trajectory.csv。
    /// 每一格都有值：沒有數字可填時寫下方四個標記之一，空白格分不出「沒這個值」和「漏寫」。
    /// 四個檔都是累積檔：每次寫入接在檔尾，每列最前面是 RecordedAt、Experiment、RunId（寫法見 <see cref="CumulativeCsv"/>）。
    /// </summary>
    public sealed class CsvExperimentWriter
    {
        #region 缺值標記

        /// <summary>沒有收集：這次求解沒開收斂軌跡（FirstSolutionMs / LastBoundChangeMs / BoundChange）。</summary>
        public const string Off = "off";

        /// <summary>有收集但事件沒發生：沒找到可行解、界從未變動、與基準無差異、求解前就失敗。</summary>
        public const string None = "none";

        /// <summary>求解器不提供這項資料（NodeCount / IterationCount / Seed / ModelType / -meta.csv 的模型結構；軌跡開了但 CPLEX 沒呼叫 callback 時的 FirstSolutionMs / LastBoundChangeMs / BoundChange），
        /// 框架沒量到（自己呼叫 Trial.Capture 時的 BuildAndSolveTimeMs），或無法跟基準比較（VsBaseline）。</summary>
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

        /// <summary>四個檔共用的前三欄：寫入時間、實驗名、批次。不同實驗、不同次執行的列放在同一個檔，靠這三欄分辨。</summary>
        internal static readonly string[] LeadingHeader = { "RecordedAt", "Experiment", "RunId" };

        private static readonly string[] Header = LeadingHeader.Concat(new[]
        {
            "TrialId", "Model", "ModelType", "TrialLabel", "ConfigChanges", "Seed", "VsBaseline",
            "Status", "ObjectiveValue", "BestBound", "Gap", "BuildAndSolveTimeMs", "SolveTimeMs",
            "FirstSolutionMs", "LastBoundChangeMs", "BoundChange",
            "NodeCount", "IterationCount"
        }).ToArray();

        /// <summary>把實驗的每個 trial 各寫成一列，接在 path 的檔尾（檔案不存在就建新檔）。</summary>
        /// <param name="experiment">要寫出的實驗。</param>
        /// <param name="path">累積檔路徑。</param>
        /// <param name="recordedAt">寫入時間，填在每一列的 RecordedAt 欄；同一次 Save 的四個檔用同一個值。</param>
        public void Write(Experiment experiment, string path, DateTime recordedAt)
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

                var cells = LeadingCells(recordedAt, experiment, t.ExperimentId);
                cells.AddRange(new[]
                {
                    t.TrialId > 0 ? t.TrialId.ToString(CultureInfo.InvariantCulture) : None,
                    OrNone(t.Model),
                    m == null ? None : m.ModelType?.ToString() ?? NotAvailable,
                    OrNone(t.Label),
                    isBaseline ? Baseline : OrNone(ConfigChanges(baseline, t)),
                    SeedOf(t),
                    ComparisonText(comparisons[t])
                });

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

            CumulativeCsv.Append(path, Header, rows);
        }

        /// <summary>每一列最前面的三格：寫入時間、實驗名、批次（RunId）。</summary>
        internal static List<string> LeadingCells(DateTime recordedAt, Experiment experiment, string runId) =>
            new List<string> { Cell(recordedAt.ToString("yyyy-MM-dd HH:mm:ss")), OrNone(experiment?.Name), OrNone(runId) };

        #endregion

        #region 基準與設定差異

        /// <summary>
        /// 找出當作比較基準的那一筆：暖機（label 含 "warmup"）以外、標籤含 "baseline" 的第一筆；都沒有的話用暖機以外的第一筆。
        /// 一輪實驗裡只會有一組基準，所以這個規則夠用，也不必額外設定。
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
        /// 這一列相對基準改了哪些設定，寫成 "參數=值"，多個參數以分號分隔。
        /// Seed 不算改動——它是同一組設定重跑幾次用的，本身另有一欄。
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

        // 軌跡衍生欄：沒開軌跡 → off；開了但 CPLEX 一次都沒呼叫 callback（presolve 或 root 就解完）→ n/a；
        // 有取樣點但事件沒發生（沒找到可行解、界從未變動）→ none
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
    /// 每批（RunId）寫一份說明：開始時間、模型名稱與大小、求解環境及基準完整設定，讓主表只需記錄設定差異。
    /// 欄位為 RecordedAt / Experiment / RunId / Section / Key / Value，新增項目不需改欄位。
    /// 以 Experiment + RunId 篩選，可取得該批完整說明（含 schema 與 legend）。
    /// </summary>
    public sealed class MetaCsvWriter
    {
        #region 格式版本

        /// <summary>
        /// 格式版本；欄位變更時遞增，供讀取端辨識。
        /// v2：新增 modelStats 區段。v3：主表與說明檔不留空白格，缺值改寫標記（見 legend 區段）；主表 Seed 改寫實際使用的種子。
        /// v4：主表 DiffKnobs 改名 ConfigChanges；新增 ModelType 與各類變數 / 限制式數量欄，模型結構一律取自求解器模型；model 區段同步列出。
        /// v5：新增 -summary.csv（每組設定的 sgm / PAR10 / θ / gap 平均 / 找到可行解數與相對基準的改善量）；暖機 trial（label 含 warmup）不當基準。
        /// v6：拿掉 sgm / PAR10 / θ / 改善量，改成逐 seed 跟基準比大小：主表新增 VsBaseline 欄，-summary.csv 改寫 Wins / Losses / Ties / NotCompared。
        /// v7：時間一律取 CPLEX 的時鐘（GetCplexTime），不再自己補軌跡終點；軌跡開了但 CPLEX 沒呼叫 callback 時，TFeasMs / TStallMs / DeltaBound 寫 n/a。
        /// v8：檔案依功能分、不依實驗分：每個專案只有 {專案}-trial.csv（原主表）、-meta.csv、-summary.csv、-trajectory.csv 四個累積檔，每次執行接在檔尾；
        /// 四個檔最前面都加 RecordedAt、Experiment、RunId 三欄（軌跡另加 TrialId）；說明檔每批寫一份，run 區段的 key 不再帶 RunId，拿掉 experiment.name / trialCount / writtenAt。
        /// v9：主表與軌跡只留調參會判讀的欄位。主表拿掉 BasedOn（同批每列一樣，基準是誰看本檔 baseline.label）、RunAt（批次時間看 RunId、順序看 TrialId）、
        /// TrajectoryPoints（有沒有軌跡已由 TFeasMs / TStallMs / DeltaBound 的 off / n/a 表示）、12 個模型結構欄（同一模型每列一樣，看本檔 model 區段）、Note；軌跡拿掉 RunAt。
        /// v10：欄名改成白話全字，對應的屬性同名：MipGap → Gap、RunTimeMs → SolveTimeMs、TFeasMs → FirstSolutionMs、TStallMs → LastBoundChangeMs、DeltaBound → BoundChange；
        /// 主表新增 BuildAndSolveTimeMs（建模 + 求解）；彙總 NoIncumbent → NoSolution、FoundIncumbent → FoundSolution、OtherStatus → Failed；
        /// 軌跡 Label → TrialLabel、TimeMs → ElapsedMs、Objective → ObjectiveValue、Bound → BestBound。
        /// v11：拿掉 modelStats 區段（框架自己記的數量與 CPLEX 重複）；目標式方向改列在 model 區段的 objectiveSense（取自 CPLEX）。
        /// </summary>
        public const int SchemaVersion = 11;

        private static readonly string[] Header = CsvExperimentWriter.LeadingHeader.Concat(new[] { "Section", "Key", "Value" }).ToArray();

        #endregion

        #region 寫出說明檔

        /// <summary>每一批（RunId）寫一份完整說明，接在 path 的檔尾（檔案不存在就建新檔）。</summary>
        /// <param name="experiment">要寫出的實驗。</param>
        /// <param name="path">累積檔路徑。</param>
        /// <param name="recordedAt">寫入時間，填在每一列的 RecordedAt 欄；同一次 Save 的四個檔用同一個值。</param>
        public void Write(Experiment experiment, string path, DateTime recordedAt)
        {
            var lines = new List<string>();
            var trials = (experiment?.Trials ?? new List<Trial>()).Where(t => t != null);
            foreach (var run in trials.GroupBy(t => t.ExperimentId ?? ""))
                foreach (var (section, key, value) in RowsOf(experiment, run.ToList()))
                {
                    var cells = CsvExperimentWriter.LeadingCells(recordedAt, experiment, run.Key);
                    cells.Add(Cell(section));
                    cells.Add(Cell(key));
                    cells.Add(Cell(string.IsNullOrEmpty(value) ? CsvExperimentWriter.None : value));
                    lines.Add(string.Join(",", cells));
                }

            CumulativeCsv.Append(path, Header, lines);
        }

        // 一批的說明：schema、legend、實驗描述、這批的開始時間與 trial 數、模型、環境、基準完整設定
        private static List<(string Section, string Key, string Value)> RowsOf(Experiment experiment, List<Trial> trials)
        {
            var rows = new List<(string Section, string Key, string Value)>
            {
                ("schema", "version", SchemaVersion.ToString(CultureInfo.InvariantCulture)),
                ("legend", CsvExperimentWriter.Off, "沒有收集：這次求解沒開收斂軌跡"),
                ("legend", CsvExperimentWriter.None, "有收集但沒發生：沒找到可行解、界從未變動、與基準無差異、求解前就失敗"),
                ("legend", CsvExperimentWriter.NotAvailable, "求解器不提供（例：軌跡開了但 CPLEX 沒呼叫 callback，presolve 或 root 就解完）、框架沒量到（自己呼叫 Trial.Capture 時的 BuildAndSolveTimeMs），或無法跟基準比較（暖機、同一個 seed 沒有基準 trial、基準本身求解失敗）"),
                ("legend", CsvExperimentWriter.Baseline, "這一列就是基準：主表的 ConfigChanges 與 VsBaseline、-summary.csv 的 Wins / Losses / Ties / NotCompared"),
                ("legend", CsvExperimentWriter.Win, "VsBaseline：同一個 seed 比基準好。依序比：有沒有找到解 → 有沒有證明最佳 → 都證明最佳比 SolveTimeMs → 都沒證明比 Gap"),
                ("legend", CsvExperimentWriter.Lose, "VsBaseline：同一個 seed 比基準差（本身求解失敗也算輸）"),
                ("legend", CsvExperimentWriter.Tie, "VsBaseline：同一個 seed 跟基準一樣（例：兩邊都沒找到解）"),
                ("legend", "#N/A", "-trajectory.csv：該時刻還沒有值（例：尚無可行解時的 ObjectiveValue / Gap）"),
                ("experiment", "description", experiment?.Description),
                // 這批第一個 trial 的記錄時間與 trial 數（OptExperiment 一次 Run 只有一批；手動組的 Experiment 可能多批，每批各一份）
                ("run", "startedAt", trials[0].RunTime.ToString("yyyy-MM-dd HH:mm:ss")),
                ("run", "trialCount", trials.Count.ToString(CultureInfo.InvariantCulture)),
            };

            // 模型：跑了哪些、類型與各類變數 / 限制式數量（取自求解器模型）；主表只留 ModelType，數量只寫在這裡
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
                // 列出基準中非 null 的設定；未列出的參數使用求解器預設值。
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
    /// 彙總表：每組設定（同一批、同一模型、同一設定、不同 seed）一列，計數取自 <see cref="Experiment.Summaries"/>，比法見 <see cref="ConfigSummary"/>。
    /// 每一格都有值：基準列的 Wins / Losses / Ties / NotCompared 寫 baseline。
    /// </summary>
    public sealed class SummaryCsvWriter
    {
        #region 寫出彙總

        private static readonly string[] Header = CsvExperimentWriter.LeadingHeader.Concat(new[]
        {
            "Model", "Config", "IsBaseline", "Trials", "Seeds",
            "Optimal", "Feasible", "NoSolution", "Failed", "FoundSolution",
            "Wins", "Losses", "Ties", "NotCompared"
        }).ToArray();

        /// <summary>把實驗的彙總（每組設定一列）接在 path 的檔尾（檔案不存在就建新檔）。</summary>
        /// <param name="experiment">要寫出的實驗。</param>
        /// <param name="path">累積檔路徑。</param>
        /// <param name="recordedAt">寫入時間，填在每一列的 RecordedAt 欄；同一次 Save 的四個檔用同一個值。</param>
        public void Write(Experiment experiment, string path, DateTime recordedAt)
        {
            var rows = new List<string>();
            foreach (var s in experiment.Summaries)
            {
                var cells = CsvExperimentWriter.LeadingCells(recordedAt, experiment, s.RunId);
                cells.AddRange(new[]
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
                });
                rows.Add(string.Join(",", cells));
            }

            CumulativeCsv.Append(path, Header, rows);
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
    /// 把每筆 Trial 的每個取樣點各寫成一列 CSV，用來畫出
    /// 最佳可行解目標值、最佳界與 gap 隨時間的變化。搭配 <see cref="CsvExperimentWriter"/>
    /// 輸出的每次求解摘要，可同時查看最終結果與中途進展。
    /// </summary>
    public sealed class TrajectoryCsvWriter
    {
        #region 寫出軌跡

        private static readonly string[] Header = CsvExperimentWriter.LeadingHeader.Concat(new[]
        {
            "TrialId", "TrialLabel", "PointIndex", "ElapsedMs", "ObjectiveValue", "BestBound", "Gap"
        }).ToArray();

        /// <summary>
        /// 每個取樣點寫一列，接在 path 的檔尾；Experiment + RunId + TrialId 對回 -trial.csv 的那一列。
        /// 沒有軌跡的 Trial 直接跳過，整批都沒有軌跡點就不動檔案；NaN / Infinity（例：還沒有可行解時的 ObjectiveValue / Gap）寫成 #N/A：
        /// Excel 讀成 #N/A 錯誤值、畫圖時自動略過該點，pandas 預設也讀成 NaN，不會像文字標記那樣被當成 0 畫出去。
        /// </summary>
        /// <param name="experiment">要寫出的實驗。</param>
        /// <param name="path">累積檔路徑。</param>
        /// <param name="recordedAt">寫入時間，填在每一列的 RecordedAt 欄；同一次 Save 的四個檔用同一個值。</param>
        public void Write(Experiment experiment, string path, DateTime recordedAt)
        {
            var rows = new List<string>();
            foreach (var t in experiment.Trials)
            {
                var pts = t.Metrics?.Convergence;
                if (pts == null) continue;

                int i = 0;
                foreach (var p in pts)
                {
                    var cells = CsvExperimentWriter.LeadingCells(recordedAt, experiment, t.ExperimentId);
                    cells.AddRange(new[]
                    {
                        t.TrialId > 0 ? t.TrialId.ToString(CultureInfo.InvariantCulture) : CsvExperimentWriter.None,
                        Cell(t.Label),
                        i.ToString(CultureInfo.InvariantCulture),
                        Num(p.ElapsedMs),
                        Num(p.ObjectiveValue),
                        Num(p.BestBound),
                        Num(p.Gap)
                    });
                    rows.Add(string.Join(",", cells));
                    i++;
                }
            }

            CumulativeCsv.Append(path, Header, rows);
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
    /// 實驗累積檔的寫法：每次寫入接在檔尾，舊列不改不刪。
    /// <list type="bullet">
    /// <item>檔案不存在（或是空檔）→ 建新檔：UTF-8 BOM + 表頭 + 這次的列（BOM 讓 zh-TW Excel 正確辨識中文，避免被當成 Big5 讀成亂碼）</item>
    /// <item>表頭跟這一版相同 → 接在檔尾，不再寫 BOM</item>
    /// <item>表頭不同（欄位改版）→ 舊檔改名成 {檔名}-old-{時間}.csv 保留，另開新檔，留 WARN</item>
    /// <item>寫不進去（例：檔案被 Excel 開著）→ 這次改寫到 {檔名}-locked-{時間}.csv，留 WARN，紀錄不會丟</item>
    /// </list>
    /// 這次沒有任何列就不動檔案，不建只有表頭的空殼。
    /// </summary>
    internal static class CumulativeCsv
    {
        private static readonly Encoding NewFileEncoding = new UTF8Encoding(true);
        private static readonly Encoding AppendEncoding = new UTF8Encoding(false);

        internal static void Append(string path, IReadOnlyList<string> header, IReadOnlyList<string> rows)
        {
            if (rows.Count == 0) return;
            string headerLine = string.Join(",", header);
            try
            {
                AppendCore(path, headerLine, rows);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                string fallback = StampedPath(path, "locked");
                Logging.Warn($"[EXPERIMENT_FILE_LOCKED] 累積檔寫不進去，這次改寫到另一個檔 | value={path} fallback={fallback} reason={ex.GetBaseException().Message} result=written_to_fallback");
                AppendCore(fallback, headerLine, rows);
            }
        }

        private static void AppendCore(string path, string headerLine, IReadOnlyList<string> rows)
        {
            var body = new StringBuilder();
            foreach (string row in rows) body.AppendLine(row);

            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                if (FirstLineOf(path) == headerLine)
                {
                    // 檔尾沒有換行（例：手動編輯過）時先補一個，免得這次的第一列黏在舊的最後一列後面
                    string separator = EndsWithNewLine(path) ? "" : Environment.NewLine;
                    File.AppendAllText(path, separator + body, AppendEncoding);
                    return;
                }

                string renamed = StampedPath(path, "old");
                File.Move(path, renamed);
                Logging.Warn($"[EXPERIMENT_HEADER_CHANGED] 累積檔的欄位跟這一版不同，舊檔改名保留、另開新檔 | value={path} renamed={renamed} result=new_file_started");
            }

            File.WriteAllText(path, headerLine + Environment.NewLine + body, NewFileEncoding);
        }

        // 讀的時候允許別人開著（例：Excel），真的寫不進去才交給 Append 改寫到 -locked 檔
        private static string FirstLineOf(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadLine();
        }

        private static bool EndsWithNewLine(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(-1, SeekOrigin.End);
            return stream.ReadByte() == '\n';
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
