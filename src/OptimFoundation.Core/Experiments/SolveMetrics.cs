using System.Collections.Generic;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 單次求解的統一 telemetry。
    /// 由各 engine 的 Solve() 回填到 EngineBase.LastMetrics。
    /// </summary>
    public sealed class SolveMetrics
    {
        public SolveStatus Status { get; set; }
        public double ObjectiveValue { get; set; }
        public double BestBound { get; set; }
        public double MipGap { get; set; }
        public double WallTimeMs { get; set; }
        public long? NodeCount { get; set; }
        public long? IterationCount { get; set; }
        public int VarCount { get; set; }
        public int ConstraintCount { get; set; }

        /// <summary>選用的逐點收斂軌跡；未啟用 captureTrajectory 時為空清單。</summary>
        public List<ConvergencePoint> Convergence { get; set; } = new List<ConvergencePoint>();
    }

    /// <summary>收斂軌跡的單一取樣點。</summary>
    public sealed class ConvergencePoint
    {
        public double TimeMs { get; set; }
        public double Objective { get; set; }
        public double Bound { get; set; }
        public double Gap { get; set; }
    }
}
