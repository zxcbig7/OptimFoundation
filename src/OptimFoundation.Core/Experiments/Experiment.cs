using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 一組實驗：同一個問題掃不同設定/模型，每次求解記成一個 <see cref="Trial"/>。
    /// 收集完呼叫 <see cref="Save"/> 輸出 Experiments/&lt;Name&gt;.csv + -meta.csv（有軌跡時再多一個 -trajectory.csv）。
    /// 同名實驗直接覆寫，不讀回也不累積舊檔。
    /// </summary>
    public class Experiment
    {
        /// <summary>實驗名稱，決定輸出檔名 Experiments/&lt;Name&gt;.*。</summary>
        public string Name { get; set; }
        /// <summary>實驗目的描述（自由文字，寫進 -meta.csv 供日後辨識）。</summary>
        public string Description { get; set; }
        /// <summary>實驗建立時間。</summary>
        public DateTime CreatedAt { get; set; }
        /// <summary>本實驗的所有 Trial（每次求解一筆）。</summary>
        public List<Trial> Trials { get; set; }

        /// <summary>建立實驗。name 決定輸出檔名，同名的舊輸出會在 Save 時被覆寫。</summary>
        public Experiment(string name, string description)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw Logging.ErrorOnce(
                    new ArgumentException("Experiment name is required.", nameof(name)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(Experiment), name, "name_is_empty");
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
        /// 把記憶體中的 trials 輸出到 Experiments/&lt;Name&gt;.csv + -meta.csv（+ -trajectory.csv），同名檔直接覆寫。
        /// 本次沒有軌跡時會刪掉同名的舊 -trajectory.csv，避免留下別次的軌跡。
        /// </summary>
        public void Save()
        {
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

            // 主表：一列一 trial，只寫「跟基準差在哪」
            // 說明檔：整批不會變的東西（模型多大、環境、基準的完整設定）只寫一次
            new CsvExperimentWriter().Write(this, FolderDir.Experiment.GetPathFile($"{Name}.csv"));
            new MetaCsvWriter().Write(this, FolderDir.Experiment.GetPathFile($"{Name}-meta.csv"));

            // 只有實際抓到收斂軌跡時才寫 trajectory.csv，避免留下只有表頭的空殼
            string trajectoryPath = FolderDir.Experiment.GetPathFile($"{Name}-trajectory.csv");
            if (Trials.Any(t => (t.Metrics?.Convergence?.Count ?? 0) > 0))
                new TrajectoryCsvWriter().Write(this, trajectoryPath);
            else
                File.Delete(trajectoryPath);

            Logging.Info($"[Experiment] Saved '{Name}' ({Trials.Count} trials) → {FolderDir.Experiment.GetPath()}");
        }
    }

    /// <summary>
    /// 收斂軌跡來源的可擴展 hook。EngineBase 提供「不支援」的預設實作；
    /// 支援的 engine（本期僅 CPLEX）override。未支援者呼叫端不報錯。
    /// </summary>
    public interface ITrajectorySource
    {
        /// <summary>本 engine 是否能記錄收斂軌跡。</summary>
        bool SupportsTrajectory { get; }

        /// <summary>開啟軌跡記錄，MUST 在 Solve() 之前呼叫；不支援的 engine 為 no-op。</summary>
        void EnableTrajectory();

        /// <summary>最近一次求解的軌跡；未開啟或不支援時為空清單。</summary>
        IReadOnlyList<ConvergencePoint> Trajectory { get; }
    }

    /// <summary>
    /// 一次求解的完整記錄：設定 Snapshot + 收斂指標。套件化用法的最小單位。
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

        /// <summary>這次跑的是哪個模型。以前是黏在 Label 前面（"模型名 | 設定名"），現在拆開成獨立欄位。</summary>
        public string Model { get; set; }

        /// <summary>求解記錄的建立時間（Capture 當下）。</summary>
        public DateTime RunTime { get; set; }

        /// <summary>求解前的設定快照，供事後重現這次結果。</summary>
        public ConfigSnapshot Config { get; set; }

        /// <summary>求解結果指標（狀態、目標值、gap、耗時、節點數、收斂軌跡）。</summary>
        public SolveMetrics Metrics { get; set; }

        /// <summary>自由備註，寫進 CSV 供日後辨識。</summary>
        public string Note { get; set; }

        /// <summary>
        /// 套件化單次擷取：讀 engine.Config → 跑 solveAction（一次求解）→ 讀 engine.LastMetrics。
        /// 不接管、不 Dispose engine（生命週期由呼叫端持有）。
        /// </summary>
        /// <param name="engine">已 Build 完成的求解引擎</param>
        /// <param name="label">這次 Trial 的標籤（如 "emphasis=2"）</param>
        /// <param name="solveAction">執行一次求解的動作，回傳是否成功</param>
        /// <param name="note">選填備註</param>
        public static Trial Capture(ISolverEngine engine, string label, Func<bool> solveAction, string note = null)
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
                return CaptureCore(engine, label, solveAction, note);
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "TRIAL_CAPTURE_FAILED", "公開 API 執行失敗", nameof(Capture), label,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        private static Trial CaptureCore(ISolverEngine engine, string label, Func<bool> solveAction, string note)
        {

            var snapshot = ConfigSnapshot.From(engine.Config);

            // 選用：支援軌跡的 engine（本期 CPLEX）在求解前啟用
            if (engine is ITrajectorySource ts && ts.SupportsTrajectory)
            {
                ts.EnableTrajectory();
            }

            solveAction();   // 跑一次求解；非 Optimal（TimeLimit/Feasible）仍照記錄，不視為失敗

            var metrics = engine.LastMetrics ?? new SolveMetrics { Status = engine.Status };

            return new Trial
            {
                Label = label,
                RunTime = DateTime.Now,
                Config = snapshot,
                Metrics = metrics,
                Note = note
            };
            // 不呼叫 engine.Dispose()：engine 生命週期由呼叫端持有
        }
    }
}
