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
