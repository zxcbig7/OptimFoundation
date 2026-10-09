using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 收集多組模型與設定的 Trial；Save 寫出同名覆寫的實驗 CSV，詳見該方法。
    /// </summary>
    public class Experiment
    {
        /// <summary>專案名，輸出檔名的前段：FolderDir.Experiment 下的 {Project}-{Name}-trial.csv 等。</summary>
        public string Project { get; set; }
        /// <summary>實驗名，輸出檔名的後段（例：tuning-r1；正式環境是 production）。</summary>
        public string Name { get; set; }
        /// <summary>實驗目的描述（自由文字，寫進 -meta.csv 供日後辨識）。</summary>
        public string Description { get; set; }
        /// <summary>實驗建立時間。</summary>
        public DateTime CreatedAt { get; set; }
        /// <summary>本實驗的所有 Trial（每次求解一筆）。</summary>
        public List<Trial> Trials { get; set; }

        /// <summary>Save 時是否寫 -summary.csv，預設 true。正式環境紀錄只有一筆、沒有可比的設定，OptProject 會設成 false。</summary>
        public bool WriteSummary { get; set; } = true;

        /// <summary>
        /// 依模型與設定彙總各 seed 的狀態及勝負；每次讀取重新計算。
        /// </summary>
        public IReadOnlyList<ConfigSummary> Summaries => ConfigSummary.From(Trials);

        /// <summary>建立實驗；建立時不求解也不寫檔。</summary>
        /// <param name="project">專案名，輸出檔名的前段（{project}-{name}-trial.csv 等）；不可空白或含非法檔名字元。</param>
        /// <param name="name">實驗名，輸出檔名的後段；不可空白或含非法檔名字元。同名實驗再跑一次整組覆寫。</param>
        /// <param name="description">實驗目的，寫進 -meta.csv。</param>
        public Experiment(string project, string name, string description)
        {
            if (string.IsNullOrWhiteSpace(project))
                throw Logging.ErrorOnce(
                    new ArgumentException("實驗的 project 不得為空", nameof(project)),
                    "實驗設定不合法", null, nameof(Experiment), project, "專案名稱為空");
            if (project.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw Logging.ErrorOnce(
                    new ArgumentException($"實驗的 project 含不合法的檔名字元：{project}", nameof(project)),
                    "實驗設定不合法", null, nameof(Experiment), project, "檔名含不合法字元");
            if (string.IsNullOrWhiteSpace(name))
                throw Logging.ErrorOnce(
                    new ArgumentException("實驗的 name 不得為空", nameof(name)),
                    "實驗設定不合法", null, nameof(Experiment), name, "名稱為空");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw Logging.ErrorOnce(
                    new ArgumentException($"實驗的 name 含不合法的檔名字元：{name}", nameof(name)),
                    "實驗設定不合法", null, nameof(Experiment), name, "檔名含不合法字元");
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
                    new ArgumentNullException(nameof(trial), "trial 不得為 null"),
                    "實驗設定不合法", null, nameof(AddTrial), Name, "試跑為空");
            Trials.Add(trial);
        }

        /// <summary>
        /// 寫出 FolderDir.Experiment 下的 {Project}-{Name}-trial.csv、-meta.csv，依設定加寫 -summary.csv，有軌跡才寫 -trajectory.csv。
        /// 同名整組覆寫，未再產出的舊檔刪除；覆寫或改寫 -locked-&lt;時間&gt; 備援檔時記警告。
        /// </summary>
        public void Save()
        {
            if (string.IsNullOrWhiteSpace(Project))
                throw Logging.ErrorOnce(
                    new InvalidOperationException("寫出前實驗的 project 不得為空"),
                    "實驗設定不合法", null, nameof(Save), Name, "專案名稱為空");
            if (string.IsNullOrWhiteSpace(Name))
                throw Logging.ErrorOnce(
                    new InvalidOperationException("寫出前實驗的 name 不得為空"),
                    "實驗設定不合法", null, nameof(Save), Name, "名稱為空");
            try
            {
                SaveCore();
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "實驗紀錄寫出失敗", null, nameof(Save), Name,
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
                Logging.Warn($"[實驗為空] 沒有任何試跑，不寫出 | 名稱={Project}/{Name} 結果=略過");
                return;
            }

            var existing = FileKinds.Select(PathOf).Where(File.Exists).Select(Path.GetFileName).ToList();
            if (existing.Count > 0)
                Logging.Warn($"[實驗紀錄覆寫] 同名實驗已有紀錄 | 名稱={Project}-{Name} 檔案={string.Join("|", existing)} 結果=覆寫");

            new CsvExperimentWriter().Write(this, PathOf("trial"));
            new MetaCsvWriter().Write(this, PathOf("meta"));
            if (WriteSummary)
                new SummaryCsvWriter().Write(this, PathOf("summary"));
            else
                ExperimentCsv.Delete(PathOf("summary"));
            new TrajectoryCsvWriter().Write(this, PathOf("trajectory"));

            Logging.Info($"[實驗紀錄寫出完成] 名稱={Name} 數量={Trials.Count} 路徑={PathOf("trial")}");
        }

        private static readonly string[] FileKinds = { "trial", "meta", "summary", "trajectory" };

        private string PathOf(string kind) => FolderDir.Experiment.GetPathFile($"{Project}-{Name}-{kind}.csv");
    }

    /// <summary>
    /// 求解軌跡介面；不支援的引擎預設不記錄、不拋例外。
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
        /// 同一個 Experiment 裡的 trial 依它分批選基準、跟基準比較；不寫進 CSV。</summary>
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
        /// 複製 Config、執行 solveAction 並讀取 LastMetrics；引擎由呼叫端釋放。
        /// </summary>
        /// <param name="engine">已 Build 完成的求解引擎</param>
        /// <param name="label">這次 Trial 的標籤（如 "emphasis=2"）</param>
        /// <param name="solveAction">執行一次求解的動作，回傳是否成功</param>
        /// <param name="captureTrajectory">
        /// 求解前啟用軌跡（須引擎支援）；callback 可能影響搜尋與耗時，正式環境及設定採用驗證應關閉。
        /// </param>
        public static Trial Capture(ISolverEngine engine, string label, Func<bool> solveAction, bool captureTrajectory = true)
        {
            if (engine == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(engine), "engine 不得為 null"),
                    "試跑擷取不合法", null, nameof(Capture), label, "引擎為空");
            if (solveAction == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(solveAction), "solveAction 不得為 null"),
                    "試跑擷取不合法", null, nameof(Capture), label, "求解動作為空");

            try
            {
                return CaptureCore(engine, label, solveAction, captureTrajectory);
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "試跑擷取失敗", null, nameof(Capture), label,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        private static Trial CaptureCore(ISolverEngine engine, string label, Func<bool> solveAction, bool captureTrajectory)
        {

            var snapshot = ConfigSnapshot.From(engine.SolverConfig);

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
        }
    }

    /// <summary>
    /// 依批次、模型及設定彙總各 seed 的狀態與勝負（見 <see cref="BaselineComparer"/>）。
    /// label 含 warmup 的 trial 不計入，也不作基準；是否採用設定由呼叫端決定。
    /// </summary>
    public sealed class ConfigSummary
    {
        /// <summary>批次識別，同 <see cref="Trial.ExperimentId"/>；不寫進 CSV。</summary>
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
    /// 與同批、同模型、同 seed 的基準比較，依序判斷：
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
