using System.Collections.Generic;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 單次求解的統一 telemetry。
    /// 由各 engine 的 Solve() 回填到 EngineBase.LastMetrics。
    /// </summary>
    public sealed class SolveMetrics
    {
        /// <summary>求解結果狀態（Optimal / Feasible / Infeasible / TimeLimit …）。</summary>
        public SolveStatus Status { get; set; }

        /// <summary>目標式值；有軟性限制式時已含 penalty。無解時為 NaN。</summary>
        public double ObjectiveValue { get; set; }

        /// <summary>最佳界（MIP best bound）。無解時為 NaN。</summary>
        public double BestBound { get; set; }

        /// <summary>相對 MIP gap。無解時為 NaN。</summary>
        public double MipGap { get; set; }

        /// <summary>求解耗時（毫秒），只計 Solve() 本身、不含建模。</summary>
        public double RunTimeMs { get; set; }

        /// <summary>B&amp;B 探索節點數；null = 該 solver 未提供。</summary>
        public long? NodeCount { get; set; }

        /// <summary>simplex / barrier 迭代數；null = 未提供。</summary>
        public long? IterationCount { get; set; }

        /// <summary>模型的變數總數。</summary>
        public int VarCount { get; set; }

        /// <summary>模型的限制式總數。</summary>
        public int ConstraintCount { get; set; }

        /// <summary>選用的逐點收斂軌跡；未啟用 captureTrajectory 時為空清單。</summary>
        public List<ConvergencePoint> Convergence { get; set; } = new List<ConvergencePoint>();

        // ── 下面四個是從 Convergence 直接算出來的，不另外存 ──────────────
        // 這樣它們不可能跟軌跡對不上。純 LP 或軌跡沒開時，軌跡是空的，這四個就都是 null / 0。

        /// <summary>軌跡點數。0 代表這次求解沒有收集到軌跡（純 LP 沒有分支定界過程，或求解太快）。</summary>
        public int TrajectoryPoints => Convergence?.Count ?? 0;

        /// <summary>第一次找到可行解的時間（毫秒）。從頭到尾都沒找到解時為 null——注意不是 0。</summary>
        public double? TFeasMs
        {
            get
            {
                if (Convergence == null) return null;
                foreach (var p in Convergence)
                    if (!double.IsNaN(p.Objective))
                        return p.TimeMs;
                return null;
            }
        }

        /// <summary>整段求解過程中，最佳界一共推進了多少（最後一點減第一點）。沒有軌跡時為 null。</summary>
        public double? DeltaBound
        {
            get
            {
                if (Convergence == null || Convergence.Count == 0) return null;
                return Convergence[Convergence.Count - 1].Bound - Convergence[0].Bound;
            }
        }

        /// <summary>最佳界最後一次變動的時間（毫秒）。它之後界就停在原地不動了。
        /// 用「有沒有變動」判斷而不是「有沒有變好」，這樣最小化與最大化問題都適用。沒有軌跡時為 null。</summary>
        public double? TStallMs
        {
            get
            {
                if (Convergence == null || Convergence.Count == 0) return null;
                double? last = null;
                for (int i = 1; i < Convergence.Count; i++)
                    if (Convergence[i].Bound != Convergence[i - 1].Bound)
                        last = Convergence[i].TimeMs;
                return last;
            }
        }
    }

    /// <summary>收斂軌跡的單一取樣點。</summary>
    public sealed class ConvergencePoint
    {
        /// <summary>取樣時刻，從求解開始起算的毫秒。</summary>
        public double TimeMs { get; set; }

        /// <summary>該時刻的 incumbent 目標值；尚無可行解時為 NaN。</summary>
        public double Objective { get; set; }

        /// <summary>該時刻的最佳界。</summary>
        public double Bound { get; set; }

        /// <summary>該時刻的相對 gap；尚無可行解時為 NaN。</summary>
        public double Gap { get; set; }
    }
}
