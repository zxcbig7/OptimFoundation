using System;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 一次求解的完整記錄：設定快照 + 收斂指標。套件化用法的最小單位。
    /// </summary>
    public sealed class Trial
    {
        /// <summary>這次求解的標籤，只放設定名稱，例 "r1-GomoryCuts=2"。模型名另外放在 <see cref="Model"/>。</summary>
        public string Label { get; set; }

        /// <summary>這批實驗的識別，值是該次執行的開始時間（yyyyMMdd-HHmmss）。
        /// 同一個實驗跑很多次時，靠它分辨哪些列是同一批。</summary>
        public string RunId { get; set; }

        /// <summary>同一批實驗內的流水號，從 1 開始。與 <see cref="RunId"/> 合起來唯一。</summary>
        public int TrialId { get; set; }

        /// <summary>這次跑的是哪個模型。以前是黏在 Label 前面（"模型名 | 設定名"），現在拆開成獨立欄位。</summary>
        public string Model { get; set; }

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
