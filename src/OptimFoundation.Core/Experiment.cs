using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OptimFoundation.Core
{
    public class Experiment
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<Trial> Trials { get; set; }

        public Experiment(string name, string description)
        {
            Name = name;
            Description = description;
            CreatedAt = DateTime.Now;
            Trials = new List<Trial>();
        }

        /// <summary>JSON 反序列化用。</summary>
        public Experiment() { Trials = new List<Trial>(); }

        public void AddTrial(Trial trial)
        {
            Trials.Add(trial);
        }

        /// <summary>
        /// 輸出到 experiments/&lt;Name&gt;.csv + .json。同名實驗為 append：
        /// 先 Load 既有 JSON、把不在本次記憶體中的 trials 併到前面（以 RunAt+Label 去重，重複 Save 同物件不會重覆）。
        /// </summary>
        public void Save()
        {
            FolderDir.Experiment.CreateFolder();

            var current = Trials ?? new List<Trial>();
            string jsonPath = FolderDir.Experiment.GetFilePath($"{Name}.json");
            var onDisk = new JsonExperimentWriter().Read(jsonPath)?.Trials ?? new List<Trial>();

            var merged = onDisk
                .Where(d => !current.Any(c => c.RunAt == d.RunAt && c.Label == d.Label))
                .ToList();
            merged.AddRange(current);
            Trials = merged;

            new CsvExperimentWriter().Write(this, FolderDir.Experiment.GetFilePath($"{Name}.csv"));
            new JsonExperimentWriter().Write(this, jsonPath);

            Logging.Info($"[Experiment] Saved '{Name}' ({Trials.Count} trials) → {FolderDir.Experiment.GetPath()}");
        }

        /// <summary>讀回既有實驗（以 JSON 為權威來源），供累積。檔案不存在回 null。</summary>
        public static Experiment Load(string name)
            => new JsonExperimentWriter().Read(FolderDir.Experiment.GetFilePath($"{name}.json"));
    }
}