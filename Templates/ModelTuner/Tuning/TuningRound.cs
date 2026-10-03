using System.Text.RegularExpressions;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace ModelTuner
{
    /// <summary>
    /// 執行一輪參數比較。每組 CplexConfig 都要命名，第一組必須是基準設定 <c>r&lt;N&gt;-baseline</c>。
    /// 每組設定會用所有亂數種子求解所有模型檔，並依 solver-tuning-guide §3.4 降低量測誤差：
    /// 各組設定使用相同的 seed 清單，label 自動加上 <c>-s&lt;seed&gt;</c>；先求解一次暖機，這次結果不列入比較；
    /// 換 seed 時輪替各組設定的執行順序，避免同一組總是最先執行而受到啟動耗時影響。
    /// </summary>
    public sealed class TuningRound
    {
        private static readonly Regex SeedSuffix = new Regex(@"-s\d+$");

        private readonly TunerWorkspace _workspace;
        private readonly string _description;
        private readonly int[] _tuningSeeds;
        private readonly int[] _holdoutSeeds;
        private readonly List<(string Label, CplexConfig Config)> _configs = new List<(string, CplexConfig)>();

        public TuningRound(TunerWorkspace workspace, int round, string description, int[] tuningSeeds, int[] holdoutSeeds)
        {
            if (tuningSeeds.Length == 0 || holdoutSeeds.Length == 0 || tuningSeeds.Intersect(holdoutSeeds).Any())
                throw Logging.ErrorOnce(
                    new ArgumentException("Tuning and holdout seeds must be non-empty and disjoint."),
                    "ROUND_DEFINITION_INVALID", "round 定義不合法", nameof(TuningRound), $"r{round}", "seed_sets_invalid");

            _workspace = workspace;
            Round = round;
            _description = description;
            _tuningSeeds = tuningSeeds;
            _holdoutSeeds = holdoutSeeds;
        }

        public int Round { get; }

        public string BaselineLabel => $"r{Round}-baseline";

        /// <summary>加入一組參數設定。label 必須以 <c>r&lt;N&gt;-</c> 開頭；seed 後綴由執行流程自動加上。</summary>
        public TuningRound Add(string label, CplexConfig config)
        {
            string reason =
                config == null ? "config_is_null"
                : string.IsNullOrWhiteSpace(label) ? "label_is_empty"
                : !label.StartsWith($"r{Round}-", StringComparison.Ordinal) ? "label_missing_round_prefix"
                : SeedSuffix.IsMatch(label) ? "label_has_seed_suffix"
                : label.Contains('|') ? "label_contains_pipe"
                : _configs.Any(c => c.Label == label) ? "label_duplicated"
                : _configs.Count == 0 && label != BaselineLabel ? "first_config_must_be_baseline"
                : "";
            if (reason != "")
                throw Logging.ErrorOnce(
                    new ArgumentException($"Invalid round config '{label}': {reason}.", nameof(label)),
                    "ROUND_DEFINITION_INVALID", "round 定義不合法", nameof(Add), label, reason);

            _configs.Add((label, config!.Clone()));
            return this;
        }

        /// <summary>以每組參數設定和每個調參用 seed，逐一求解 Instances/tune 的所有模型。</summary>
        public int Run() =>
            Execute(holdout: false, _description, _configs, _tuningSeeds, _workspace.Tune);

        /// <summary>
        /// 依 §4.6 用保留的 seed 比較基準設定和已選出的最佳設定；若有 Instances/holdout 模型，也改用這批未參與調參的模型。
        /// 這一步只驗證已選設定的表現，不再挑選參數，因此只接受一個已選好的設定名稱。
        /// </summary>
        public int RunHoldout(string championLabel)
        {
            var champion = _configs.FirstOrDefault(c => c.Label == championLabel);
            if (champion.Config == null || championLabel == BaselineLabel)
            {
                Logging.Error($"[HOLDOUT_CHAMPION_INVALID] champion 必須是 R{Round} 區塊裡 baseline 以外的 label | value={championLabel} candidates={string.Join("|", _configs.Skip(1).Select(c => c.Label))} result=aborted");
                return 2;
            }

            var instances = _workspace.Holdout.Count > 0 ? _workspace.Holdout : _workspace.Tune;
            Logging.Info($"[Holdout] instances={(_workspace.Holdout.Count > 0 ? "Instances/holdout" : "Instances/tune（無 holdout instance）")} seeds={string.Join(",", _holdoutSeeds)}");

            return Execute(
                holdout: true,
                $"R{Round} hold-out：{championLabel} vs baseline",
                new List<(string, CplexConfig)> { _configs[0], champion },
                _holdoutSeeds,
                instances);
        }

        private int Execute(
            bool holdout,
            string description,
            IReadOnlyList<(string Label, CplexConfig Config)> configs,
            int[] seeds,
            IReadOnlyList<TuningInstance> instances)
        {
            string experimentName = _workspace.ExperimentName(Round, holdout);
            string shortName = TunerWorkspace.ExperimentShortName(Round, holdout);
            if (configs.Count == 0 || instances.Count == 0)
            {
                Logging.Error($"[ROUND_DEFINITION_INVALID] round 沒有 config 或沒有 instance | value={experimentName} configs={configs.Count} instances={instances.Count} result=aborted");
                return 2;
            }
            if (!_workspace.PrepareRun(shortName)) return 3;

            // 先建立實驗以切換 log 檔，讓暖機和正式求解的紀錄都在同一檔，後續才能完整檢查 dynamic search 是否啟用。
            var experiment = _workspace.Project.Experiment(shortName, description);
            Warmup(experimentName, configs[0].Config, seeds[0], instances[0]);

            var models = instances.Select(i => OptModel.ReadModel(i.FullPath, i.Name)).ToList();
            for (int k = 0; k < seeds.Length; k++)
                foreach (var model in models)
                    for (int j = 0; j < configs.Count; j++)
                    {
                        var (label, config) = configs[(j + k) % configs.Count];
                        var cell = config.Clone();
                        cell.Seed = seeds[k];
                        experiment.AddTrial(model, $"{label}-s{seeds[k]}", cell);
                    }

            Logging.Info($"[Round] {experimentName} configs={configs.Count} seeds={seeds.Length} instances={instances.Count} cells={configs.Count * seeds.Length * instances.Count}");
            experiment.Run();

            _workspace.Archive(shortName);
            return RoundFacts.Report(_workspace, experimentName);
        }

        // 第一次求解包含 JIT 編譯、DLL 載入與快取準備的耗時；先用同樣設定求解一次暖機，結果不列入實驗。
        private static void Warmup(string experimentName, CplexConfig config, int seed, TuningInstance instance)
        {
            var cell = config.Clone();
            cell.Seed = seed;
            using var engine = new OptEngine(cell, ProjectConfig.Quiet());
            engine.SetModelName($"{experimentName}-warmup");
            engine.Build();
            engine.ReadModel(instance.FullPath);
            engine.Solve();
            Logging.Info($"[Warmup] {instance.Name} seed={seed} status={engine.Status} result=excluded_from_experiment");
        }
    }
}
