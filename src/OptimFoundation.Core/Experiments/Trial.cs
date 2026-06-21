using System;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 一次求解的完整記錄：設定快照 + 收斂指標。套件化用法的最小單位。
    /// </summary>
    public sealed class Trial
    {
        public string Label { get; set; }
        public DateTime RunAt { get; set; }
        public ConfigSnapshot Config { get; set; }
        public SolveMetrics Metrics { get; set; }
        public string Note { get; set; }

        /// <summary>
        /// 套件化單次擷取：讀 engine.Config → 跑 solveAction（一次求解）→ 讀 engine.LastMetrics。
        /// 不接管、不 Dispose engine（生命週期由呼叫端持有）。
        /// </summary>
        /// <param name="engine">已 Build 完成的求解引擎</param>
        /// <param name="label">這次 Trial 的標籤（如 "emphasis=2"）</param>
        /// <param name="solveAction">執行一次求解的動作，回傳是否成功</param>
        /// <param name="note">選填備註</param>
        public static Trial Capture(ISolverEngine engine, string label, Func<bool> solveAction, string note = null)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (solveAction == null) throw new ArgumentNullException(nameof(solveAction));

            var snapshot = ConfigSnapshot.From(engine.Config);

            // 選用：支援軌跡的 engine（本期 CPLEX）在求解前啟用
            if (engine is ITrajectorySource ts && ts.SupportsTrajectory)
                ts.EnableTrajectory();

            solveAction();   // 跑一次求解；非 Optimal（TimeLimit/Feasible）仍照記錄，不視為失敗

            var metrics = engine.LastMetrics ?? new SolveMetrics { Status = engine.Status };

            return new Trial
            {
                Label = label,
                RunAt = DateTime.Now,
                Config = snapshot,
                Metrics = metrics,
                Note = note
            };
            // 不呼叫 engine.Dispose()：engine 生命週期由呼叫端持有
        }
    }
}
