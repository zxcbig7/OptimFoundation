using System;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 一次求解的完整記錄：設定快照 + 收斂指標。套件化用法的最小單位。
    /// </summary>
    public sealed class Trial
    {
        /// <summary>這次求解的標籤，例 "emphasis=2"；與 RunAt 一起當 append 去重的鍵。</summary>
        public string Label { get; set; }

        /// <summary>求解記錄的建立時間（Capture 當下）。</summary>
        public DateTime RunAt { get; set; }

        /// <summary>求解前的設定快照，供事後重現這次結果。</summary>
        public ConfigSnapshot Config { get; set; }

        /// <summary>求解結果指標（狀態、目標值、gap、耗時、節點數、收斂軌跡）。</summary>
        public SolveMetrics Metrics { get; set; }

        /// <summary>自由備註，寫進 CSV / JSON 供日後辨識。</summary>
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
            if (engine == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(engine)),
                    "TRIAL_CAPTURE_INVALID", "實驗紀錄擷取失敗", nameof(Capture), label, "engine_is_null");
            if (solveAction == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(solveAction)),
                    "TRIAL_CAPTURE_INVALID", "實驗紀錄擷取失敗", nameof(Capture), label, "solve_action_is_null");

            try
            {
                return CaptureCore(engine, label, solveAction, note);
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "TRIAL_CAPTURE_FAILED", "公開 API 執行失敗", nameof(Capture), label,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        private static Trial CaptureCore(ISolverEngine engine, string label, Func<bool> solveAction, string note)
        {

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
