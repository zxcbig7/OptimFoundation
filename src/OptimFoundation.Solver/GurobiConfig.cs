using OptimFoundation.Core;

namespace OptimFoundation.Solver
{
    public sealed class SolverConfig : ISolverConfig
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
    }
}
