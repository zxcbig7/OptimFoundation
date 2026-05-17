using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    public sealed class CplexConfig : ISolverConfig
    {
        public double? TimeLimit { get; set; }
        public double? MipGap { get; set; }
        public int? Threads { get; set; }
        public bool LogToConsole { get; set; } = true;
        public string LogFilePath { get; set; }

        public int? RootAlgorithm { get; set; }
        public int? NodeAlgorithm { get; set; }
        public bool? PreIndicator { get; set; }
    }
}
