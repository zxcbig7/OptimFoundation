using System;
using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// 執行模型與求解器設定的組合，並把每次結果存成 Trial。
    /// 正式求解（<see cref="OptProduction"/>）與實驗（<see cref="OptExperiment"/>）共用這組動詞，只差規則與預設值。
    /// </summary>
    public abstract class OptExecution
    {
        private readonly string _description;
        private readonly string _logName;
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

        private protected OptExecution(string projectName, string name, string description, string logName,
            ProjectConfig projectConfig, bool captureTrajectory)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw Logging.ErrorOnce(
                    new ArgumentException("實驗名稱不得為空白", nameof(name)),
                    "實驗設定不合法", null, nameof(OptExecution), name, "名稱為空");
            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                throw Logging.ErrorOnce(
                    new ArgumentException($"實驗名稱含不合法的檔名字元：{name}", nameof(name)),
                    "實驗設定不合法", null, nameof(OptExecution), name, "檔名含不合法字元");
            ProjectName = projectName;
            Name = name;
            _description = description ?? string.Empty;
            _logName = logName;
            _projectConfig = projectConfig;
            _captureTrajectory = captureTrajectory;

            // 建立時切換 log，讓 Run 前的準備作業也記在同一檔。
            Logging.SetLogFileName(_logName);
        }

        /// <summary>實驗名；正式環境固定為 <c>production</c>。</summary>
        public string Name { get; }

        /// <summary>實驗紀錄的檔名前綴：{專案名}-{實驗名}。</summary>
        public string FullName => $"{ProjectName}-{Name}";

        private protected string ProjectName { get; }

        /// <summary>設定所有組合共用的專案選項；重複設定會覆蓋前一份並記錄警告。</summary>
        public OptExecution AddProjectConfig(ProjectConfig config)
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

        /// <summary>是否記錄目標值、最佳界限與 MIP gap 的變化；callback 可能增加求解時間。</summary>
        public OptExecution CaptureTrajectory(bool enabled)
        {
            _captureTrajectory = enabled;
            return this;
        }

        /// <summary>每組建模完成後、求解前執行；null 表示不執行。</summary>
        public OptExecution BeforeSolve(Action<OptEngine> handler)
        {
            _beforeSolve = handler;
            return this;
        }

        /// <summary>每組找到可用解（Optimal 或 Feasible）後執行，通常用來讀解、驗證、寫出；null 表示不執行。</summary>
        public OptExecution OnSolved(Action<OptEngine> handler)
        {
            _onSolved = handler;
            return this;
        }

        /// <summary>加入一個模型；執行時會分別搭配每組 AddSolverConfig 設定。</summary>
        public OptExecution AddModel(OptModel model)
        {
            if (model == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(model), "model 不得為 null"),
                    "實驗設定不合法", null, nameof(AddModel), Name, "模型為空");
            _models.Add(model);
            return this;
        }

        /// <summary>加入一組有名稱的求解器設定；執行時會分別搭配每個 AddModel 模型。</summary>
        public OptExecution AddSolverConfig(string label, CplexConfig config)
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
        public OptExecution AddTrial(OptModel model, string label, CplexConfig config)
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

        /// <summary>執行所有模型與設定組合，儲存並回傳紀錄；正式環境是否成功看 <c>project.IsSuccess</c>。</summary>
        public Experiment Run()
        {
            try
            {
                return RunCore();
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, FailureEventName, null, nameof(Run), Name, ex.GetBaseException().Message);
                throw;
            }
        }

        #region 子類別的規則與流程

        private protected abstract string FailureEventName { get; }

        private protected abstract Experiment RunCore();

        #endregion

        #region 共用步驟

        // 展開 AddModel × AddSolverConfig，再接上 AddTrial；至少要有一組，模型名 + 標籤不得重複。
        private protected List<(OptModel Model, string Label, CplexConfig Config)> ExpandTrials()
        {
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
            return cells;
        }

        // 切回自己的 log（另一個 OptExecution 可能切換過），建立紀錄並記下開始事件；description 沒給時用 defaultDescription。
        private protected Experiment StartRecord(string startEventName, string defaultDescription, bool writeSummary,
            int trialCount, bool multiModel)
        {
            string description = _description.Length == 0 ? defaultDescription : _description;
            var record = new Experiment(ProjectName, Name, description) { WriteSummary = writeSummary };

            Logging.SetLogFileName(_logName);
            Logging.Info(
                $"[{startEventName}] 名稱={FullName} 試跑數量={trialCount} 多模型={(multiModel ? "開" : "關")} " +
                $"收斂軌跡={(_captureTrajectory ? "開" : "關")}");
            return record;
        }

        private protected OptEngine CreateEngine(CplexConfig config) => new OptEngine(config.Clone(), _projectConfig.Clone());

        // 在 engine 上建模、執行 BeforeSolve、求解，並把 Trial 加進紀錄；OnSolved 由呼叫端在記錄結果後執行。
        private protected Trial RunTrial(OptEngine engine, (OptModel Model, string Label, CplexConfig Config) cell,
            string runName, Experiment record, string runId, out bool solved)
        {
            engine.SetModelName(runName);
            var trial = engine.RunModel(cell.Model, cell.Label, _captureTrajectory, _beforeSolve, out solved);
            trial.ExperimentId = runId;
            trial.TrialId = record.Trials.Count + 1;
            record.AddTrial(trial);
            return trial;
        }

        private protected void InvokeOnSolved(OptEngine engine) => _onSolved?.Invoke(engine);

        // 中途丟例外時照存已完成的組，避免 OnSolved 或後面某組失敗時遺失前面的求解結果。
        private protected void SaveCompleted(Experiment record, int total)
        {
            if (record.Trials.Count == 0) return;
            Logging.Warn($"[試跑中斷] 名稱={FullName} 已完成={record.Trials.Count}/{total} 結果=照存已完成的紀錄");
            try
            {
                record.Save();
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

        #endregion

        #region 批次識別

        private static readonly object RunIdLock = new object();
        private static string _lastRunStamp;
        private static int _runStampRepeat;

        /// <summary>
        /// 批次識別使用 yyyyMMdd-HHmmss；同秒重複時加流水號。
        /// </summary>
        private protected static string NextRunId()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            lock (RunIdLock)
            {
                _runStampRepeat = stamp == _lastRunStamp ? _runStampRepeat + 1 : 1;
                _lastRunStamp = stamp;
                return _runStampRepeat == 1 ? stamp : $"{stamp}-{_runStampRepeat}";
            }
        }

        #endregion
    }
}
