using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 收集同一問題在不同模型或設定下的求解結果，每次求解記成一筆 <see cref="Trial"/>。
    /// 收集完呼叫 <see cref="Save"/>，把紀錄接在 FolderDir.Experiment 下這個專案的四個累積檔尾端：
    /// {Project}-trial.csv（一列一 trial）、-meta.csv（說明）、-summary.csv（每組設定一列）、-trajectory.csv（一列一個軌跡點，有才寫）。
    /// 檔案依功能分、不依實驗或批次分：每一列最前面是 RecordedAt（寫入時間）、Experiment（實驗名）、RunId（批次），靠這三欄分辨；舊列不改不刪。
    /// </summary>
    public class Experiment
    {
        /// <summary>專案名，決定寫進哪一組累積檔：FolderDir.Experiment 下的 {Project}-trial.csv 等。</summary>
        public string Project { get; set; }
        /// <summary>實驗名，寫在每一列的 Experiment 欄，用來分辨同一專案的不同實驗（例：tuning-r1；正式求解是 solve）。</summary>
        public string Name { get; set; }
        /// <summary>實驗目的描述（自由文字，寫進 -meta.csv 供日後辨識）。</summary>
        public string Description { get; set; }
        /// <summary>實驗建立時間。</summary>
        public DateTime CreatedAt { get; set; }
        /// <summary>本實驗的所有 Trial（每次求解一筆）。</summary>
        public List<Trial> Trials { get; set; }

        /// <summary>Save 時是否寫 -summary.csv，預設 true。正式求解紀錄只有一筆、沒有可比的設定，OptProject 會設成 false。</summary>
        public bool WriteSummary { get; set; } = true;

        /// <summary>
        /// 彙總同一模型與設定在不同 seed 下的結果：各狀態、找到解及與基準比較的勝負次數。
        /// 每次讀取都從 <see cref="Trials"/> 重新計算；比法見 <see cref="ConfigSummary"/>。
        /// </summary>
        public IReadOnlyList<ConfigSummary> Summaries => ConfigSummary.From(Trials);

        /// <summary>建立實驗；建立時不求解也不寫檔。</summary>
        /// <param name="project">專案名，決定寫進哪一組累積檔（{project}-trial.csv 等）；不可空白或含非法檔名字元。</param>
        /// <param name="name">實驗名，寫在每一列的 Experiment 欄；同名實驗再跑一次會多一批 RunId，不覆寫舊列。</param>
        /// <param name="description">實驗目的，寫進 -meta.csv。</param>
        public Experiment(string project, string name, string description)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw Logging.ErrorOnce(
                    new ArgumentException("Experiment project is required.", nameof(project)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(Experiment), project, "project_is_empty");
            if (project.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw Logging.ErrorOnce(
                    new ArgumentException($"Experiment project '{project}' contains invalid file name characters.", nameof(project)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(Experiment), project, "invalid_file_name_char");
            if (string.IsNullOrWhiteSpace(name))
                throw Logging.ErrorOnce(
                    new ArgumentException("Experiment name is required.", nameof(name)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(Experiment), name, "name_is_empty");
            Project = project;
            Name = name;
            Description = description;
            CreatedAt = DateTime.Now;
            Trials = new List<Trial>();
        }

        /// <summary>把一次求解的紀錄（Trial）加入本實驗。</summary>
        public void AddTrial(Trial trial)
        {
            if (trial == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(trial)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(AddTrial), Name, "trial_is_null");
            Trials.Add(trial);
        }

        /// <summary>
        /// 把記憶體中的 Trial 接在 FolderDir.Experiment 下這個專案的累積檔尾端：{Project}-trial.csv、-meta.csv、-summary.csv（<see cref="WriteSummary"/> 為 true 時），
        /// 有軌跡點時再寫 -trajectory.csv。舊列不改不刪；同一個物件 Save 兩次會寫兩次。
        /// 檔案表頭跟這一版不同時，舊檔改名成 -old-&lt;時間&gt; 保留、另開新檔；寫不進去（例：檔案被 Excel 開著）時改寫到 -locked-&lt;時間&gt; 檔。兩者都留 WARN。
        /// </summary>
        public void Save()
        {
            if (string.IsNullOrWhiteSpace(Project))
                throw Logging.ErrorOnce(
                    new InvalidOperationException("Experiment project is required before Save."),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(Save), Name, "project_is_empty");
            if (string.IsNullOrWhiteSpace(Name))
                throw Logging.ErrorOnce(
                    new InvalidOperationException("Experiment name is required before Save."),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(Save), Name, "name_is_empty");
            try
            {
                SaveCore();
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "EXPERIMENT_SAVE_FAILED", "公開 API 執行失敗", nameof(Save), Name,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        private void SaveCore()
        {
            FolderDir.Experiment.CreateFolder();
            Trials ??= new List<Trial>();
            if (Trials.Count == 0)
            {
                Logging.Warn($"[EXPERIMENT_EMPTY] 實驗沒有任何 trial，不寫入 | value={Project}/{Name} result=skipped");
                return;
            }

            // 同一次 Save 的四個檔用同一個寫入時間，才對得起來
            var recordedAt = DateTime.Now;

            // 主表：一列一 trial，只寫「跟基準差在哪」
            // 說明檔：每批不會變的東西（模型多大、環境、基準的完整設定）每批寫一份
            new CsvExperimentWriter().Write(this, PathOf("trial"), recordedAt);
            new MetaCsvWriter().Write(this, PathOf("meta"), recordedAt);
            // 彙總：每組設定一列，列出各 seed 與基準比較的結果。
            if (WriteSummary)
                new SummaryCsvWriter().Write(this, PathOf("summary"), recordedAt);
            // 軌跡：沒有任何軌跡點就不寫，也不建只有表頭的空殼
            new TrajectoryCsvWriter().Write(this, PathOf("trajectory"), recordedAt);

            Logging.Info($"[Experiment] Saved '{Name}' ({Trials.Count} trials) → {PathOf("trial")}");
        }

        private string PathOf(string kind) => FolderDir.Experiment.GetPathFile($"{Project}-{kind}.csv");
    }

    /// <summary>
    /// 供引擎提供求解過程紀錄的介面。EngineBase 預設不支援，
    /// 支援的引擎（如 CPLEX）會覆寫相關方法。不支援的引擎不記錄，也不拋例外。
    /// </summary>
    public interface ITrajectorySource
    {
        /// <summary>本 engine 是否能記錄收斂軌跡。</summary>
        bool SupportsTrajectory { get; }

        /// <summary>開啟求解過程記錄，必須在 Solve() 之前呼叫；不支援的引擎不做任何事。</summary>
        void EnableTrajectory();

        /// <summary>最近一次求解的軌跡；未開啟或不支援時為空清單。</summary>
        IReadOnlyList<ConvergencePoint> Trajectory { get; }
    }

    /// <summary>
    /// 一次求解的完整紀錄，包含使用的設定、求解結果與過程中的收斂指標。
    /// </summary>
    public sealed class Trial
    {
        /// <summary>這次求解的標籤，格式:r*-description，例 "r1-GomoryCuts=2"。</summary>
        public string Label { get; set; }

        /// <summary>這批實驗的識別，值是該次執行的開始時間（yyyyMMdd-HHmmss）。
        /// 同一個實驗跑很多次時，靠它分辨哪些列是同一批。</summary>
        public string ExperimentId { get; set; }

        /// <summary>同一批實驗內的流水號，從 1 開始。與 <see cref="ExperimentId"/> 合起來唯一。</summary>
        public int TrialId { get; set; }

        /// <summary>這次求解的模型名稱，與設定標籤 Label 分開保存。</summary>
        public string Model { get; set; }

        /// <summary>求解記錄的建立時間（Capture 當下）。</summary>
        public DateTime RunTime { get; set; }

        /// <summary>求解前的設定快照，供事後重現這次結果。</summary>
        public ConfigSnapshot Config { get; set; }

        /// <summary>求解結果指標（狀態、目標值、gap、耗時、節點數、收斂軌跡）。</summary>
        public SolveMetrics Metrics { get; set; }

        /// <summary>
        /// 記錄一次求解：先複製 engine.Config，再執行 solveAction，最後讀取 engine.LastMetrics。
        /// 引擎仍由呼叫端管理；此方法不會呼叫 Dispose。
        /// </summary>
        /// <param name="engine">已 Build 完成的求解引擎</param>
        /// <param name="label">這次 Trial 的標籤（如 "emphasis=2"）</param>
        /// <param name="solveAction">執行一次求解的動作，回傳是否成功</param>
        /// <param name="captureTrajectory">
        /// 是否在求解前開啟過程記錄（引擎需支援）。記錄用的 callback 可能影響搜尋順序與耗時，
        /// 正式求解，以及用保留資料確認新設定成效、決定是否採用時，應關閉此功能。
        /// </param>
        public static Trial Capture(ISolverEngine engine, string label, Func<bool> solveAction, bool captureTrajectory = true)
        {
            if (engine == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(engine)),
                    "TRIAL_CAPTURE_INVALID", "實驗紀錄擷取失敗", nameof(Capture), label, "engine_is_null");
            if (solveAction == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(solveAction)),
                    "TRIAL_CAPTURE_INVALID", "實驗紀錄擷取失敗", nameof(Capture), label, "solve_action_is_null");

            try
            {
                return CaptureCore(engine, label, solveAction, captureTrajectory);
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "TRIAL_CAPTURE_FAILED", "公開 API 執行失敗", nameof(Capture), label,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        private static Trial CaptureCore(ISolverEngine engine, string label, Func<bool> solveAction, bool captureTrajectory)
        {

            var snapshot = ConfigSnapshot.From(engine.Config);

            // 使用者要求記錄，且引擎支援此功能時，在求解前啟用。
            if (captureTrajectory && engine is ITrajectorySource ts && ts.SupportsTrajectory)
            {
                ts.EnableTrajectory();
            }

            solveAction();   // 執行一次求解；不論回傳 true 或 false 都繼續記錄結果，拋出的例外則向外傳遞。

            var metrics = engine.LastMetrics ?? new SolveMetrics { Status = engine.Status };

            return new Trial
            {
                Label = label,
                RunTime = DateTime.Now,
                Config = snapshot,
                Metrics = metrics
            };
            // 不呼叫 engine.Dispose()：engine 生命週期由呼叫端持有
        }
    }

    /// <summary>
    /// 一組設定的彙總：同一批（RunId）、同一模型、同一設定（Trial label 去掉結尾 -s&lt;seed&gt;）跑不同 seed 的結果，寫成 -summary.csv 一列。
    /// 統計各狀態、找到解及逐 seed 與基準比較的勝負次數（規則見 <see cref="BaselineComparer"/>），不計算其他統計指標。
    /// 要不要換成這組設定由使用端的規則決定，例如 AI-Modeling tuning 規範：一個 seed 都不能輸，而且至少贏 3 個。
    /// label 含 "warmup" 的 trial 是暖機，不計入也不當基準。
    /// </summary>
    public sealed class ConfigSummary
    {
        /// <summary>批次識別，同主表的 RunId。</summary>
        public string RunId { get; set; }

        /// <summary>模型名。</summary>
        public string Model { get; set; }

        /// <summary>設定名：Trial label 去掉結尾的 -s&lt;seed&gt;。</summary>
        public string Config { get; set; }

        /// <summary>是否為本批的基準設定（-meta.csv baseline.label 那組）。</summary>
        public bool IsBaseline { get; set; }

        /// <summary>計入的 trial 數（不含暖機）。</summary>
        public int Trials { get; set; }

        /// <summary>本組用到的 seed，以空白分隔。</summary>
        public string Seeds { get; set; }

        /// <summary>Status = Optimal 的 trial 數。</summary>
        public int Optimal { get; set; }

        /// <summary>Status = Feasible 的 trial 數：有可行解，但停止時尚未證明最佳。</summary>
        public int Feasible { get; set; }

        /// <summary>Status = TimeLimit 的 trial 數：超過時限且未找到可行解。</summary>
        public int NoSolution { get; set; }

        /// <summary>求解失敗的 trial 數（Infeasible / Unbounded / Error / NotSolved）。</summary>
        public int Failed { get; set; }

        /// <summary>找到解的 trial 數（Optimal + Feasible）。</summary>
        public int FoundSolution { get; set; }

        /// <summary>比同一個 seed 的基準好的 trial 數；基準列為 null。</summary>
        public int? Wins { get; set; }

        /// <summary>比同一個 seed 的基準差的 trial 數（本身求解失敗也算）；基準列為 null。</summary>
        public int? Losses { get; set; }

        /// <summary>跟同一個 seed 的基準一樣的 trial 數；基準列為 null。</summary>
        public int? Ties { get; set; }

        /// <summary>無法比較的 trial 數（同一個 seed 沒有基準 trial，或基準本身求解失敗）；基準列為 null。</summary>
        public int? NotCompared { get; set; }

        private static readonly Regex SeedSuffix = new Regex(@"-s\d+$");

        /// <summary>label 含 "warmup"（不分大小寫）的 trial 是暖機：不計入，也不當基準。</summary>
        internal static bool IsWarmup(Trial trial) =>
            (trial?.Label ?? "").IndexOf("warmup", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>設定名：label 去掉結尾的 -s&lt;seed&gt;。</summary>
        internal static string ConfigOf(Trial trial) => SeedSuffix.Replace(trial?.Label ?? "", "");

        /// <summary>依批次、模型、設定分組計數；每個模型的基準列排第一，其餘照 trial 出現順序。</summary>
        internal static List<ConfigSummary> From(IEnumerable<Trial> trials)
        {
            var all = (trials ?? Enumerable.Empty<Trial>()).Where(t => t != null).ToList();
            var comparisons = BaselineComparer.CompareAll(all);
            var result = new List<ConfigSummary>();

            foreach (var run in all.GroupBy(t => t.ExperimentId ?? ""))
            {
                var summaries = run
                    .Where(t => !IsWarmup(t))
                    .GroupBy(t => (Model: t.Model ?? "", Config: ConfigOf(t)))
                    .Select(g => Summarize(run.Key, g.Key.Model, g.Key.Config, g.ToList(), comparisons))
                    .ToList();
                result.AddRange(summaries.GroupBy(s => s.Model).SelectMany(g => g.OrderByDescending(s => s.IsBaseline)));
            }
            return result;
        }

        private static ConfigSummary Summarize(string runId, string model, string config, List<Trial> group,
            Dictionary<Trial, BaselineComparison> comparisons)
        {
            var results = group.Select(t => comparisons[t]).ToList();
            bool isBaseline = results.Contains(BaselineComparison.Baseline);
            int? CountOf(BaselineComparison kind) => isBaseline ? (int?)null : results.Count(r => r == kind);

            return new ConfigSummary
            {
                RunId = runId,
                Model = model,
                Config = config,
                IsBaseline = isBaseline,
                Trials = group.Count,
                Seeds = string.Join(" ", group.Select(CsvExperimentWriter.SeedOf).Distinct()),
                Optimal = group.Count(t => StatusOf(t) == SolveStatus.Optimal),
                Feasible = group.Count(t => StatusOf(t) == SolveStatus.Feasible),
                NoSolution = group.Count(t => StatusOf(t) == SolveStatus.TimeLimit),
                Failed = group.Count(t => !IsMeasurable(t)),
                FoundSolution = group.Count(HasSolution),
                Wins = CountOf(BaselineComparison.Win),
                Losses = CountOf(BaselineComparison.Lose),
                Ties = CountOf(BaselineComparison.Tie),
                NotCompared = CountOf(BaselineComparison.NotCompared),
            };
        }

        internal static SolveStatus StatusOf(Trial trial) => trial?.Metrics?.Status ?? SolveStatus.NotSolved;

        internal static bool IsMeasurable(Trial trial) =>
            StatusOf(trial) is SolveStatus.Optimal or SolveStatus.Feasible or SolveStatus.TimeLimit;

        internal static bool HasSolution(Trial trial) =>
            (StatusOf(trial) is SolveStatus.Optimal or SolveStatus.Feasible) && !double.IsNaN(trial.Metrics.ObjectiveValue);
    }

    /// <summary>一筆 trial 跟基準比大小的結果，寫在主表的 VsBaseline 欄。</summary>
    internal enum BaselineComparison
    {
        /// <summary>這一筆屬於基準設定。</summary>
        Baseline,

        /// <summary>比同一個 seed 的基準好。</summary>
        Win,

        /// <summary>比同一個 seed 的基準差；本身求解失敗（Infeasible / Unbounded / Error / NotSolved）也算輸。</summary>
        Lose,

        /// <summary>跟同一個 seed 的基準一樣，例如兩邊都沒找到解。</summary>
        Tie,

        /// <summary>無法比較：暖機、同一個 seed 沒有基準 trial，或基準本身求解失敗。</summary>
        NotCompared
    }

    /// <summary>
    /// 逐 seed 跟基準比大小，不算任何統計指標。對手是同一批、同一模型、同一個 seed 的基準 trial，依序比：
    /// <list type="number">
    /// <item>有找到解的贏過沒找到解的</item>
    /// <item>都有解：證明最佳（Optimal）的贏過沒證明的</item>
    /// <item>都證明最佳：時間短的贏</item>
    /// <item>都沒證明：gap 小的贏</item>
    /// <item>都沒找到解：平手</item>
    /// </list>
    /// 基準設定 = 第一個 label 含 "baseline" 的非暖機 trial 的設定（label 去掉結尾 -s&lt;seed&gt;）。
    /// </summary>
    internal static class BaselineComparer
    {
        /// <summary>算出每筆 trial 的比較結果。</summary>
        internal static Dictionary<Trial, BaselineComparison> CompareAll(IEnumerable<Trial> trials)
        {
            var result = new Dictionary<Trial, BaselineComparison>();
            if (trials == null) return result;

            foreach (var run in trials.Where(t => t != null).GroupBy(t => t.ExperimentId ?? ""))
            {
                var runTrials = run.ToList();
                var baselineTrial = CsvExperimentWriter.FindBaselineIn(runTrials);
                string baselineConfig = baselineTrial == null || ConfigSummary.IsWarmup(baselineTrial)
                    ? null
                    : ConfigSummary.ConfigOf(baselineTrial);
                var baselines = runTrials
                    .Where(t => !ConfigSummary.IsWarmup(t) && ConfigSummary.ConfigOf(t) == baselineConfig)
                    .ToList();

                foreach (var trial in runTrials)
                {
                    if (baselineConfig == null || ConfigSummary.IsWarmup(trial))
                        result[trial] = BaselineComparison.NotCompared;
                    else if (ConfigSummary.ConfigOf(trial) == baselineConfig)
                        result[trial] = BaselineComparison.Baseline;
                    else
                        result[trial] = Compare(trial, OpponentOf(trial, baselines));
                }
            }
            return result;
        }

        /// <summary>trial 跟對手（同一個 seed 的基準 trial）比大小。</summary>
        internal static BaselineComparison Compare(Trial trial, Trial opponent)
        {
            if (opponent == null || !ConfigSummary.IsMeasurable(opponent)) return BaselineComparison.NotCompared;
            if (!ConfigSummary.IsMeasurable(trial)) return BaselineComparison.Lose;

            bool found = ConfigSummary.HasSolution(trial);
            if (found != ConfigSummary.HasSolution(opponent)) return found ? BaselineComparison.Win : BaselineComparison.Lose;
            if (!found) return BaselineComparison.Tie;

            bool optimal = ConfigSummary.StatusOf(trial) == SolveStatus.Optimal;
            if (optimal != (ConfigSummary.StatusOf(opponent) == SolveStatus.Optimal))
                return optimal ? BaselineComparison.Win : BaselineComparison.Lose;
            return optimal
                ? Smaller(trial.Metrics.SolveTimeMs, opponent.Metrics.SolveTimeMs)
                : Smaller(trial.Metrics.Gap, opponent.Metrics.Gap);
        }

        // 同一模型、同一個 seed 的基準 trial；seed 不明時不配對
        private static Trial OpponentOf(Trial trial, List<Trial> baselines)
        {
            string seed = CsvExperimentWriter.SeedOf(trial);
            if (seed == CsvExperimentWriter.NotAvailable) return null;
            return baselines.FirstOrDefault(b => b.Model == trial.Model && CsvExperimentWriter.SeedOf(b) == seed);
        }

        private static BaselineComparison Smaller(double value, double opponent)
        {
            if (double.IsNaN(value) || double.IsNaN(opponent)) return BaselineComparison.NotCompared;
            if (value < opponent) return BaselineComparison.Win;
            return value > opponent ? BaselineComparison.Lose : BaselineComparison.Tie;
        }
    }
}
