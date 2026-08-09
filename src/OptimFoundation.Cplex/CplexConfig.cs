using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// CPLEX 求解器設定。跨 solver 與 CPLEX 專屬參數都只暴露一個 PascalCase property。
    /// </summary>
    public sealed class CplexConfig : ISolverConfig, ITunableConfig
    {
        /// <summary>Creates a shallow copy containing every current public setting.</summary>
        public CplexConfig Clone() => (CplexConfig)MemberwiseClone();
        // ── CPLEX 參數（單一公開入口） ──────────────────────
        // null = 不呼叫 SetParam，交給 CPLEX 自己的預設值；有值才會在 Configuration() 內套用並寫進 log

        /// <summary>Param.Threads：求解可用的工作執行緒上限。</summary>
        public int? Threads { get; set; } = 32;

        /// <summary>Param.Read.Constraints：讀檔時的限制式數量上限。</summary>
        public int? RowRead { get; set; } = 30000;

        /// <summary>IntParam.WorkMem：求解可用的工作記憶體 (MB)。設定時會一併把 NodeFileInd 設為 0（節點資訊只留記憶體）。</summary>
        public double? MemoryLimitMb { get; set; } = 2048;

        /// <summary>Param.MIP.Tolerances.MIPGap：相對 MIP gap，達到即視為收斂停止。1e-4 = 0.01%。</summary>
        public double? MipGap { get; set; } = 1e-4;

        /// <summary>Param.MIP.Strategy.NodeSelect：節點選擇策略。0 深度優先 / 2 最佳估計 / 3 交替最佳估計 / 其他 最佳界限（預設）。</summary>
        public int? NodeSelect { get; set; }

        /// <summary>Param.RandomSeed：隨機種子。要重現實驗結果時 ALWAYS 固定它（並搭配 ParallelMode = 1）。</summary>
        public int? Seed { get; set; }

        /// <summary>Param.Simplex.Tolerances.Optimality：最佳性容差（reduced cost 判定門檻）。</summary>
        public double? OptimalityTol { get; set; } = 1e-06;

        /// <summary>Param.Simplex.Tolerances.Feasibility：可行性容差（限制式違反量的容忍門檻）。</summary>
        public double? FeasibilityTol { get; set; } = 1e-06;

        /// <summary>Param.TimeLimit：求解逾時秒數（wall-clock）。逾時會回傳當下最好的可行解，不是失敗。</summary>
        public double? TimeLimit { get; set; }

        /// <summary>Param.MIP.PolishAfter.Time：求解滿幾秒後轉入 solution polishing（放棄證明最佳、專心改善現有解）。</summary>
        public double? PolishAfterTime { get; set; }

        /// <summary>Param.Emphasis.MIP：求解重點。1 重可行解 / 2 重最佳解 / 3 重最佳界限 / 4 找隱藏可行解 / 其他 平衡（預設）。</summary>
        public int? Emphasis { get; set; }

        /// <summary>Param.MIP.Strategy.VariableSelect：分支變數選擇。-1 最小可行 / 1 最大可行 / 2 假定成本 / 3 強分支 / 4 假定降低成本 / 其他 自動。</summary>
        public int? VariableSelect { get; set; }

        /// <summary>IntParam.RootAlgorithm：根節點 LP 演算法。1 primal / 2 dual / 3 network / 4 barrier / 5 sifting / 6 concurrent / 其他 自動。</summary>
        public int? RootAlgorithm { get; set; }

        /// <summary>Param.MIP.Strategy.File：節點資訊存放方式。0 不儲存 / 2 存磁碟 / 3 壓縮存磁碟 / 其他 壓縮存記憶體（預設）。</summary>
        public int? NodeFileStrategy { get; set; }

        // ── 高影響 tuning 控制項目（CPLEX 專屬；null = 用 CPLEX 預設） ──────────
        // General / 決定論 / 計時

        /// <summary>IntParam.Parallel：-1 機會式, 0 自動, 1 決定論。要重現實驗結果設 1。</summary>
        public int? ParallelMode { get; set; }

        /// <summary>DoubleParam.DetTiLim：決定論時間上限（ticks）。與 TimeLimit 不同，同一模型每次跑的停點一致。</summary>
        public double? DeterministicTimeLimit { get; set; }

        /// <summary>IntParam.ClockType：計時基準。1 CPU time, 2 wall-clock。</summary>
        public int? ClockType { get; set; }

        /// <summary>BooleanParam.NumericalEmphasis：數值穩定優先（犧牲速度換精度），係數量級差距大時可開。</summary>
        public bool? NumericalEmphasis { get; set; }

        // 容差（MIP）

        /// <summary>DoubleParam.EpInt：整數容差，判定整數變數是否為整數的門檻。</summary>
        public double? IntegralityTolerance { get; set; }

        /// <summary>DoubleParam.EpAGap：絕對 MIP gap 收斂門檻（MipGap 是相對版）。</summary>
        public double? AbsoluteMipGap { get; set; }

        // MIP limits

        /// <summary>LongParam.NodeLim：B&amp;B 節點數上限，達到即停。</summary>
        public long? NodeLimit { get; set; }

        /// <summary>DoubleParam.TreLim：搜尋樹記憶體上限 (MB)，超過即停或轉存節點檔。</summary>
        public double? TreeMemoryLimitMb { get; set; }

        /// <summary>LongParam.IntSolLim：找到 N 個整數解即停（要快速拿可行解時用）。</summary>
        public long? IntegerSolutionLimit { get; set; }

        // MIP strategy

        /// <summary>IntParam.Probe：變數探測強度 -1..3，愈大前處理花愈久但可能大幅縮小問題。</summary>
        public int? Probe { get; set; }

        /// <summary>LongParam.RINSHeur：RINS 啟發式頻率（-1 關閉, 0 自動, N 每 N 節點跑一次）。</summary>
        public long? RinsHeuristicFrequency { get; set; }

        /// <summary>IntParam.MIPSearch：0 自動, 1 傳統 B&amp;C, 2 動態搜尋。掛 callback 時會被強制為 1。</summary>
        public int? MipSearch { get; set; }

        /// <summary>IntParam.DiveType：下潛策略。0 自動, 1 傳統, 2 探測, 3 引導。</summary>
        public int? DiveType { get; set; }

        /// <summary>IntParam.BrDir：分支方向。-1 先向下, 0 自動, 1 先向上。</summary>
        public int? BranchDirection { get; set; }

        // MIP cuts（每族 -1 關閉 / 0 自動 / 1..3 漸積極；CutsFactor、CutPass 控制總量）

        /// <summary>DoubleParam.CutsFactor：cut 總數上限倍數（相對於原始列數）。</summary>
        public double? CutsFactor { get; set; }

        /// <summary>LongParam.CutPass：cut 生成回合數（-1 不生成, 0 自動, N 上限）。</summary>
        public long? CutPasses { get; set; }

        /// <summary>IntParam.FracCuts：Gomory fractional cuts 強度。</summary>
        public int? GomoryCuts { get; set; }

        /// <summary>IntParam.Covers：cover cuts 強度。</summary>
        public int? CoverCuts { get; set; }

        /// <summary>IntParam.Cliques：clique cuts 強度。</summary>
        public int? CliqueCuts { get; set; }

        /// <summary>IntParam.MIRCuts：mixed-integer rounding cuts 強度。</summary>
        public int? MirCuts { get; set; }

        /// <summary>IntParam.FlowCovers：flow cover cuts 強度。</summary>
        public int? FlowCoverCuts { get; set; }

        // 純 LP（Simplex / Barrier）

        /// <summary>LongParam.ItLim：simplex 迭代次數上限。</summary>
        public long? SimplexIterationLimit { get; set; }

        /// <summary>IntParam.BarAlg：barrier 演算法選擇。</summary>
        public int? BarrierAlgorithm { get; set; }

        // CPLEX 無直接對應的 ISolverConfig 延伸項

        /// <summary>IntParam.NodeAlg：子問題（非根節點）的 LP 演算法，取值同 <see cref="RootAlgorithm"/>。</summary>
        public int? NodeAlgorithm { get; set; }

        /// <summary>Param.Preprocessing.Presolve：是否啟用前處理。infeasible 找不出原因時可關掉它再跑 IIS。</summary>
        public bool? PreIndicator { get; set; }

        /// <summary>Param.Preprocessing.Symmetry：-1 = auto，0 = off，1..5 = 逐步提高對稱破除強度。</summary>
        public int? Symmetry { get; set; }

        /// <summary>
        /// ITunableConfig 介面名，是 <see cref="PreIndicator"/> 的 int 視角：0 = off、非 0 = on、null = 用 CPLEX 預設。
        /// </summary>
        public int? Presolve
        {
            get => PreIndicator.HasValue ? (PreIndicator.Value ? 1 : 0) : (int?)null;
            set => PreIndicator = value.HasValue ? value.Value != 0 : (bool?)null;
        }

        /// <summary>
        /// Param.MIP.Strategy.HeuristicEffort：啟發式投入程度（0 = 關閉，1 = 預設，&gt;1 = 更積極找可行解）。
        /// 沒有對應的 camelCase 欄位，本屬性自己就是單一來源。
        /// </summary>
        public double? HeuristicEffort { get; set; }
    }
}
