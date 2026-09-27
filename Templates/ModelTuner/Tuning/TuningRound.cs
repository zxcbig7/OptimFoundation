using System.Text.RegularExpressions;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace ModelTuner
{
    /// <summary>
    /// 一輪 tuning experiment：一組具名 CplexConfig，第一顆必須是 <c>r&lt;N&gt;-baseline</c>。
    /// runner 自己把 seeds × instances 展開成 cell，並落實 solver-tuning-guide §3.4 的降噪要求：
    /// seed 是共同因子（寫成 label 的 <c>-s&lt;seed&gt;</c> 後綴，不是 variant）、第一個 solve 當 warm-up 不計入、
    /// variant 執行順序跨 seed 輪替，避免固定讓某一顆承擔 cold-start。
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

        /// <summary>加入一顆 config。label 帶 <c>r&lt;N&gt;-</c> 前綴、不帶 seed 後綴（runner 會加）。</summary>
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

        /// <summary>全部 config × tuning seeds × Instances/tune。</summary>
        public int Run() =>
            Execute(_workspace.ExperimentName(Round), _description, _configs, _tuningSeeds, _workspace.Tune);

        /// <summary>
        /// §4.6 hold-out：只跑 baseline 與 champion，seed 換成 holdout seeds；有 Instances/holdout 就改用它（train / test 分離）。
        /// holdout 只能估計、NEVER 用來選 config——所以這裡只接受一個已經選好的 champion。
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
                _workspace.ExperimentName(Round, holdout: true),
                $"R{Round} hold-out：{championLabel} vs baseline",
                new List<(string, CplexConfig)> { _configs[0], champion },
                _holdoutSeeds,
                instances);
        }

        private int Execute(
            string experimentName,
            string description,
            IReadOnlyList<(string Label, CplexConfig Config)> configs,
            int[] seeds,
            IReadOnlyList<TuningInstance> instances)
        {
            if (configs.Count == 0 || instances.Count == 0)
            {
                Logging.Error($"[ROUND_DEFINITION_INVALID] round 沒有 config 或沒有 instance | value={experimentName} configs={configs.Count} instances={instances.Count} result=aborted");
                return 2;
            }
            if (!_workspace.PrepareRun(experimentName)) return 3;

            // 跟 OptExperiment 用同一個 log 名，warm-up 與正式 cell 收在同一個檔，dynamic search 檢查才掃得到全部
            Logging.SetLogFileName($"{experimentName}_exp");
            Warmup(experimentName, configs[0].Config, seeds[0], instances[0]);

            var models = instances.Select(i => OptModel.FromFile(i.FullPath, i.Name)).ToList();
            var experiment = new OptExperiment(experimentName, description);
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

            _workspace.Archive(experimentName);
            return RoundFacts.Report(_workspace, experimentName);
        }

        // cold-start（JIT、DLL 載入、OS cache）固定懲罰第一個 solve；先跑一次同規格的 cell 丟掉
        private static void Warmup(string experimentName, CplexConfig config, int seed, TuningInstance instance)
        {
            var cell = config.Clone();
            cell.Seed = seed;
            var quiet = new ProjectConfig { EnableSolverLog = false, ExportLP = false, ExportMPS = false, ExportSol = false };

            using var engine = new OptEngine(cell, quiet);
            engine.SetModelName($"{experimentName}-warmup");
            engine.Build();
            engine.ImportModel(instance.FullPath);
            engine.Solve();
            Logging.Info($"[Warmup] {instance.Name} seed={seed} status={engine.Status} result=excluded_from_experiment");
        }
    }
}
