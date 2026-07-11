using OptimFoundation.Core;

namespace OptimFoundation.Gurobi
{
    public sealed class GurobiConfig : ISolverConfig, ITunableConfig
    {
        // ISolverConfig 共用參數
        public double? TimeLimit { get; set; }
        public double? MipGap { get; set; }
        public int? Threads { get; set; }
        public bool LogToConsole { get; set; } = true;
        public string LogFilePath { get; set; }

        // Gurobi 演算法參數
        public int? Method { get; set; }   // GRB.IntParam.Method
        public int? Presolve { get; set; }   // GRB.IntParam.Presolve
        public int? MipFocus { get; set; }   // GRB.IntParam.MIPFocus
        public int? Seed { get; set; }   // GRB.IntParam.Seed
        public double? FeasibilityTol { get; set; } // GRB.DoubleParam.FeasibilityTol
        public double? OptimalityTol { get; set; } // GRB.DoubleParam.OptimalityTol
        public double? Heuristics { get; set; } // GRB.DoubleParam.Heuristics (0~1)
        public double? SoftMemLimit { get; set; } // GRB.DoubleParam.SoftMemLimit (GB)

        // 輸出設定
        public bool ExportLp { get; set; }
        public bool ExportMps { get; set; }
        public bool ExportSol { get; set; }
        public string ProjectName { get; set; } = "Model";

        // Gurobi WLS License
        public int? LicenseId { get; set; }
        public string WlsAccessId { get; set; }
        public string WlsSecret { get; set; }

        // ── ITunableConfig — delegate 到既有 Gurobi 欄位 ──
        //    Seed / Presolve / FeasibilityTol / OptimalityTol 已由上方同名欄位直接滿足
        public int? Emphasis { get => MipFocus; set => MipFocus = value; }
        public int? RootAlgorithm { get => Method; set => Method = value; }
        public double? HeuristicEffort { get => Heuristics; set => Heuristics = value; }
        public double? MemoryLimitMb   // ITunableConfig 為 MB，Gurobi SoftMemLimit 為 GB
        {
            get => SoftMemLimit.HasValue ? SoftMemLimit.Value * 1024.0 : (double?)null;
            set => SoftMemLimit = value.HasValue ? value.Value / 1024.0 : (double?)null;
        }
    }
}
