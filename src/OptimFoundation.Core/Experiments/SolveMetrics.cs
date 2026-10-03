using System.Collections.Generic;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 一次求解的狀態、目標值、耗時，以及節點數等統計。
    /// 各 engine 執行 Solve() 後，會把這些資料寫入 EngineBase.LastMetrics。
    /// </summary>
    public sealed class SolveMetrics
    {
        /// <summary> 求解結果狀態（Optimal / Feasible / Infeasible / TimeLimit …）。</summary>
        public SolveStatus Status { get; set; }

        /// <summary>目標式值；有軟性限制式時已含 penalty。無解時為 NaN。</summary>
        public double ObjectiveValue { get; set; }

        /// <summary>求解器目前證明的目標值界限（MIP best bound），用來估計離最佳解還有多遠；無可用值時為 NaN。</summary>
        public double BestBound { get; set; }

        /// <summary>求解結束時實際達到的相對 MIP gap（不是 CplexConfig.MipGap 那個停止門檻）。無解時為 NaN。</summary>
        public double Gap { get; set; }

        /// <summary>純求解耗時（毫秒），只計 Solve() 本身、不含建模；用 CPLEX 的時鐘（GetCplexTime）在 Solve 前後各取一次相減。</summary>
        public double SolveTimeMs { get; set; }

        /// <summary>
        /// 建模 + 求解耗時（毫秒）= 把 OptModel 套進 CPLEX 的時間（讀模型檔時含讀檔與建立查找索引）+ <see cref="SolveTimeMs"/>；兩段都用 CPLEX 的時鐘量。
        /// 不含 beforeSolve、匯出模型 / 解檔與 IIS 分析。只有經由 OptProject.Solve / OptExperiment 執行才有值，自己呼叫 Trial.Capture 時為 null。
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

        // ── 模型結構：一律取自求解器模型本身（CPLEX Ncols / Nrows / NbinVars …），不用框架建模時的統計 ──

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

        // 以下四項由 Convergence 計算；純 LP、未開啟軌跡等情況下沒有軌跡，回傳 null 或 0。

        /// <summary>軌跡點數。0 代表這次求解沒有收集到軌跡（純 LP 沒有分支定界過程，或求解太快）。</summary>
        public int TrajectoryPoints => Convergence?.Count ?? 0;

        /// <summary>
        /// 紀錄中第一次出現可行解的時間（毫秒，從求解開始起算）；未記錄到可行解時為 null，不是 0。
        /// 軌跡只含 CPLEX 實際呼叫 callback 時觀察到的點；presolve 或 root 就解完時 callback 不會被呼叫，軌跡為空，這裡也是 null。
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

        /// <summary>紀錄中最佳界最後一次變動的時間（毫秒，從求解開始起算）；用來查看何時開始不再改善。
        /// 只比較相鄰取樣值是否不同，因此最小化與最大化都適用；沒有軌跡或未曾變動時為 null。</summary>
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
