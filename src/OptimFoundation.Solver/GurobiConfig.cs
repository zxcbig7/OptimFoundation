using OptimFoundation.Core;

namespace OptimFoundation.Solver
{
    public sealed class SolverConfig : ISolverConfig, ITunableConfig
    {
        public double? TimeLimit    { get; set; }
        public double? MipGap       { get; set; }
        public int?    Threads      { get; set; }
        public bool    LogToConsole { get; set; } = false;
        public string  LogFilePath  { get; set; } = string.Empty;
        public double  Epsilon      { get; set; } = 1e-9;

        public bool exportLP  = false;
        public bool exportMPS = false;
        public bool exportSol = false;

        // ── ITunableConfig — 自研 solver 僅 FeasibilityTol 有對應（Epsilon），其餘暫無、僅供快照記錄 ──
        public double? FeasibilityTol  { get => Epsilon; set => Epsilon = value ?? 1e-9; }
        public int?    Seed            { get; set; }
        public int?    Emphasis        { get; set; }
        public double? OptimalityTol   { get; set; }
        public int?    RootAlgorithm   { get; set; }
        public int?    Presolve        { get; set; }
        public double? HeuristicEffort { get; set; }
        public double? MemoryLimitMb   { get; set; }
    }
}
