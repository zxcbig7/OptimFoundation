using OptimFoundation.Core;

namespace OptimFoundation.Gurobi
{
    public sealed class GurobiConfig : ISolverConfig
    {
        // ISolverConfig 共用參數
        public double? TimeLimit    { get; set; }
        public double? MipGap       { get; set; }
        public int?    Threads      { get; set; }
        public bool    LogToConsole { get; set; } = true;
        public string  LogFilePath  { get; set; }

        // Gurobi 演算法參數
        public int?    Method       { get; set; }   // GRB.IntParam.Method
        public int?    Presolve     { get; set; }   // GRB.IntParam.Presolve
        public int?    MipFocus     { get; set; }   // GRB.IntParam.MIPFocus
        public int?    Seed         { get; set; }   // GRB.IntParam.Seed
        public double? FeasibilityTol { get; set; } // GRB.DoubleParam.FeasibilityTol
        public double? OptimalityTol  { get; set; } // GRB.DoubleParam.OptimalityTol
        public double? Heuristics     { get; set; } // GRB.DoubleParam.Heuristics (0~1)
        public double? SoftMemLimit   { get; set; } // GRB.DoubleParam.SoftMemLimit (GB)

        // 輸出設定
        public bool   ExportLp  { get; set; }
        public bool   ExportMps { get; set; }
        public bool   ExportSol { get; set; }
        public string ProjectName { get; set; } = "Project";

        // Gurobi WLS License
        public int?   LicenseId  { get; set; }
        public string WlsAccessId { get; set; }
        public string WlsSecret   { get; set; }
    }
}
