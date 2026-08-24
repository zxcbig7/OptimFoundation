using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 一組實驗：同一個問題掃不同設定/模型，每次求解記成一個 <see cref="Trial"/>。
    /// 收集完呼叫 <see cref="Save"/> 輸出 experiments/&lt;Name&gt;.csv + .json（有軌跡時再多一個 -trajectory.csv）。
    /// 同名實驗為累積（append），不覆寫歷史。
    /// </summary>
    public class Experiment
    {
        /// <summary>實驗名稱，決定輸出檔名 experiments/&lt;Name&gt;.*。</summary>
        public string Name { get; set; }
        /// <summary>實驗目的描述（自由文字，寫進 JSON 供日後辨識）。</summary>
        public string Description { get; set; }
        /// <summary>實驗建立時間。</summary>
        public DateTime CreatedAt { get; set; }
        /// <summary>本實驗累積的所有 Trial（每次求解一筆）。</summary>
        public List<Trial> Trials { get; set; }

        /// <summary>建立實驗。name 決定輸出檔名，同名等於接續同一份歷史（Save 會 append）。</summary>
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

        /// <summary>JSON 反序列化用。</summary>
        public Experiment() { Trials = new List<Trial>(); }

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
        /// 輸出到 experiments/&lt;Name&gt;.csv + .json。同名實驗為 append：
        /// 先 Load 既有 JSON、把不在本次記憶體中的 trials 併到前面（以 RunAt+Label 去重，重複 Save 同物件不會重覆）。
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

            // 1) 讀回同名實驗磁碟上既有的 trials（以 JSON 為權威來源；不存在則空清單）
            var current = Trials ?? new List<Trial>();
            string jsonPath = FolderDir.Experiment.GetFilePath($"{Name}.json");
            var onDisk = new JsonExperimentWriter().Read(jsonPath)?.Trials ?? new List<Trial>();

            // 2) 合併：保留磁碟上「本次記憶體沒有」的舊 trials，本次的接在後面
            //    去重的鍵是 RunAt + Model + Label —— Label 現在只放設定名稱，
            //    所以要把 Model 一起算進去，否則兩個模型用同一個設定名會被誤判成同一筆。
            var merged = onDisk
                .Where(d => !current.Any(c => c.RunAt == d.RunAt
                                           && c.Label == d.Label
                                           && c.Model == d.Model))
                .ToList();
            merged.AddRange(current);
            Trials = merged;

            // 3) 輸出三份給人看的檔 + 一份給程式讀的 json
            //    主表：一列一 trial，只寫「跟基準差在哪」
            //    說明檔：整批不會變的東西（模型多大、環境、基準的完整設定）只寫一次
            //    json：保留當累積與重讀的權威來源，設定已改成只記有設的那些
            new CsvExperimentWriter().Write(this, FolderDir.Experiment.GetFilePath($"{Name}.csv"));
            new MetaCsvWriter().Write(this, FolderDir.Experiment.GetFilePath($"{Name}-meta.csv"));
            new JsonExperimentWriter().Write(this, jsonPath);

            // 4) 只有實際抓到收斂軌跡時才多出 trajectory.csv，避免留下只有表頭的空殼（與 csv/json 永遠有料一致）
            if (Trials.Any(t => (t.Metrics?.Convergence?.Count ?? 0) > 0))
                new TrajectoryCsvWriter().Write(this, FolderDir.Experiment.GetFilePath($"{Name}-trajectory.csv"));

            Logging.Info($"[Experiment] Saved '{Name}' ({Trials.Count} trials) → {FolderDir.Experiment.GetPath()}");
        }

        /// <summary>讀回既有實驗（以 JSON 為權威來源），供累積。檔案不存在回 null。</summary>
        public static Experiment Load(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw Logging.ErrorOnce(
                    new ArgumentException("Experiment name is required.", nameof(name)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(Load), name, "name_is_empty");
            try
            {
                return new JsonExperimentWriter().Read(FolderDir.Experiment.GetFilePath($"{name}.json"));
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "EXPERIMENT_LOAD_FAILED", "公開 API 執行失敗", nameof(Load), name,
                    ex.GetBaseException().Message);
                throw;
            }
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
}
