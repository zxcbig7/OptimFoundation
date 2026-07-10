namespace OptimFoundation.Core
{
    /// <summary>
    /// 跨引擎共通的 tuning 控制項目抽象。與 <see cref="ISolverConfig"/> 並存（加法，不破壞既有）。
    /// 各 concrete config 將這些抽象控制項目對映到自家專屬欄位；null = 使用 solver 預設。
    /// </summary>
    public interface ITunableConfig
    {
        int? Seed { get; set; }  // CPLEX randomSeed / Gurobi Seed
        int? Emphasis { get; set; }  // CPLEX mipEmphasis / Gurobi MipFocus（只記原始整數值，不做語意正規化）
        double? FeasibilityTol { get; set; }  // CPLEX epRHS / Gurobi FeasibilityTol / Solver Epsilon
        double? OptimalityTol { get; set; }  // CPLEX epOpt / Gurobi OptimalityTol
        int? RootAlgorithm { get; set; }  // CPLEX algorithm / Gurobi Method
        int? Presolve { get; set; }  // CPLEX PreInd / Gurobi Presolve
        double? HeuristicEffort { get; set; }  // Gurobi Heuristics(0~1) / CPLEX 對映
        double? MemoryLimitMb { get; set; }  // CPLEX workMemory / Gurobi SoftMemLimit
    }
}
