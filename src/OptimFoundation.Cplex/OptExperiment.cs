using System;
using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// 把每個模型分別配上每組求解器設定，各求解一次；也可用 <see cref="AddTrial"/> 指定單一模型與設定組合。
    /// 每次求解都建立新的引擎，透過 <see cref="OptEngine.RunModel"/> 執行並留下 <see cref="Trial"/> 紀錄；求解後釋放引擎。
    /// 由 <see cref="OptProject.Experiment"/> 建立；紀錄寫成 Experiment/{FullName}-trial.csv 等四個檔，同名實驗再跑一次整組覆寫。
    /// </summary>
    public sealed class OptExperiment
    {
        private readonly OptProject _project;
        private readonly string _description;
        private readonly List<OptModel> _models = new List<OptModel>();
        private readonly List<(string Label, CplexConfig Config)> _configs =
            new List<(string, CplexConfig)>();
        private readonly List<(OptModel Model, string Label, CplexConfig Config)> _explicitTrials =
            new List<(OptModel, string, CplexConfig)>();
        private ProjectConfig _projectConfig = ProjectConfig.Quiet();
        private bool _captureTrajectory = true;

        internal OptExperiment(OptProject project, string name, string description)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw Logging.ErrorOnce(
                    new ArgumentException("Experiment name is required.", nameof(name)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(OptExperiment), name, "name_is_empty");
            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                throw Logging.ErrorOnce(
                    new ArgumentException($"Experiment name '{name}' contains invalid file name characters.", nameof(name)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(OptExperiment), name, "invalid_file_name_char");
            _project = project;
            Name = name;
            _description = description ?? string.Empty;

            // 建立時切換 log，讓 Run 前的準備作業也記在實驗檔。
            Logging.SetLogFileName(LogName);
        }

        /// <summary>實驗名（建立時給的名稱，不含專案名）。</summary>
        public string Name { get; }

        /// <summary>{專案名}-{實驗名}：log 檔名前綴（{FullName}_exp），也是實驗紀錄的檔名前綴（{FullName}-trial.csv 等）。</summary>
        public string FullName => $"{_project.Name}-{Name}";

        private string LogName => $"{FullName}_exp";

        /// <summary>設定所有實驗共用的輸出選項；每次求解前各複製一份。預設使用 <see cref="ProjectConfig.Quiet"/>。</summary>
        public OptExperiment LoadConfig(ProjectConfig config)
        {
            _projectConfig = config ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(config)),
                "EXPERIMENT_INVALID", "實驗設定不合法", nameof(LoadConfig), Name, "config_is_null");
            return this;
        }

        /// <summary>
        /// 是否記錄目標值、最佳界限與 MIP gap 的變化，預設 true。
        /// callback 影響搜尋路徑與耗時；與正式求解比較時應關閉。
        /// </summary>
        public OptExperiment CaptureTrajectory(bool enabled)
        {
            _captureTrajectory = enabled;
            return this;
        }

        /// <summary>加入一個模型；執行實驗時會分別搭配每組 AddConfig 設定。</summary>
        public OptExperiment AddModel(OptModel model)
        {
            if (model == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(model)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(AddModel), Name, "model_is_null");
            _models.Add(model);
            return this;
        }

        /// <summary>加入一組有名稱的求解器設定；執行時會分別搭配每個 AddModel 模型。</summary>
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

        /// <summary>額外加入一次實驗，明確指定要搭配的模型、設定名稱與設定值。</summary>
        public OptExperiment AddTrial(OptModel model, string label, CplexConfig config)
        {
            if (model == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(model)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(AddTrial), Name, "model_is_null");
            ValidateLabel(label);
            if (config == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(config)),
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(AddTrial), label, "config_is_null");
            _explicitTrials.Add((model, label, config));
            return this;
        }

        /// <summary>執行所有模型與設定組合，儲存並回傳實驗紀錄。</summary>
        public Experiment Run()
        {
            try
            {
                return RunCore();
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "EXPERIMENT_RUN_FAILED", "公開 API 執行失敗", nameof(Run), Name,
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
                    "EXPERIMENT_INVALID", "實驗設定不合法", nameof(Run), Name, "trial_cell_is_missing");

            var finalLabels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cell in cells)
            {
                string finalLabel = $"{cell.Model.Name} | {cell.Label}";
                if (!finalLabels.Add(finalLabel))
                    throw Logging.ErrorOnce(
                        new InvalidOperationException($"Duplicate final experiment trial label '{finalLabel}'."),
                        "EXPERIMENT_INVALID", "實驗設定不合法", nameof(Run), finalLabel, "duplicate_final_trial_label");
            }

            var experiment = new Experiment(_project.Name, Name, _description);

            // 正式求解可能切換 log，執行前須切回實驗 log。
            Logging.SetLogFileName(LogName);
            string projectName = _project.Name;

            // 多模型時加入模型名，避免輸出互相覆寫。
            bool multiModel = cells.Select(c => c.Model.Name).Distinct(StringComparer.Ordinal).Count() > 1;

            Logging.Info(
                $"[Experiment] {FullName} | cells={cells.Count} multiModel={(multiModel ? "ON" : "OFF")} " +
                $"trajectory={(_captureTrajectory ? "ON" : "OFF")}");

            string runId = OptProject.NextRunId();
            int trialId = 0;
            var runTrials = new List<Trial>();

            foreach (var cell in cells)
            {
                string runName = multiModel
                    ? $"{projectName}-{cell.Model.Name}-{cell.Label}"
                    : $"{projectName}-{cell.Label}";
                using var engine = new OptEngine(cell.Config.Clone(), _projectConfig.Clone());
                engine.SetModelName(runName);

                var trial = engine.RunModel(cell.Model, cell.Label, _captureTrajectory, beforeSolve: null, out _);
                trial.ExperimentId = runId;
                trial.TrialId = ++trialId;
                experiment.AddTrial(trial);
                runTrials.Add(trial);
            }

            experiment.Save();
            return experiment;
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
