using System;
using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// 把每個模型分別配上每組求解器設定，各求解一次；也可用 <see cref="AddTrial"/> 指定單一模型與設定組合。
    /// 每次求解都建立新的引擎，透過 <see cref="OptEngine.RunModel"/> 執行並留下 <see cref="Trial"/> 紀錄。
    /// 由 <see cref="OptProject.Production"/>（正式環境）或 <see cref="OptProject.Experiment"/>（實驗）建立，寫法相同，只差預設值：
    /// 正式環境只能一組模型 × 一組設定（多了在 Run 時丟例外、不執行），保留 engine、不開收斂軌跡、不寫 -summary.csv；
    /// 實驗可以一對一或多對多，每組跑完就釋放 engine、開收斂軌跡、寫四個檔。
    /// 紀錄寫成 Experiment/{FullName}-trial.csv 等，同名再跑一次整組覆寫。
    /// </summary>
    public sealed class OptExperiment
    {
        private readonly OptProject _project;
        private readonly string _description;
        private readonly bool _isProduction;
        private readonly List<OptModel> _models = new List<OptModel>();
        private readonly List<(string Label, CplexConfig Config)> _configs =
            new List<(string, CplexConfig)>();
        private readonly List<(OptModel Model, string Label, CplexConfig Config)> _explicitTrials =
            new List<(OptModel, string, CplexConfig)>();
        private ProjectConfig _projectConfig;
        private bool _projectConfigAdded;
        private bool _captureTrajectory;
        private Action<OptEngine> _beforeSolve;
        private Action<OptEngine> _onSolved;

        internal OptExperiment(OptProject project, string name, string description, bool isProduction)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw Logging.ErrorOnce(
                    new ArgumentException("實驗名稱不得為空白", nameof(name)),
                    "實驗設定不合法", null, nameof(OptExperiment), name, "名稱為空");
            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                throw Logging.ErrorOnce(
                    new ArgumentException($"實驗名稱含不合法的檔名字元：{name}", nameof(name)),
                    "實驗設定不合法", null, nameof(OptExperiment), name, "檔名含不合法字元");
            _project = project;
            Name = name;
            _description = description ?? string.Empty;
            _isProduction = isProduction;
            _projectConfig = isProduction ? new ProjectConfig() : ProjectConfig.Quiet();
            _captureTrajectory = !isProduction;

            // 建立時切換 log，讓 Run 前的準備作業也記在同一檔。
            Logging.SetLogFileName(LogName);
        }

        /// <summary>實驗名（建立時給的名稱，不含專案名）；正式環境固定為 <see cref="OptProject.ProductionExperimentName"/>。</summary>
        public string Name { get; }

        /// <summary>{專案名}-{實驗名}：實驗紀錄的檔名前綴（{FullName}-trial.csv 等），實驗的 log 檔名前綴為 {FullName}_exp。</summary>
        public string FullName => $"{_project.Name}-{Name}";

        // 正式環境寫專案 log，實驗寫自己的 _exp log。
        private string LogName => _isProduction ? _project.Name : $"{FullName}_exp";

        /// <summary>
        /// 加入專案設定（solver log、LP / MPS / Sol 匯出），所有組共用，每次求解前各複製一份。
        /// 只有一份：再加一次會覆蓋前一份並 WARN。正式環境預設 <c>new ProjectConfig()</c>，實驗預設 <see cref="ProjectConfig.Quiet"/>。
        /// </summary>
        public OptExperiment AddProjectConfig(ProjectConfig config)
        {
            if (config == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(config), "config 不得為 null"),
                    "實驗設定不合法", null, nameof(AddProjectConfig), Name, "設定為空");
            if (_projectConfigAdded)
                Logging.Warn($"[專案設定重複加入] 名稱={FullName} 原因=專案設定只有一份 結果=以後加入的為準");
            _projectConfig = config;
            _projectConfigAdded = true;
            return this;
        }

        /// <summary>
        /// 是否記錄目標值、最佳界限與 MIP gap 的變化；正式環境預設 false，實驗預設 true。
        /// callback 影響搜尋路徑與耗時；與正式環境比較時應關閉。
        /// </summary>
        public OptExperiment CaptureTrajectory(bool enabled)
        {
            _captureTrajectory = enabled;
            return this;
        }

        /// <summary>每組建模完成後、求解前執行；null 表示不執行。</summary>
        public OptExperiment BeforeSolve(Action<OptEngine> handler)
        {
            _beforeSolve = handler;
            return this;
        }

        /// <summary>每組找到可用解（Optimal 或 Feasible）後執行，通常用來讀解、驗證、寫出；null 表示不執行。</summary>
        public OptExperiment OnSolved(Action<OptEngine> handler)
        {
            _onSolved = handler;
            return this;
        }

        /// <summary>加入一個模型；執行時會分別搭配每組 AddSolverConfig 設定。</summary>
        public OptExperiment AddModel(OptModel model)
        {
            if (model == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(model), "model 不得為 null"),
                    "實驗設定不合法", null, nameof(AddModel), Name, "模型為空");
            _models.Add(model);
            return this;
        }

        /// <summary>加入一組有名稱的求解器設定；執行時會分別搭配每個 AddModel 模型。</summary>
        public OptExperiment AddSolverConfig(string label, CplexConfig config)
        {
            ValidateLabel(label);
            if (config == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(config), "config 不得為 null"),
                    "實驗設定不合法", null, nameof(AddSolverConfig), label, "設定為空");
            if (_configs.Any(c => string.Equals(c.Label, label, StringComparison.Ordinal)))
                throw Logging.ErrorOnce(
                    new ArgumentException($"設定標籤重複：{label}", nameof(label)),
                    "實驗設定不合法", null, nameof(AddSolverConfig), label, "設定標籤重複");
            _configs.Add((label, config));
            return this;
        }

        /// <summary>額外加入一組，明確指定要搭配的模型、設定名稱與設定值。</summary>
        public OptExperiment AddTrial(OptModel model, string label, CplexConfig config)
        {
            if (model == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(model), "model 不得為 null"),
                    "實驗設定不合法", null, nameof(AddTrial), Name, "模型為空");
            ValidateLabel(label);
            if (config == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(config), "config 不得為 null"),
                    "實驗設定不合法", null, nameof(AddTrial), label, "設定為空");
            _explicitTrials.Add((model, label, config));
            return this;
        }

        /// <summary>執行所有模型與設定組合，儲存並回傳紀錄；正式環境是否成功看 <see cref="OptProject.IsSuccess"/>。</summary>
        public Experiment Run()
        {
            try
            {
                return RunCore();
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, _isProduction ? "求解執行失敗" : "實驗執行失敗", null, nameof(Run), Name,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        private Experiment RunCore()
        {
            // 正式環境一進來就清掉上一次的結果，設定不合法丟例外時才不會留下舊值。
            if (_isProduction)
            {
                _project.IsSuccess = false;
                _project.Trial = null;
            }

            var cells = new List<(OptModel Model, string Label, CplexConfig Config)>();
            foreach (var model in _models)
                foreach (var config in _configs)
                    cells.Add((model, config.Label, config.Config));
            cells.AddRange(_explicitTrials);

            if (cells.Count == 0)
                throw Logging.ErrorOnce(
                    new InvalidOperationException("至少需要一組模型與設定的搭配"),
                    "實驗設定不合法", null, nameof(Run), Name, "沒有任何模型與設定的搭配");

            var finalLabels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cell in cells)
            {
                string finalLabel = $"{cell.Model.Name} | {cell.Label}";
                if (!finalLabels.Add(finalLabel))
                    throw Logging.ErrorOnce(
                        new InvalidOperationException($"試跑標籤重複：{finalLabel}"),
                        "實驗設定不合法", null, nameof(Run), finalLabel, "試跑標籤重複");
            }

            // 正式環境只跑一組：多組會讓 project.Engine / Trial 不知道該留哪一組，比較多組請用 Experiment。
            if (_isProduction && cells.Count > 1)
                throw Logging.ErrorOnce(
                    new InvalidOperationException($"正式環境只能有一組模型與設定，目前有 {cells.Count} 組；比較多組請改用 project.Experiment(name)"),
                    "求解設定不合法", null, nameof(Run), Name, "超過一組模型與設定", $"組數={cells.Count}");

            string description = _isProduction && _description.Length == 0
                ? $"正式環境紀錄：{string.Join(", ", cells.Select(c => c.Model.Name).Distinct(StringComparer.Ordinal))}"
                : _description;
            var experiment = new Experiment(_project.Name, Name, description) { WriteSummary = !_isProduction };

            // 另一個 OptExperiment 可能切換 log，執行前須切回自己的 log。
            Logging.SetLogFileName(LogName);
            string projectName = _project.Name;

            // 多模型時加入模型名，避免輸出互相覆寫；正式環境沿用專案名。
            bool multiModel = cells.Select(c => c.Model.Name).Distinct(StringComparer.Ordinal).Count() > 1;

            Logging.Info(
                $"[{(_isProduction ? "正式環境開始" : "實驗開始")}] 名稱={FullName} 試跑數量={cells.Count} 多模型={(multiModel ? "開" : "關")} " +
                $"收斂軌跡={(_captureTrajectory ? "開" : "關")}");

            string runId = OptProject.NextRunId();
            int trialId = 0;

            try
            {
                foreach (var cell in cells)
                {
                    string runName = _isProduction
                        ? projectName
                        : multiModel ? $"{projectName}-{cell.Model.Name}-{cell.Label}" : $"{projectName}-{cell.Label}";
                    var engine = new OptEngine(cell.Config.Clone(), _projectConfig.Clone());
                    if (_isProduction) _project.ReplaceEngine(engine);
                    try
                    {
                        engine.SetModelName(runName);
                        var trial = engine.RunModel(cell.Model, cell.Label, _captureTrajectory, _beforeSolve, out bool solved);
                        trial.ExperimentId = runId;
                        trial.TrialId = ++trialId;
                        experiment.AddTrial(trial);
                        if (_isProduction)
                        {
                            _project.Trial = trial;
                            _project.IsSuccess = solved;
                        }

                        if (solved) _onSolved?.Invoke(engine);
                    }
                    finally
                    {
                        if (!_isProduction) engine.Dispose();
                    }
                }
            }
            catch
            {
                SaveCompleted(experiment, cells.Count);
                throw;
            }

            experiment.Save();
            if (_isProduction) _project.TotalElapsed = _project.Engine.ElapsedSinceRunStart;
            return experiment;
        }

        // 中途丟例外時照存已完成的組，避免 OnSolved 或後面某組失敗時遺失前面的求解結果。
        private void SaveCompleted(Experiment experiment, int total)
        {
            if (experiment.Trials.Count == 0) return;
            Logging.Warn($"[試跑中斷] 名稱={FullName} 已完成={experiment.Trials.Count}/{total} 結果=照存已完成的紀錄");
            try
            {
                experiment.Save();
            }
            catch (Exception saveEx)
            {
                Logging.Warn($"[試跑紀錄儲存失敗] 名稱={FullName} 原因={saveEx.GetBaseException().Message} 結果=略過");
            }
        }

        private static void ValidateLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                throw Logging.ErrorOnce(
                    new ArgumentException("設定標籤不得為空白", nameof(label)),
                    "實驗設定不合法", null, nameof(ValidateLabel), label, "標籤為空");
        }
    }
}
