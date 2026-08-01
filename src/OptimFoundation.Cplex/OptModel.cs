using System;
using System.Collections.Generic;
using System.Diagnostics;

using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// 通用求解管線（Registry / Composition）。
    ///
    /// 不持有任何具體 Dataload / Variable / Constraint —— 在 <see cref="Execute"/> 之前，
    /// 由外部透過 <see cref="UseConfig"/> / <see cref="AddVariables"/> / <see cref="AddModel"/> /
    /// <see cref="OnSolved"/> 註冊一連串「建構步驟」（delegate）；Execute() 只負責在對的時機
    /// 把它們叫起來。因此對任何具體 model class 零依賴、不需繼承、永不需修改（Open/Closed）。
    ///
    /// 用法：
    /// <code>
    ///   using (var m = new OptModel("MyProject")
    ///       .UseConfig(() => new CplexConfig { timeLimit = 100 })
    ///       .AddVariables(e => e.BuildBVs&lt;MyVar&gt;(set1, set2))
    ///       .AddModel(e => new MyConstraint(data, e).Build())
    ///       .OnSolved(e => data.WriteToCSV(e)))
    ///   {
    ///       m.Execute();
    ///   }
    /// </code>
    /// </summary>
    public class OptModel : IDisposable
    {
        // ── 註冊清單（Execute 之前由外部填入）────────────────────────────
        private readonly List<Action<OptEngine>> _variableSteps = new List<Action<OptEngine>>();
        private readonly List<Action<OptEngine>> _modelSteps = new List<Action<OptEngine>>();
        private readonly List<Action<OptEngine>> _solvedHandlers = new List<Action<OptEngine>>();
        private Func<CplexConfig> _configFactory = () => new CplexConfig();

        // ── 執行期狀態（一律唯讀對外：看得到、不給換掉）──────────────────
        /// <summary>本次求解的引擎；Execute() 建立。細部資訊（模型名、狀態、目標值、best bound、建立明細）由它自己提供。</summary>
        public OptEngine optEngine { get; private set; }

        /// <summary>
        /// 記錄建構模型的時間，包含變數、目標式、限制式的建構。可用於 log 或除錯。
        /// </summary>
        public Stopwatch buildModelTimer { get; } = new Stopwatch();

        /// <summary>
        /// 記錄整體運作時間，包含建構模型、求解、後處理。可用於 log 或除錯。
        /// </summary>
        public Stopwatch totalTimer { get; } = new Stopwatch();

        /// <summary>上一次 Execute() 的總耗時快照（建模 + 求解 + 後處理）；Execute() 結束時定格，之後查得到。</summary>
        public TimeSpan totalTimeSpan { get; private set; }

        /// <summary>上一次 Execute() 是否求得可用解。Execute() 的回傳值，事後仍查得到。</summary>
        public bool IsSuccess => _isSuccess;

        /// <summary>本模型名稱——log 檔與 LP / MPS / Sol / IIS 輸出檔都以它命名。</summary>
        public string ProjectName => _projectName;

        private bool _isSuccess;
        private readonly string _projectName;


        /// <summary>
        /// 一個數學問題
        /// </summary>
        /// <param name="projectName">用於 log 與模型匯出檔（LP/MPS/Sol/IIS）命名，辨識本次求解。空白時預設 "Model"。</param>
        /// <param name="retentionDays">建構時自動清除各輸出資料夾中超過此天數的舊檔（Logs/Models/Sols/IISs/Experiments/Solution）。預設 30；&lt;= 0 關閉清理。</param>
        public OptModel(string projectName = "Model", int retentionDays = 30)
        {
            _isSuccess = false;
            _projectName = string.IsNullOrWhiteSpace(projectName) ? "Model" : projectName;
            Logging.SetLogFileName(_projectName);

            int purged = FolderDir.PurgeOutputs(retentionDays);
            if (purged > 0)
                Logging.Info($"[Housekeeping] 已清除 {purged} 個超過 {retentionDays} 天的舊輸出檔");
        }

        // ── 註冊 API（皆回傳 this，可鏈式呼叫）──────────────────────────

        /// <summary>設定求解器組態。未呼叫時使用 CplexConfig 預設值。</summary>
        public OptModel UseConfig(Func<CplexConfig> configFactory)
        {
            _configFactory = configFactory ?? throw new ArgumentNullException(nameof(configFactory));
            return this;
        }

        /// <summary>註冊變數建立步驟（保證在約束/目標式之前執行）。</summary>
        public OptModel AddVariables(Action<OptEngine> build)
        {
            _variableSteps.Add(build ?? throw new ArgumentNullException(nameof(build)));
            return this;
        }

        /// <summary>註冊目標式 / 限制式建立步驟（依註冊順序執行）。</summary>
        public OptModel AddModel(Action<OptEngine> build)
        {
            _modelSteps.Add(build ?? throw new ArgumentNullException(nameof(build)));
            return this;
        }

        /// <summary>註冊求解成功後的處理（例如輸出 CSV）。</summary>
        public OptModel OnSolved(Action<OptEngine> handler)
        {
            _solvedHandlers.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        // ── 執行流程：對具體 class 零依賴，永不需修改 ────────────────────

        /// <summary>
        /// 跑完整條求解管線：建引擎 → 套組態 → 依註冊順序建變數 → 建目標式 / 限制式 → 求解 →
        /// 成功才依序跑 OnSolved handler。全程計時並寫進 log。
        /// </summary>
        /// <returns>true = 求得可用解（Optimal 或 Feasible）；事後亦可由 <see cref="IsSuccess"/> 查詢。</returns>
        public bool Execute()
        {
            totalTimer.Restart();

            // 求解器：組態由註冊的 factory 提供
            optEngine = new OptEngine(_configFactory());
            optEngine.SetModelName(_projectName);
            optEngine.Build();

            // 建構模型
            buildModelTimer.Restart();

            if (_variableSteps.Count > 0)
            {
                foreach (var step in _variableSteps) step(optEngine);   // 變數先建
                Logging.Info("【建構變數完成】", buildModelTimer);
            }

            foreach (var step in _modelSteps) step(optEngine);      // 再建目標式 / 限制式
            Logging.Info("【建構模型完成】", buildModelTimer);

            buildModelTimer.Stop();

            // 求解
            _isSuccess = optEngine.Solve();

            if (_isSuccess)
                foreach (var handler in _solvedHandlers) handler(optEngine);

            totalTimeSpan = totalTimer.Elapsed;
            totalTimer.Stop();

            Logging.Info("【整體運作時間】", totalTimer);
            return _isSuccess;
        }

        /// <summary>釋放本次求解建立的引擎（連同 CPLEX native 資源）。ALWAYS 用 using 包住 OptModel，否則 native 記憶體不會回收。</summary>
        public void Dispose()
        {
            optEngine?.Dispose();
        }
    }
}
