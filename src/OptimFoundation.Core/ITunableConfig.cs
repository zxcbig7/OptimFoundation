namespace OptimFoundation.Core
{
    /// <summary>
    /// 跨引擎共通的 tuning 控制項目抽象。與 <see cref="ISolverConfig"/> 並存（加法，不破壞既有）。
    /// 各 concrete config 將這些抽象控制項目對映到自家專屬欄位；null = 使用 solver 預設。
    /// </summary>
    public interface ITunableConfig
    {
        /// <summary>隨機種子（CPLEX randomSeed / Gurobi Seed）。要重現結果就固定它。</summary>
        int? Seed { get; set; }

        /// <summary>求解重點（CPLEX mipEmphasis / Gurobi MipFocus）。只記原始整數值，各 solver 語意不同、不做正規化。</summary>
        int? Emphasis { get; set; }

        /// <summary>可行性容差（CPLEX epRHS / Gurobi FeasibilityTol / Solver Epsilon）。</summary>
        double? FeasibilityTol { get; set; }

        /// <summary>最佳性容差（CPLEX epOpt / Gurobi OptimalityTol）。</summary>
        double? OptimalityTol { get; set; }

        /// <summary>根節點 LP 演算法（CPLEX algorithm / Gurobi Method）。取值語意依 solver。</summary>
        int? RootAlgorithm { get; set; }

        /// <summary>前處理開關（CPLEX PreInd / Gurobi Presolve）；0 = 關閉。</summary>
        int? Presolve { get; set; }

        /// <summary>啟發式投入程度（Gurobi Heuristics 0~1 / CPLEX HeuristicEffort）。</summary>
        double? HeuristicEffort { get; set; }

        /// <summary>記憶體上限 MB（CPLEX workMemory / Gurobi SoftMemLimit）。</summary>
        double? MemoryLimitMb { get; set; }
    }
}
