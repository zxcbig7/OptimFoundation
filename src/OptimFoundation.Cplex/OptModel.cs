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
        private readonly List<Action<OptEngine>> _variableSteps  = new List<Action<OptEngine>>();
        private readonly List<Action<OptEngine>> _modelSteps     = new List<Action<OptEngine>>();
        private readonly List<Action<OptEngine>> _solvedHandlers = new List<Action<OptEngine>>();
        private Func<CplexConfig> _configFactory = () => new CplexConfig();

        // ── 執行期狀態 ───────────────────────────────────────────────────
        public OptEngine optEngine;
        public Stopwatch buildModelTimer = new Stopwatch();
        public Stopwatch totalTimer      = new Stopwatch();
        public TimeSpan  totalTimeSpan   = new TimeSpan();

        private bool _isSuccess;

        /// <param name="projectName">用於 log 檔命名，辨識本次求解。</param>
        public OptModel(string projectName)
        {
            _isSuccess = false;
            Logging.SetLogFileName(projectName);
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
        public bool Execute()
        {
            totalTimer.Restart();

            // 求解器：組態由註冊的 factory 提供
            optEngine = new OptEngine(_configFactory());
            optEngine.Build();

            // 建構模型
            buildModelTimer.Restart();

            foreach (var step in _variableSteps) step(optEngine);   // 變數先建
            Logging.Info("【建構變數完成】", buildModelTimer);

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

        public void Dispose()
        {
            optEngine?.Dispose();
        }
    }
}
