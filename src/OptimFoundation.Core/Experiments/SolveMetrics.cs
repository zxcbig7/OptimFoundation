using System.Collections.Generic;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 一次求解的狀態與統計；Solve 後寫入 EngineBase.LastMetrics。
    /// </summary>
    public sealed class SolveMetrics
    {
        /// <summary> 求解結果狀態（Optimal / Feasible / Infeasible / TimeLimit …）。</summary>
        public SolveStatus Status { get; set; }

        /// <summary>目標式值；有軟性限制式時已含 penalty。無解時為 NaN。</summary>
        public double ObjectiveValue { get; set; }

        /// <summary>MIP best bound；無可用值時為 NaN。</summary>
        public double BestBound { get; set; }

        /// <summary>求解結束時的相對 MIP gap；無解時為 NaN。</summary>
        public double Gap { get; set; }

        /// <summary>純求解耗時（毫秒），以 CPLEX 時鐘測量，不含建模。</summary>
        public double SolveTimeMs { get; set; }

        /// <summary>
        /// 建模與求解耗時（毫秒，CPLEX 時鐘）；建模含讀檔與索引，不含 beforeSolve、匯出及 IIS。
        /// 僅 OptProject.Solve / OptExperiment 填入；直接 Trial.Capture 為 null。
        /// </summary>
        public double? BuildAndSolveTimeMs { get; set; }

        /// <summary>分支定界法（B&amp;B）探索的節點數；null 表示求解器未提供。</summary>
        public long? NodeCount { get; set; }

        /// <summary>simplex / barrier 迭代數；null = 未提供。</summary>
        public long? IterationCount { get; set; }

        /// <summary>這次求解實際使用的亂數種子（沒明設時就是求解器預設值）；null 表示求解器未提供。</summary>
        public int? Seed { get; set; }

        /// <summary>這次求解有沒有開收斂軌跡。用來分辨「沒開」與「開了但 CPLEX 沒呼叫 callback」。</summary>
        public bool TrajectoryEnabled { get; set; }

        // 模型結構取自求解器，非框架建立計數。

        /// <summary>問題類型（LP / MILP / IP / BP），由求解器模型的變數組成判定；null 表示求解器未提供。</summary>
        public ModelType? ModelType { get; set; }

        /// <summary>目標式方向；null 表示模型沒有目標式。</summary>
        public ObjectiveSense? ObjectiveSense { get; set; }

        /// <summary>求解器模型的變數總數（CPLEX Ncols，只算已收進模型的變數）。</summary>
        public int VarCount { get; set; }

        /// <summary>Binary 變數數；null 表示求解器未提供。</summary>
        public int? BinaryVarCount { get; set; }

        /// <summary>Integer 變數數；null 表示求解器未提供。</summary>
        public int? IntegerVarCount { get; set; }

        /// <summary>連續變數數，不含 semi-continuous / semi-integer；null 表示求解器未提供。</summary>
        public int? ContinuousVarCount { get; set; }

        /// <summary>semi-continuous 變數數；null 表示求解器未提供。</summary>
        public int? SemiContinuousVarCount { get; set; }

        /// <summary>semi-integer 變數數；null 表示求解器未提供。</summary>
        public int? SemiIntegerVarCount { get; set; }

        /// <summary>求解器模型的線性限制式條數（CPLEX Nrows）。</summary>
        public int ConstraintCount { get; set; }

        /// <summary>二次限制式條數；null 表示求解器未提供。</summary>
        public int? QuadraticConstraintCount { get; set; }

        /// <summary>indicator 限制式條數；null 表示求解器未提供。</summary>
        public int? IndicatorConstraintCount { get; set; }

        /// <summary>SOS 個數；null 表示求解器未提供。</summary>
        public int? SosCount { get; set; }

        /// <summary>lazy constraint 條數；null 表示求解器未提供。</summary>
        public int? LazyConstraintCount { get; set; }

        /// <summary>user cut 條數；null 表示求解器未提供。</summary>
        public int? UserCutCount { get; set; }

        /// <summary>求解過程中各取樣時刻的目標值、最佳界與 gap；未啟用 captureTrajectory 時為空清單。</summary>
        public List<ConvergencePoint> Convergence { get; set; } = new List<ConvergencePoint>();


        /// <summary>軌跡點數。0 代表這次求解沒有收集到軌跡（純 LP 沒有分支定界過程，或求解太快）。</summary>
        public int TrajectoryPoints => Convergence?.Count ?? 0;

        /// <summary>
        /// 首次記錄可行解的時間（求解起算毫秒）；未觀察到為 null。
        /// 僅計 callback 取樣；presolve 或 root 解完可能沒有取樣。
        /// </summary>
        public double? FirstSolutionMs
        {
            get
            {
                if (Convergence == null) return null;
                foreach (var p in Convergence)
                    if (!double.IsNaN(p.ObjectiveValue))
                        return p.ElapsedMs;
                return null;
            }
        }

        /// <summary>最佳界的總變化量（最後一個取樣值減第一個）。沒有軌跡時為 null。</summary>
        public double? BoundChange
        {
            get
            {
                if (Convergence == null || Convergence.Count == 0) return null;
                return Convergence[Convergence.Count - 1].BestBound - Convergence[0].BestBound;
            }
        }

        /// <summary>最佳界最後一次取樣變動的時間（求解起算毫秒）；無軌跡或未變動時為 null，適用兩種目標方向。</summary>
        public double? LastBoundChangeMs
        {
            get
            {
                if (Convergence == null || Convergence.Count == 0) return null;
                double? last = null;
                for (int i = 1; i < Convergence.Count; i++)
                    if (Convergence[i].BestBound != Convergence[i - 1].BestBound)
                        last = Convergence[i].ElapsedMs;
                return last;
            }
        }
    }

    /// <summary>收斂軌跡的單一取樣點。</summary>
    public sealed class ConvergencePoint
    {
        /// <summary>取樣時刻：從這次求解開始起算經過的毫秒。</summary>
        public double ElapsedMs { get; set; }

        /// <summary>該時刻已找到的最佳可行解（incumbent）目標值；尚無可行解時為 NaN。</summary>
        public double ObjectiveValue { get; set; }

        /// <summary>該時刻的最佳界。</summary>
        public double BestBound { get; set; }

        /// <summary>該時刻的相對 gap；尚無可行解時為 NaN。</summary>
        public double Gap { get; set; }
    }
}
