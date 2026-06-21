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

        // ── 高影響 tuning 旋鈕（camelCase，CPLEX 專屬；null = 用 CPLEX 預設） ──────────
        // General / 決定論 / 計時
        public int? parallelMode = null;  // IntParam.Parallel：-1 機會式, 0 自動, 1 決定論
        public double? detTimeLimit = null;  // DoubleParam.DetTiLim：決定論時間上限（ticks），實驗可重現
        public int? clockType = null;  // IntParam.ClockType：1 CPU, 2 wall-clock
        public bool? numericalEmphasis = null;  // BooleanParam.NumericalEmphasis：數值穩定優先

        // 容差（MIP）
        public double? epInt = null;  // DoubleParam.EpInt：整數容差
        public double? epAGap = null;  // DoubleParam.EpAGap：絕對 MIP gap

        // MIP limits
        public long? nodeLimit = null;  // LongParam.NodeLim：B&B 節點上限
        public double? treeMemoryLimit = null;  // DoubleParam.TreLim：搜尋樹記憶體上限 (MB)
        public long? intSolLimit = null;  // LongParam.IntSolLim：找到 N 個整數解即停

        // MIP strategy
        public int? probe = null;  // IntParam.Probe：-1..3 變數探測強度
        public long? rinsHeur = null;  // LongParam.RINSHeur：RINS 啟發式頻率（-1 關閉, 0 自動, N 每 N 節點）
        public int? mipSearch = null;  // IntParam.MIPSearch：0 自動, 1 傳統 B&C, 2 動態 B&C
        public int? diveType = null;  // IntParam.DiveType：0 自動, 1 傳統, 2 探測, 3 引導
        public int? branchDir = null;  // IntParam.BrDir：-1 向下, 0 自動, 1 向上

        // MIP cuts（每族 -1 關閉 / 0 自動 / 1..3 漸積極；CutsFactor、CutPass 控制總量）
        public double? cutsFactor = null;  // DoubleParam.CutsFactor：cut 數量上限倍數
        public long? cutPasses = null;  // LongParam.CutPass：cut 生成回合數（-1 無, 0 自動, N 上限）
        public int? gomoryCuts = null;  // IntParam.FracCuts：Gomory fractional cuts
        public int? coverCuts = null;  // IntParam.Covers
        public int? cliqueCuts = null;  // IntParam.Cliques
        public int? mirCuts = null;  // IntParam.MIRCuts：mixed-integer rounding
        public int? flowCoverCuts = null;  // IntParam.FlowCovers

        // 純 LP（Simplex / Barrier）
        public long? simplexIterLimit = null;  // LongParam.ItLim：simplex 迭代上限
        public int? barrierAlgorithm = null;  // IntParam.BarAlg：barrier 演算法

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
        public int? Seed { get => randomSeed; set => randomSeed = value; }
        public int? Emphasis { get => mipEmphasis; set => mipEmphasis = value; }
        public double? FeasibilityTol { get => epRHS; set => epRHS = value; }
        public double? OptimalityTol { get => epOpt; set => epOpt = value; }
        public double? MemoryLimitMb { get => workMemory; set => workMemory = value; }  // CPLEX WorkMem 單位為 MB

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
