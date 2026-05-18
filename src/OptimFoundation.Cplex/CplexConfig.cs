using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// CPLEX 求解器設定。camelCase fields 為 CPLEX 專屬參數（單一來源）；
    /// ISolverConfig properties 為 adapter，delegate 到對應 field。
    /// </summary>
    public sealed class CplexConfig : ISolverConfig
    {
        // ── CPLEX 專屬參數（單一來源，camelCase） ──────────────────────
        public int? workThreads = 32;
        public bool enableLog = false;
        public bool exportLP = false;
        public bool exportSol = false;
        public bool exportMPS = false;
        public int? rowRead = 30000;
        public double? workMemory = 2048;
        public double? epGap = 1e-4;
        public int? nodeSelect = null;
        public int? randomSeed = null;
        public double? epOpt = 1e-06;
        public double? epRHS = 1e-06;
        public double? timeLimit = null;
        public double? polishAfterTime = null;
        public int? mipEmphasis = null;
        public int? varSel = null;
        public int? algorithm = null;
        public int? nodeFileInd = null;

        // ── ISolverConfig — delegate 到 camelCase fields ──────────────
        public double? TimeLimit { get => timeLimit; set => timeLimit = value; }
        public double? MipGap { get => epGap; set => epGap = value; }
        public int? Threads { get => workThreads; set => workThreads = value; }
        public bool LogToConsole { get => !enableLog; set => enableLog = !value; }
        public string LogFilePath { get; set; }
        public int? RootAlgorithm { get => algorithm; set => algorithm = value; }

        // CPLEX 無直接對應的 ISolverConfig 延伸項
        public int? NodeAlgorithm { get; set; }
        public bool? PreIndicator { get; set; }
    }
}
