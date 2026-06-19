using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// CPLEX 求解器設定。camelCase fields 為 CPLEX 專屬參數（單一來源）；
    /// ISolverConfig properties 為 adapter，delegate 到對應 field。
    /// </summary>
    public sealed class CplexConfig : ISolverConfig, ITunableConfig
    {
        // ── CPLEX 專屬參數（單一來源，camelCase） ──────────────────────
        public int? workThreads = 32;
        public bool enableLog = true;
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
        public bool LogToConsole { get => enableLog; set => enableLog = value; }
        public string LogFilePath { get; set; }
        public int? RootAlgorithm { get => algorithm; set => algorithm = value; }

        // CPLEX 無直接對應的 ISolverConfig 延伸項
        public int? NodeAlgorithm { get; set; }
        public bool? PreIndicator { get; set; }

        // ── ITunableConfig — 抽象旋鈕 delegate 到既有 CPLEX 欄位（RootAlgorithm 由上方滿足） ──
        public int?    Seed           { get => randomSeed;  set => randomSeed  = value; }
        public int?    Emphasis       { get => mipEmphasis; set => mipEmphasis = value; }
        public double? FeasibilityTol { get => epRHS;       set => epRHS       = value; }
        public double? OptimalityTol  { get => epOpt;       set => epOpt       = value; }
        public double? MemoryLimitMb  { get => workMemory;  set => workMemory  = value; }  // CPLEX WorkMem 單位為 MB

        // Presolve（int? on/off）↔ PreIndicator（bool?）：0=off、非0=on
        public int? Presolve
        {
            get => PreIndicator.HasValue ? (PreIndicator.Value ? 1 : 0) : (int?)null;
            set => PreIndicator = value.HasValue ? value.Value != 0 : (bool?)null;
        }

        // CPLEX 無直接對應欄位，保留為獨立旋鈕（Configuration 不套用，僅供快照記錄）
        public double? HeuristicEffort { get; set; }
    }
}
