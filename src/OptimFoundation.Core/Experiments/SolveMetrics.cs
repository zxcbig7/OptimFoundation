using System.Collections.Generic;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 單次求解的統一 telemetry。
    /// 由各 engine 的 Solve() 回填到 EngineBase.LastMetrics。
    /// </summary>
    public sealed class SolveMetrics
    {
        public SolveStatus Status { get; set; } // 求解結果狀態（Optimal / Feasible / Infeasible / TimeLimit …）
        public double ObjectiveValue { get; set; } // 目標式值（若有 soft constraint 則已含 penalty）
        public double BestBound { get; set; } // 最佳界（MIP 的 best bound）
        public double MipGap { get; set; } // 相對 MIP gap
        public double RunTimeMs { get; set; } // 求解時間（毫秒）
        public long? NodeCount { get; set; } // B&B 探索節點數（null = solver 未提供）
        public long? IterationCount { get; set; } // simplex / barrier 迭代數（null = 未提供）
        public int VarCount { get; set; } // 變數總數
        public int ConstraintCount { get; set; } // 限制式總數

        /// <summary>選用的逐點收斂軌跡；未啟用 captureTrajectory 時為空清單。</summary>
        public List<ConvergencePoint> Convergence { get; set; } = new List<ConvergencePoint>();
    }

    /// <summary>收斂軌跡的單一取樣點。</summary>
    public sealed class ConvergencePoint
    {
        public double TimeMs { get; set; } // 取樣時刻（求解開始起算的毫秒）
        public double Objective { get; set; } // 該時刻的 incumbent 目標值
        public double Bound { get; set; } // 該時刻的最佳界
        public double Gap { get; set; } // 該時刻的相對 gap
    }
}
