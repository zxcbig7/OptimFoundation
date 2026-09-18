using System;
using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>Runs a model/configuration cross-product plus optional explicit cells.</summary>
    public sealed class OptExperiment
    {
        private readonly string _name;
        private readonly string _description;
        private readonly List<OptModel> _models = new List<OptModel>();
        private readonly List<(string Label, CplexConfig Config)> _configs =
            new List<(string, CplexConfig)>();
        private readonly List<(OptModel Model, string Label, CplexConfig Config)> _explicitTrials =
            new List<(OptModel, string, CplexConfig)>();
        private Func<ProjectConfig> _projectConfigFactory = () => new ProjectConfig
        {
            RetentionDays = 0,
            EnableSolverLog = false,
            ExportLP = false,
            ExportMPS = false,
            ExportSol = false,
        };

        /// <summary>Creates an experiment.</summary>
        public OptExperiment(string name, string description)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw Logging.ErrorOnce(
                    new ArgumentException("Experiment name is required.", nameof(name)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(OptExperiment), name, "name_is_empty");
            _name = name;
            _description = description ?? string.Empty;
        }

        /// <summary>Uses a fresh project configuration for each experiment cell.</summary>
        public OptExperiment UseConfig(Func<ProjectConfig> configFactory)
        {
            _projectConfigFactory = configFactory ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(configFactory)),
                "EXPERIMENT_INVALID", "實驗設定不合法", nameof(UseConfig), _name, "config_factory_is_null");
            return this;
        }

        /// <summary>Adds a model to the cross-product.</summary>
        public OptExperiment AddModel(OptModel model)
        {
            if (model == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(model)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(AddModel), _name, "model_is_null");
            _models.Add(model);
            return this;
        }

        /// <summary>Adds a labelled solver configuration to the cross-product.</summary>
        public OptExperiment AddConfig(string label, CplexConfig config)
        {
            ValidateLabel(label);
            if (config == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(config)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(AddConfig), label, "config_is_null");
            if (_configs.Any(c => string.Equals(c.Label, label, StringComparison.Ordinal)))
                throw Logging.ErrorOnce(
                    new ArgumentException($"Duplicate experiment configuration label '{label}'.", nameof(label)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(AddConfig), label, "duplicate_config_label");
            _configs.Add((label, config));
            return this;
        }

        /// <summary>Adds exactly one explicit model/configuration cell.</summary>
        public OptExperiment AddTrial(OptModel model, string label, CplexConfig config)
        {
            if (model == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(model)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(AddTrial), _name, "model_is_null");
            ValidateLabel(label);
            if (config == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(config)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(AddTrial), label, "config_is_null");
            _explicitTrials.Add((model, label, config));
            return this;
        }

        /// <summary>Runs all cells and saves the resulting experiment.</summary>
        public Experiment Run()
        {
            try
            {
                return RunCore();
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "EXPERIMENT_RUN_FAILED", "公開 API 執行失敗", nameof(Run), _name,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        private Experiment RunCore()
        {
            var cells = new List<(OptModel Model, string Label, CplexConfig Config)>();
            foreach (var model in _models)
                foreach (var config in _configs)
                    cells.Add((model, config.Label, config.Config));
            cells.AddRange(_explicitTrials);

            if (cells.Count == 0)
                throw Logging.ErrorOnce(
                    new InvalidOperationException("An experiment requires at least one model/configuration cell."),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(Run), _name, "trial_cell_is_missing");

            var finalLabels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cell in cells)
            {
                string finalLabel = $"{cell.Model.Name} | {cell.Label}";
                if (!finalLabels.Add(finalLabel))
                    throw Logging.ErrorOnce(
                        new InvalidOperationException($"Duplicate final experiment trial label '{finalLabel}'."),
                        "EXPERIMENT_INVALID", "實驗設定不合法", nameof(Run), finalLabel, "duplicate_final_trial_label");
            }

            var experiment = new Experiment(_name, _description);

            // 專案名是所有輸出檔名的根，與 OptProject 同一套來源優先序；實驗這一側再接上參數名。
            string projectName = ResolveProjectName(out string projectNameSource);
            Logging.SetLogFileName($"{projectName}_exp");

            // 單一模型時檔名就是「專案名-參數名」；多模型才插模型名，否則各模型的輸出檔會互相覆蓋。
            bool multiModel = cells.Select(c => c.Model.Name).Distinct(StringComparer.Ordinal).Count() > 1;

            Logging.Info(
                $"[Experiment] {_name} | ProjectName={projectName}({projectNameSource}) " +
                $"cells={cells.Count} multiModel={(multiModel ? "ON" : "OFF")}");

            // 這批實驗的識別：用開始時間。同一個實驗跑很多次時，靠它分辨哪些列是同一批。
            string runId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            int trialId = 0;

            foreach (var cell in cells)
            {
                var projectConfig = _projectConfigFactory() ?? new ProjectConfig();
                using var engine = new OptEngine(cell.Config.Clone(), projectConfig);
                engine.SetModelName(multiModel
                    ? $"{projectName}-{cell.Model.Name}-{cell.Label}"
                    : $"{projectName}-{cell.Label}");
                engine.Build();
                cell.Model.ApplyTo(engine);

                // Label 只放設定名稱；模型名放到 Trial.Model，不再黏成一個字串
                var trial = Trial.Capture(engine, cell.Label, () => engine.Solve());
                trial.RunId = runId;
                trial.TrialId = ++trialId;
                trial.Model = cell.Model.Name;
                experiment.AddTrial(trial);
            }

            experiment.Save();
            return experiment;
        }

        /// <summary>
        /// 解析輸出檔名的根。與 OptProject 一致：ProjectConfig.ProjectName 優先，沒設就退回實驗名。
        /// 名稱必須在跑任何 cell 之前決定（log 檔要先接上），故在這裡多叫一次工廠——
        /// 工廠本來就該每次回傳全新實例，多叫無副作用。
        /// </summary>
        private string ResolveProjectName(out string source)
        {
            string configured = (_projectConfigFactory() ?? new ProjectConfig()).ProjectName;
            if (string.IsNullOrWhiteSpace(configured))
            {
                source = "experiment";
                return _name;
            }

            source = "cfg";
            return configured;
        }

        private static void ValidateLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                throw Logging.ErrorOnce(
                    new ArgumentException("Experiment configuration label is required.", nameof(label)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(ValidateLabel), label, "label_is_empty");
        }
    }
}
