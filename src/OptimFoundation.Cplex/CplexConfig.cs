using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>CPLEX 22.1.1 求解器參數；所有屬性預設 null，LoadConfig 只套用有值的設定。</summary>
    /// <remarks>
    /// null 不列入實驗設定紀錄；比較搜尋策略時，固定停止條件與執行資源。
    /// 設定 MemoryLimitMb 會停用節點檔；需壓縮或寫入磁碟時，同時指定 NodeFileStrategy 覆寫此行為。
    /// 各屬性的 Param 路徑、值域與預設值對應 IBM Parameters Reference。
    /// </remarks>
    public sealed class CplexConfig : ISolverConfig
    {
        /// <summary>複製所有設定至新物件。</summary>
        public CplexConfig Clone() => (CplexConfig)MemberwiseClone();

        #region 停止條件（27）

        /// <summary>
        /// <c>Param.MIP.Tolerances.AbsMIPGap</c>：可接受的絕對 MIP gap；最佳可行解與最佳界限差距達到此門檻時可停止。
        /// <para>值：非負數；預設 1e-06.</para>
        /// </summary>
        public double? AbsoluteMipGap { get; set; }

        /// <summary>
        /// <c>Param.Barrier.ConvergeTol</c>：barrier 解 LP / QP 時，用來判定已收斂的容許誤差。
        /// <para>值：至少 1e-12 的正數；預設 1e-8.</para>
        /// </summary>
        public double? BarrierConvergeTol { get; set; }

        /// <summary>
        /// <c>Param.Barrier.Limits.Iteration</c>：barrier 迭代次數上限。
        /// <para>值：0 = 不執行 barrier 迭代｜9223372036800000000 = 預設｜正整數 = 停止前最多執行的迭代次數</para>
        /// </summary>
        public long? BarrierIterationLimit { get; set; }

        /// <summary>
        /// <c>Param.Barrier.QCPConvergeTol</c>：barrier 解 QCP 時，用來判定已收斂的容許誤差。
        /// <para>值：至少 1e-12 的正數；預設 1e-7。LP 與所有限制式均為線性的 QP 改用 BarrierConvergeTol。</para>
        /// </summary>
        public double? BarrierQcpConvergeTol { get; set; }

        /// <summary>
        /// <c>Param.DetTimeLimit</c>：以 CPLEX 的計算工作量（ticks）限制求解長度，不以實際秒數計算。
        /// </summary>
        public double? DeterministicTimeLimit { get; set; }

        /// <summary>
        /// <c>Param.Feasopt.Tolerance</c>：FeasOpt 判定限制式已充分放寬時使用的容許誤差。
        /// <para>值：非負數；預設 1e-6.</para>
        /// </summary>
        public double? FeasOptRelaxTolerance { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Tolerances.Feasibility</c>：simplex 判斷解是否滿足限制式時，允許的數值誤差。
        /// </summary>
        public double? FeasibilityTol { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.Solutions</c>：找到 N 個整數解即停。
        /// <para>值：正整數；預設 9223372036800000000。設 0 會被拒（Error 1014）。</para>
        /// </summary>
        public long? IntegerSolutionLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.Integrality</c>：整數容差：離整數多近算整數。
        /// </summary>
        public double? IntegralityTolerance { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.Linearization</c>：QP / MIQP 線性化時使用的微小誤差門檻（epsilon）。
        /// </summary>
        public double? LinearizationTolerance { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.LowerCutoff</c>：最大化問題的目標值下限；CPLEX 可排除無法優於此值的搜尋分支。
        /// </summary>
        public double? LowerCutoff { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.LowerObjStop</c>：目標值低於此值即停止（最小化問題的早停門檻）。
        /// </summary>
        public double? LowerObjectiveStop { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.MIPGap</c>：可接受的相對 MIP gap；最佳可行解與最佳界限的相對差距達到門檻時可停止。
        /// <para>值：0.0 到 1.0；預設 1e-04.</para>
        /// </summary>
        public double? MipGap { get; set; }

        /// <summary>
        /// <c>Param.Network.Tolerances.Feasibility</c>：network simplex 判斷解是否滿足限制式時，允許的數值誤差。
        /// <para>值：1e-11 到 1e-1；預設 1e-6.</para>
        /// </summary>
        public double? NetworkFeasibilityTol { get; set; }

        /// <summary>
        /// <c>Param.Network.Iterations</c>：network simplex 迭代次數上限。
        /// </summary>
        public long? NetworkIterationLimit { get; set; }

        /// <summary>
        /// <c>Param.Network.Tolerances.Optimality</c>：network simplex 判斷解是否已達最佳時，允許的數值誤差。
        /// <para>值：1e-11 到 1e-1；預設 1e-6.</para>
        /// </summary>
        public double? NetworkOptimalityTol { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.Nodes</c>：分支定界搜尋（B&amp;B）最多處理的節點數。
        /// <para>值：非負整數；預設 9223372036800000000.</para>
        /// </summary>
        public long? NodeLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.ObjDifference</c>：新可行解的目標值至少要比目前最佳解改善多少，才接受為新的最佳解。
        /// <para>值：任意數值；預設 0.0.</para>
        /// </summary>
        public double? ObjectiveDifference { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Tolerances.Optimality</c>：simplex 以 reduced cost 判斷解是否已達最佳時，允許的數值誤差。
        /// <para>值：1e-9 到 1e-1；預設 1e-06.</para>
        /// </summary>
        public double? OptimalityTol { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.RelObjDifference</c>：接受新的最佳可行解時，目標值至少要改善的相對比例。
        /// <para>值：0.0 到 1.0；預設 0.0.</para>
        /// </summary>
        public double? RelativeObjectiveDifference { get; set; }

        /// <summary>
        /// <c>Param.Sifting.Iterations</c>：sifting 迭代次數上限。
        /// <para>值：非負整數；預設 9223372036800000000.</para>
        /// </summary>
        public long? SiftingIterationLimit { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Limits.Iterations</c>：simplex 迭代次數上限。
        /// <para>值：非負整數；預設 9223372036800000000.</para>
        /// </summary>
        public long? SimplexIterationLimit { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Limits.LowerObj</c>：純 LP：目標值低於此值即停止。
        /// <para>值：任意數值；預設 -1e+75.</para>
        /// </summary>
        public double? SimplexLowerObjectiveLimit { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Limits.UpperObj</c>：純 LP：目標值高於此值即停止。
        /// <para>值：任意數值；預設 1e+75.</para>
        /// </summary>
        public double? SimplexUpperObjectiveLimit { get; set; }

        /// <summary>
        /// <c>Param.TimeLimit</c>：求解時間上限，單位為秒；計時方式由 ClockType 決定。
        /// <para>值：非負秒數；預設 1e+75.</para>
        /// </summary>
        public double? TimeLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.UpperCutoff</c>：上界剪枝：已知最小化問題的解不會大於此值時填入，直接砍掉更差的分支。
        /// </summary>
        public double? UpperCutoff { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.UpperObjStop</c>：目標值超過此值即停止（最大化問題的早停門檻）。
        /// </summary>
        public double? UpperObjectiveStop { get; set; }

        #endregion

        #region 執行資源（9）

        /// <summary>
        /// <c>Param.MIP.Limits.AuxRootThreads</c>：根節點的輔助工作最多可使用多少執行緒。
        /// <para>值：-1 = 不使用額外執行緒｜0 = 由 CPLEX 決定（預設）｜N &gt; n &gt; 0 = 使用 n 個執行緒，N 為可用執行緒總數</para>
        /// </summary>
        public int? AuxiliaryRootThreads { get; set; }

        /// <summary>
        /// <c>Param.CPUmask</c>：將執行緒固定在指定 CPU 核心，減少執行緒排程差異對測量的影響。
        /// <para>值："off" = 不固定核心｜"auto" = 由 CPLEX 決定（預設）｜由 0-9、a-f、A-F 組成的遮罩字串 = 執行緒輪流綁定到遮罩指定的核心</para>
        /// <para>僅適用支援 CPU 綁定的平台；不支援時含 "auto" 在內的值皆可能觸發 Error 1811，請留 null。</para>
        /// </summary>
        public string CpuMask { get; set; }

        /// <summary>
        /// <c>Param.Emphasis.Memory</c>：記憶體節約模式：犧牲速度換記憶體。
        /// <para>值：0 = 不特別節省記憶體（預設）｜1 = 盡可能節省記憶體</para>
        /// </summary>
        public bool? MemoryEmphasis { get; set; }

        /// <summary>
        /// <c>Param.WorkMem</c>：CPLEX 工作記憶體大小（MB），主要控制搜尋樹使用的記憶體，不是整個行程的總上限。
        /// </summary>
        public double? MemoryLimitMb { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.File</c>：搜尋樹超過工作記憶體大小時，節點資料要如何壓縮或存到磁碟。
        /// </summary>
        public int? NodeFileStrategy { get; set; }

        /// <summary>
        /// <c>Param.Parallel</c>：平行搜尋模式，決定是否要求搜尋順序可重現。
        /// <para>值：-1 = 允許執行緒時序影響搜尋順序｜0 = 由 CPLEX 選擇模式（預設）｜1 = 使用可重現的平行搜尋順序</para>
        /// </summary>
        public int? ParallelMode { get; set; }

        /// <summary>
        /// <c>Param.Threads</c>：求解可用的工作執行緒上限。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = 單執行緒｜N = 最多使用 N 個執行緒，仍受處理器與授權 Processor Value Units（PVU）限制</para>
        /// </summary>
        public int? Threads { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.TreeMemory</c>：分支定界搜尋樹（B&amp;B）的記憶體上限，單位為 MB。
        /// <para>值：非負數；預設 1e+75.</para>
        /// </summary>
        public double? TreeMemoryLimitMb { get; set; }

        /// <summary>
        /// <c>Param.WorkDir</c>：節點檔等暫存檔的目錄；NodeFileStrategy 設 2/3 時才有意義。
        /// <para>值：已存在的目錄；預設為目前目錄「.」。</para>
        /// </summary>
        public string WorkDir { get; set; }

        #endregion

        #region 重複量測（2）

        /// <summary>
        /// <c>Param.ClockType</c>：求解計時方式：1 使用 CPU 時間，2 使用實際經過時間。
        /// </summary>
        public int? ClockType { get; set; }

        /// <summary>
        /// <c>Param.RandomSeed</c>：控制隨機選擇的種子；用不同種子重測同一組設定，不以種子值評選最佳搜尋策略。
        /// <para>值：0 到 BIGINT 的整數；CPLEX 預設種子可能隨版本改變。實際上限見下方說明。</para>
        /// <para>上限 2100000000；超過時觸發 Error 1015。</para>
        /// </summary>
        public int? Seed { get; set; }

        #endregion

        #region 搜尋策略 · emphasis 與搜尋分支（18）

        /// <summary>
        /// <c>Param.Advance</c>：是否使用已有的基底（basis）或起始解，以及如何將它們套用到前處理後的模型。
        /// <para>值：0 = 不使用起始資訊｜1 = 使用使用者提供的基底（預設）｜2 = 將提供的基底或起始向量轉換成前處理後模型可用的形式</para>
        /// </summary>
        public int? AdvancedStart { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.Backtrack</c>：回溯容差：越小越傾向繼續往深處走。
        /// <para>值：0.0 到 1.0；預設 0.9999</para>
        /// </summary>
        public double? BacktrackTolerance { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.BBInterval</c>：以估計值選節點時，每隔多少個節點改選一次最佳界限節點。
        /// <para>值：0 = 一律選最佳估計值節點｜1 = 一律選最佳界限節點｜7 = 偶爾選最佳界限節點（預設）｜正整數 = 選最佳界限節點的間隔</para>
        /// </summary>
        public long? BestBoundInterval { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.Branch</c>：分支方向：先試哪一邊。
        /// </summary>
        public int? BranchDirection { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.Dive</c>：選擇連續往搜尋樹深處尋找可行解的方式（dive）。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = 傳統 dive｜2 = 使用 probing 的 dive｜3 = 使用引導資訊的 dive</para>
        /// </summary>
        public int? DiveType { get; set; }

        /// <summary>
        /// <c>Param.Emphasis.MIP</c>：決定搜尋重點：找可行解、證明最佳性，或改善最佳界限。
        /// <para>值：0 = 平衡找可行解與證明最佳性（預設）｜1 = 優先找可行解｜2 = 優先證明最佳性｜3 = 優先改善最佳界限｜4 = 加強尋找不易找到的可行解｜5 = 優先提早找到品質好的可行解</para>
        /// </summary>
        public int? Emphasis { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.KappaStats</c>：計算 MIP 的條件數（kappa）統計，用來檢查數值計算是否穩定。
        /// <para>值：–1 = 不計算 MIP kappa｜0 = 由 CPLEX 決定（預設）｜1 = 抽樣部分子問題計算｜2 = 計算所有子問題</para>
        /// </summary>
        public int? KappaStatistics { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.Search</c>：選擇 dynamic search 或傳統的分支與切割平面搜尋（branch-and-cut）。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = 傳統 branch-and-cut，關閉 dynamic search｜2 = dynamic search</para>
        /// </summary>
        public int? MipSearch { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.NodeSelect</c>：決定下一個要處理的搜尋樹節點。
        /// <para>值：0 = 深度優先｜1 = 最佳界限優先（預設）｜2 = 最佳估計值優先｜3 = 另一種最佳估計值搜尋</para>
        /// </summary>
        public int? NodeSelect { get; set; }

        /// <summary>
        /// <c>Param.Emphasis.Numerical</c>：數值穩定優先（犧牲速度換精度）。
        /// <para>值：0 = 不特別加強數值精度（預設）｜1 = 採用更謹慎的數值計算</para>
        /// </summary>
        public bool? NumericalEmphasis { get; set; }

        /// <summary>
        /// <c>Param.OptimalityTarget</c>：決定 QP 要找全域最佳解，還是只要求滿足一階最佳性條件。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = 求凸模型的全域最佳解｜2 = 求滿足一階最佳性條件的解，不保證全域最佳｜3 = 求非凸模型的全域最佳解，必要時將問題轉成 MIQP</para>
        /// <para><b>值 2（只要求一階最佳性條件）不能用在 MIP</b>，會丟 <c>Error 1017: Not available for mixed-integer problems</c>。</para>
        /// </summary>
        public int? OptimalityTarget { get; set; }

        /// <summary>
        /// <c>Param.MIP.OrderType</c>：未提供分支優先序時，CPLEX 要依哪個規則產生優先序。
        /// <para>值：0 = 不自動產生優先序｜1 = 目標係數由大到小｜2 = 上下界範圍由小到大｜3 = 目標係數除以非零係數數量後，由小到大</para>
        /// </summary>
        public int? PriorityOrderType { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.Probe</c>：求解前試探變數值、推導界限的強度。
        /// <para>值：-1 = 不做 probing｜0 = 由 CPLEX 決定（預設）｜1 = 一般強度｜2 = 加強｜3 = 大幅加強</para>
        /// </summary>
        public int? Probe { get; set; }

        /// <summary>
        /// <c>Param.SolutionType</c>：指定 LP / QP 要回傳基底解，還是原始與對偶解向量。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = 計算基底解｜2 = 計算原始與對偶解向量</para>
        /// </summary>
        public int? SolutionType { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.StrongCand</c>：強分支（strong branching）每次最多評估多少個候選變數。
        /// <para>值：正整數；預設 10。設 0 會被拒（Error 1014）。</para>
        /// </summary>
        public int? StrongBranchingCandidateLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.StrongIt</c>：強分支評估每個候選變數時，最多執行多少次 simplex 迭代。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜正整數 = 每個候選變數的 simplex 迭代上限</para>
        /// </summary>
        public long? StrongBranchingIterationLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.Order</c>：是否套用分支優先序。
        /// <para>值：0 = 不使用優先序｜1 = 有提供優先序時就使用（預設）</para>
        /// </summary>
        public bool? UsePriorityOrder { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.VariableSelect</c>：決定分支時要選哪個變數。
        /// <para>值：-1 = 選距離整數最近的非整數變數｜0 = 由 CPLEX 決定（預設）｜1 = 選距離整數最遠的變數｜2 = 依 pseudo cost｜3 = 強分支｜4 = 依 pseudo reduced cost</para>
        /// </summary>
        public int? VariableSelect { get; set; }

        #endregion

        #region 搜尋策略 · 啟發式與 solution polishing（13）

        /// <summary>
        /// <c>Param.MIP.Strategy.CardLs</c>：控制針對基數限制式的區域搜尋，用來尋找或改善可行解。
        /// <para>值：-1 = 不使用基數限制式區域搜尋（CLSH，預設）｜0 = 由 CPLEX 決定｜1 = 只在根節點使用｜2 = 在分支定界樹的節點使用</para>
        /// </summary>
        public int? CardinalityLocalSearch { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.FPHeur</c>：控制 feasibility pump 啟發式，協助找出第一個可行解。
        /// <para>值：-1 = 不使用 feasibility pump｜0 = 由 CPLEX 決定（預設）｜1 = 優先找出可行解｜2 = 優先找目標值較好的可行解</para>
        /// </summary>
        public int? FeasibilityPumpHeuristic { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.HeuristicEffort</c>：尋找可行解的啟發式方法要投入多少計算：0 關閉、1 預設、&gt;1 增加投入。
        /// <para>值：0 = 關閉啟發式方法｜&lt;1 = 減少投入｜1 = 預設｜&gt;1 = 增加投入</para>
        /// </summary>
        public double? HeuristicEffort { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.HeuristicFreq</c>：週期性啟發式的頻率：每 N 個節點跑一次。
        /// <para>值：-1 = 不使用週期性啟發式｜0 = 由 CPLEX 決定（預設）｜正整數 = 每隔這麼多節點執行一次</para>
        /// </summary>
        public long? HeuristicFrequency { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.LBHeur</c>：每找到新的最佳可行解，就在它附近搜尋是否有更好的解（local branching）。
        /// <para>值：0 = 關閉（預設）｜1 = 對新找到的最佳可行解執行 local branching</para>
        /// </summary>
        public bool? LocalBranchingHeuristic { get; set; }

        /// <summary>
        /// <c>Param.MIP.PolishAfter.AbsMIPGap</c>：絕對 MIP gap 降到多少後，開始集中改善已知可行解（solution polishing）。
        /// <para>值：非負數；預設 0.0.</para>
        /// </summary>
        public double? PolishAfterAbsoluteMipGap { get; set; }

        /// <summary>
        /// <c>Param.MIP.PolishAfter.DetTime</c>：累積多少計算工作量（ticks）後，開始集中改善已知可行解。
        /// <para>值：非負數，單位為 deterministic ticks；預設 1.0E+75 ticks。</para>
        /// </summary>
        public double? PolishAfterDetTime { get; set; }

        /// <summary>
        /// <c>Param.MIP.PolishAfter.MIPGap</c>：相對 MIP gap 降到多少後，開始集中改善已知可行解。
        /// <para>值：0.0 到 1.0；預設 0.0.</para>
        /// </summary>
        public double? PolishAfterMipGap { get; set; }

        /// <summary>
        /// <c>Param.MIP.PolishAfter.Nodes</c>：處理多少個節點後，開始集中改善已知可行解。
        /// <para>值：非負整數；預設 9223372036800000000</para>
        /// </summary>
        public long? PolishAfterNodes { get; set; }

        /// <summary>
        /// <c>Param.MIP.PolishAfter.Solutions</c>：找到多少個整數可行解後，開始集中改善已知可行解。
        /// <para>值：正整數；預設 9223372036800000000。設 0 會被拒（Error 1014）。</para>
        /// </summary>
        public long? PolishAfterSolutions { get; set; }

        /// <summary>
        /// <c>Param.MIP.PolishAfter.Time</c>：求解經過多少秒後，開始集中改善已知可行解（solution polishing）。
        /// <para>值：非負秒數；預設 1.0E+75 秒。</para>
        /// </summary>
        public double? PolishAfterTime { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.RepairTries</c>：MIP start 不可行時嘗試修復幾次。
        /// <para>值：-1 = 不嘗試修復｜0 = 由 CPLEX 決定（預設）｜正整數 = 最多嘗試次數</para>
        /// </summary>
        public long? RepairTries { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.RINSHeur</c>：每隔多少個節點執行一次 RINS 啟發式，在鬆弛解與已知可行解附近尋找更好的解。
        /// <para>值：-1 = 不使用 RINS｜0 = 由 CPLEX 決定（預設）｜正整數 = 每隔這麼多節點執行一次 RINS</para>
        /// </summary>
        public long? RinsHeuristicFrequency { get; set; }

        #endregion

        #region 搜尋策略 · 切割平面（22）

        /// <summary>
        /// <c>Param.MIP.Limits.AggForCut</c>：產生切割平面（cut）時，最多可合併多少條限制式。
        /// <para>值：非負整數；預設 3</para>
        /// </summary>
        public int? AggregationLimitForCut { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.BQP</c>：控制 Boolean Quadric Polytope 切割平面的產生強度，適用於非凸 QP / MIQP。
        /// </summary>
        public int? BqpCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.Cliques</c>：控制 clique 切割平面的產生強度，用來收緊互斥選擇的限制。
        /// </summary>
        public int? CliqueCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.Covers</c>：控制 cover 切割平面的產生強度。
        /// <para>值：-1 = 關閉｜0 = 由 CPLEX 決定（預設）｜1 = 一般強度｜2 = 高強度｜3 = 最高強度</para>
        /// </summary>
        public int? CoverCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.CutPasses</c>：最多執行多少輪切割平面生成。
        /// <para>值：-1 = 不產生 cuts｜0 = 由 CPLEX 決定（預設）｜正整數 = 最多執行輪數</para>
        /// </summary>
        public long? CutPasses { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.CutsFactor</c>：限制切割平面總數；以原始限制式數量的倍數表示。
        /// </summary>
        public double? CutsFactor { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.Disjunctive</c>：控制 disjunctive 切割平面的產生強度。
        /// <para>值：-1 = 關閉｜0 = 由 CPLEX 決定（預設）｜1 = 一般強度｜2 = 高強度｜3 = 最高強度</para>
        /// </summary>
        public int? DisjunctiveCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.EachCutLimit</c>：每一類切割平面各自的數量上限。
        /// <para>值：0 = 不產生 cuts｜正數 = 每類 cut 的數量上限｜2100000000 = 預設</para>
        /// </summary>
        public int? EachCutLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.FlowCovers</c>：控制 flow cover 切割平面的產生強度。
        /// <para>值：-1 = 關閉｜0 = 由 CPLEX 決定（預設）｜1 = 一般強度｜2 = 高強度</para>
        /// </summary>
        public int? FlowCoverCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.PathCut</c>：控制 flow path 切割平面的產生強度。
        /// <para>值：-1 = 關閉｜0 = 由 CPLEX 決定（預設）｜1 = 一般強度｜2 = 高強度</para>
        /// </summary>
        public int? FlowPathCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.GomoryCand</c>：產生 Gomory 切割平面時，最多考慮多少個候選。
        /// <para>值：正整數；預設 200。設 0 會被拒（Error 1014）。</para>
        /// </summary>
        public int? GomoryCandidateLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.Gomory</c>：控制 Gomory fractional 切割平面的產生強度。
        /// <para>值：-1 = 關閉｜0 = 由 CPLEX 決定（預設）｜1 = 一般強度｜2 = 高強度</para>
        /// </summary>
        public int? GomoryCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.GomoryPass</c>：最多執行多少輪 Gomory 切割平面生成。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜正整數 = 產生 Gomory fractional cuts 的輪數</para>
        /// </summary>
        public long? GomoryPassLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.GUBCovers</c>：控制 GUB cover 切割平面的產生強度，適用於一組二元變數至多選一個等結構。
        /// <para>值：-1 = 關閉｜0 = 由 CPLEX 決定（預設）｜1 = 一般強度｜2 = 高強度</para>
        /// </summary>
        public int? GubCoverCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.Implied</c>：控制全模型有效的隱含界限切割平面，可用於 big-M / indicator 結構。
        /// <para>值：-1 = 關閉｜0 = 由 CPLEX 決定（預設）｜1 = 一般強度｜2 = 高強度</para>
        /// </summary>
        public int? ImpliedBoundCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.LiftProj</c>：控制 lift-and-project 切割平面的產生強度。
        /// <para>值：-1 = 關閉｜0 = 由 CPLEX 決定（預設）｜1 = 一般強度｜2 = 高強度｜3 = 最高強度</para>
        /// </summary>
        public int? LiftAndProjectCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.LocalImplied</c>：控制只在目前子樹有效的隱含界限切割平面。
        /// <para>值：-1 = 關閉｜0 = 由 CPLEX 決定（預設）｜1 = 一般強度｜2 = 高強度｜3 = 最高強度</para>
        /// </summary>
        public int? LocalImpliedBoundCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.MCFCut</c>：控制多商品網路流（multi-commodity flow）切割平面的產生強度。
        /// <para>值：-1 = 關閉｜0 = 自動（預設）｜1 = 一般強度｜2 = 高強度</para>
        /// </summary>
        public int? McfCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.MIRCut</c>：控制混合整數捨入（mixed-integer rounding）切割平面的產生強度。
        /// </summary>
        public int? MirCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.Nodecuts</c>：控制根節點以外的節點是否繼續產生切割平面，以及產生強度。
        /// <para>值：-1 = 關閉｜0 = 由 CPLEX 決定（預設）｜1 = 一般強度｜2 = 高強度｜3 = 最高強度</para>
        /// </summary>
        public int? NodeCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.RLT</c>：控制 RLT（Reformulation Linearization Technique）切割平面的產生強度，適用於非凸 QP / MIQP。
        /// </summary>
        public int? RltCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.ZeroHalfCut</c>：控制 zero-half 切割平面的產生強度，可用於純二元變數問題。
        /// <para>值：-1 = 關閉｜0 = 由 CPLEX 決定（預設）｜1 = 一般強度｜2 = 高強度</para>
        /// </summary>
        public int? ZeroHalfCuts { get; set; }

        #endregion

        #region 搜尋策略 · 前處理（16）

        /// <summary>
        /// <c>Param.Preprocessing.Fill</c>：前處理合併限制式時，允許增加多少非零係數（fill-in）。
        /// <para>值：非負整數；預設 10</para>
        /// </summary>
        public long? AggregatorFill { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Aggregator</c>：前處理最多執行多少次限制式合併。
        /// <para>值：-1 = 自動，LP 為 1 次、MIP 不限次數（預設）｜0 = 不合併限制式｜正整數 = 最多套用次數</para>
        /// </summary>
        public int? AggregatorLimit { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.BoundStrength</c>：控制前處理是否利用限制式收緊變數的上下界。
        /// </summary>
        public int? BoundStrengthening { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.CoeffReduce</c>：控制前處理的係數縮減，會影響連續鬆弛問題的範圍。
        /// </summary>
        public int? CoefficientReduction { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Dependency</c>：偵測並移除可由其他限制式推得的相依限制式。
        /// <para>值：-1 = 由 CPLEX 決定（預設）｜0 = 不檢查｜1 = 只在前處理開始時檢查｜2 = 只在前處理結束時檢查｜3 = 開始與結束都檢查</para>
        /// </summary>
        public int? DependencyCheck { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Folding</c>：控制純 LP 使用 folding 縮小模型的程度。
        /// <para>值：-1 = 由 CPLEX 決定（預設）｜0 = 關閉 folding｜1 = 一般強度｜2 = 加強｜3 = 大幅加強｜4 = 更強｜5 = 最強</para>
        /// </summary>
        public int? LpFolding { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.PresolveNode</c>：控制各搜尋節點是否再做前處理；每個節點處理太久時可比較不同設定。
        /// </summary>
        public int? NodePresolve { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Presolve</c>：是否啟用求解前的模型簡化；若無法判讀不可行原因，可關閉後重新做衝突分析（IIS）。
        /// <para>值：0 = 不做前處理｜1 = 做前處理（預設）</para>
        /// </summary>
        public bool? PreIndicator { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Dual</c>：控制 LP 是否先轉成對偶形式，再做前處理。
        /// <para>值：-1 = 關閉｜0 = 由 CPLEX 決定（預設）｜1 = 開啟</para>
        /// </summary>
        public int? PresolveDual { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.NumPass</c>：前處理最多執行多少輪。
        /// <para>值：-1 = 有幫助就繼續，由 CPLEX 決定（預設）｜0 = 不做 presolve，但其他縮減仍可能執行｜正整數 = 最多執行次數</para>
        /// </summary>
        public int? PresolvePasses { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Reduce</c>：控制前處理使用原始問題縮減、對偶縮減、兩者都用，或兩者都不用。
        /// <para>值：0 = 不做原始或對偶縮減｜1 = 只做原始縮減｜2 = 只做對偶縮減｜3 = 兩者都做（預設）</para>
        /// </summary>
        public int? PresolveReduce { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Reformulations</c>：控制前處理允許哪些模型改寫方式。
        /// <para>值：0 = 不允許改寫｜1 = 允許會影響原模型轉成前處理模型（crushing）的改寫｜2 = 允許會影響轉回原模型（uncrushing）的改寫｜3 = 允許所有改寫（預設）</para>
        /// </summary>
        public int? PresolveReformulations { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Relax</c>：根節點的連續鬆弛問題是否額外做一次 LP 前處理。
        /// </summary>
        public int? RelaxedLpPresolve { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.RepeatPresolve</c>：根節點處理完成後，是否再做一次前處理。
        /// <para>值：-1 = 由 CPLEX 決定（預設）｜0 = 不重做前處理｜1 = 重做但不使用 cuts｜2 = 重做並使用 cuts｜3 = 使用 cuts，且允許產生新的根節點 cuts</para>
        /// </summary>
        public int? RepeatPresolve { get; set; }

        /// <summary>
        /// <c>Param.Read.Scale</c>：調整矩陣係數的尺度；係數大小差很多時，可在 NumericalEmphasis 之外比較此設定。
        /// <para>值：-1 = 不縮放｜0 = 平衡行列的係數尺度（預設）｜1 = 更積極縮放</para>
        /// </summary>
        public int? Scaling { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Symmetry</c>：減少等價解重複搜尋的強度，適用於排班、指派等有相同資源可互換的模型。
        /// <para>值：-1 = 由 CPLEX 決定（預設）｜0 = 不處理對稱｜1 = 一般強度｜2 = 加強｜3 = 大幅加強｜4 = 更強｜5 = 最強</para>
        /// </summary>
        public int? Symmetry { get; set; }

        #endregion

        #region 搜尋策略 · root 與節點的 LP 演算法（25）

        /// <summary>
        /// <c>Param.Barrier.Algorithm</c>：barrier 演算法選擇。
        /// </summary>
        public int? BarrierAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Barrier.ColNonzeros</c>：一欄要有多少個非零係數，才被 barrier 視為稠密欄。
        /// </summary>
        public int? BarrierColumnNonzeros { get; set; }

        /// <summary>
        /// <c>Param.Barrier.Limits.Corrections</c>：barrier 中心化修正次數上限。
        /// <para>值：-1 = 由 CPLEX 決定（預設）｜0 = 不修正｜正整數 = 每次迭代的中心化修正上限</para>
        /// </summary>
        public long? BarrierCorrectionLimit { get; set; }

        /// <summary>
        /// <c>Param.Barrier.Crossover</c>：控制 barrier 結束後是否轉換成基底解（crossover）。
        /// </summary>
        public int? BarrierCrossover { get; set; }

        /// <summary>
        /// <c>Param.Barrier.Limits.Growth</c>：barrier 判斷計算不穩定時使用的成長上限。
        /// <para>值：至少 1.0；預設 1e12。</para>
        /// </summary>
        public double? BarrierGrowthLimit { get; set; }

        /// <summary>
        /// <c>Param.Barrier.Limits.ObjRange</c>：barrier 目標值的可接受範圍。
        /// <para>值：非負數；預設 1e20</para>
        /// </summary>
        public double? BarrierObjectiveRange { get; set; }

        /// <summary>
        /// <c>Param.Barrier.Ordering</c>：barrier 的排序演算法。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = 近似最小度數（AMD）｜2 = 近似最少填入（AMF）｜3 = 巢狀分割（ND）</para>
        /// </summary>
        public int? BarrierOrdering { get; set; }

        /// <summary>
        /// <c>Param.Barrier.StartAlg</c>：barrier 起始點演算法。
        /// <para>最小值 1，設 0 會被拒（Error 1014）。</para>
        /// </summary>
        public int? BarrierStartAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Simplex.DGradient</c>：dual simplex 選擇下一個候選變數所用的方法（pricing）。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = 標準 dual pricing｜2 = 最陡邊法｜3 = 在 slack 空間使用最陡邊法｜4 = 以單位初始範數使用最陡邊法｜5 = devex</para>
        /// </summary>
        public int? DualSimplexPricing { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Tolerances.Markowitz</c>：選擇矩陣分解樞紐時使用的 Markowitz 容許誤差；數值不穩時可調大。
        /// <para>值：0.0001 到 0.99999；預設 0.01.</para>
        /// </summary>
        public double? MarkowitzTolerance { get; set; }

        /// <summary>
        /// <c>Param.Network.NetFind</c>：從模型辨識出網路結構時，要嘗試到什麼程度。
        /// <para>值：1 = 只辨識純網路結構｜2 = 嘗試反射縮放（預設）｜3 = 嘗試一般縮放</para>
        /// <para>最小值 1（預設 2），設 0 會被拒（Error 1014）。</para>
        /// </summary>
        public int? NetworkExtractionLevel { get; set; }

        /// <summary>
        /// <c>Param.Network.Pricing</c>：network simplex 選擇下一個候選變數所用的方法（pricing）。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = 部分 pricing｜2 = 多重部分 pricing｜3 = 先排序再做多重部分 pricing</para>
        /// </summary>
        public int? NetworkPricing { get; set; }

        /// <summary>
        /// <c>Param.NodeAlgorithm</c>：非根節點的連續鬆弛問題使用哪個演算法。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = Primal simplex｜2 = Dual simplex｜3 = Network simplex｜4 = Barrier｜5 = Sifting</para>
        /// </summary>
        public int? NodeAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Simplex.PGradient</c>：primal simplex 選擇下一個候選變數所用的方法（pricing）。
        /// <para>值：-1 = 依 reduced cost｜0 = 混用 reduced cost 與 devex（預設）｜1 = devex｜2 = 最陡邊法｜3 = 以 slack 初始範數使用最陡邊法｜4 = 完整 pricing</para>
        /// </summary>
        public int? PrimalSimplexPricing { get; set; }

        /// <summary>
        /// <c>Param.RootAlgorithm</c>：根節點的連續鬆弛問題使用哪個演算法。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = Primal Simplex｜2 = Dual Simplex｜3 = Network Simplex｜4 = Barrier｜5 = Sifting｜6 = 多演算法同時求解；機會式模式使用 Dual、Barrier、Primal，決定論模式使用 Dual、Barrier</para>
        /// </summary>
        public int? RootAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Sifting.Algorithm</c>：sifting 子問題用哪個演算法。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = Primal Simplex｜2 = Dual Simplex｜3 = Network Simplex｜4 = Barrier</para>
        /// </summary>
        public int? SiftingAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Sifting.Simplex</c>：是否允許 simplex 內部切換到 sifting。
        /// <para>值：1 = 條件合適時，在 simplex 求解中使用 sifting（預設）｜0 = 不在 simplex 中使用 sifting</para>
        /// </summary>
        public bool? SiftingFromSimplex { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Crash</c>：simplex 建立起始基底時，如何使用目標係數與二次項（crash）。
        /// <para>值：LP Primal：-1 / 1 = 以不同方式使用目標係數，0 = 忽略目標係數，預設 1；LP Dual：-1 / 0 = 積極建立起始基底，1 = 預設基底；QP Primal：-1 = slack 基底，0 = 忽略二次項並用 LP 建立基底，1 = 忽略目標式並用 LP 建立基底（預設）；QP Dual：-1 = slack 基底，0 / 1 = 使用二次項建立基底，預設 1。</para>
        /// </summary>
        public int? SimplexCrash { get; set; }

        /// <summary>
        /// <c>Param.Simplex.DynamicRows</c>：允許 dual simplex 自動管理目前保留的限制式列。
        /// <para>值：-1 = 由 CPLEX 決定（預設）｜0 = 保留所有限制式列｜1 = 由 CPLEX 管理要保留的列</para>
        /// </summary>
        public int? SimplexDynamicRows { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Perturbation.Constant</c>：simplex 為避免反覆停在相同位置而加入的微小擾動量。
        /// <para>值：至少 1e-8 的正數；預設 1e-6.</para>
        /// </summary>
        public double? SimplexPerturbationConstant { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Perturbation.Indicator</c>：是否從一開始就加入擾動，協助處理嚴重退化、進度停滯的模型。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = 從一開始就加入擾動</para>
        /// </summary>
        public bool? SimplexPerturbationIndicator { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Limits.Perturbation</c>：連續多少次退化迭代沒有進展後，開始加入擾動。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜正整數 = 加入擾動前允許的退化迭代次數</para>
        /// </summary>
        public int? SimplexPerturbationLimit { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Pricing</c>：simplex 每次選擇變數時，候選清單的大小。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜正整數 = pricing 候選數量</para>
        /// </summary>
        public int? SimplexPricingCandidateList { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Refactor</c>：每隔多少次迭代重新分解基底矩陣。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 到 10 000 的整數 = 兩次基底矩陣分解之間的迭代次數</para>
        /// </summary>
        public int? SimplexRefactorFrequency { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Limits.Singularity</c>：基底矩陣無法正常求逆時，最多嘗試修復多少次。
        /// <para>值：非負整數；預設 10.</para>
        /// </summary>
        public int? SimplexSingularityLimit { get; set; }

        #endregion

        #region 搜尋策略 · 特定模型類別（15）

        /// <summary>
        /// <c>Param.Benders.Tolerances.FeasibilityCut</c>：Benders 判斷可行性切割限制時使用的容許誤差。
        /// </summary>
        public double? BendersFeasibilityCutTol { get; set; }

        /// <summary>
        /// <c>Param.Benders.Tolerances.OptimalityCut</c>：Benders 判斷最佳性切割限制時使用的容許誤差。
        /// </summary>
        public double? BendersOptimalityCutTol { get; set; }

        /// <summary>
        /// <c>Param.Benders.Strategy</c>：選擇是否使用 Benders 分解，以及依使用者註記或自動方式拆分主問題與子問題。
        /// <para>值：-1 = 不使用 Benders，忽略分解註記；0 = 自動，沒有註記時用一般分支定界，有註記時保留指定主問題並嘗試再拆分子問題（預設）；1 = 完全依使用者註記分解；2 = 保留指定主問題，再嘗試拆分其餘部分；3 = 忽略註記，先做前處理，再將整數變數放入主問題、連續線性變數分成互不重疊的子問題。1 / 2 缺少註記會回報 CPXERR_NO_DECOMPOSITION；0 / 1 / 2 的註記無法形成完整分解時會回報 CPXERR_BAD_DECOMPOSITION。3 若遇純 LP，可回報 CPXERR_PARAM_INCOMPATIBLE；若沒有可用的整數主問題或連續子問題，可回報 CPXERR_NO_DECOMPOSITION。</para>
        /// <para><b>1 / 2 需要分解註記；3 需要模型能自動分解</b>。若沒有可用的分解，會拋出 <c>Error 2000: No Benders decomposition available</c>；一般 MIP 若不打算使用 Benders，可選 -1 或 0。</para>
        /// </summary>
        public int? BendersStrategy { get; set; }

        /// <summary>
        /// <c>Param.Benders.WorkerAlgorithm</c>：Benders 子問題用哪個演算法。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = Primal Simplex｜2 = Dual Simplex｜3 = Network Simplex｜4 = Barrier｜5 = Sifting</para>
        /// </summary>
        public int? BendersWorkerAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.QCPDuals</c>：是否計算 QCP 的對偶值。
        /// <para>值：0 = 不計算｜1 = 可以計算時才計算｜2 = 要求一定計算</para>
        /// </summary>
        public int? CalculateQcpDuals { get; set; }

        /// <summary>
        /// <c>Param.Feasopt.Mode</c>：模型不可行時，FeasOpt 用哪種標準決定要放寬哪些限制式與界限。
        /// <para>值：0 = 最小化放寬量總和，只做第一階段（預設）｜1 = 先最小化放寬量總和，再於此條件下求最佳解｜2 = 最小化需放寬的限制式與界限數量，只做第一階段｜3 = 先最小化需放寬的數量，再求最佳解｜4 = 最小化放寬量的平方和，只做第一階段｜5 = 先最小化平方和，再求最佳解</para>
        /// </summary>
        public int? FeasOptMode { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.MIQCPStrat</c>：MIQCP 各節點使用二次限制鬆弛（QCP）或線性鬆弛（LP）。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = 每個節點解 QCP 鬆弛｜2 = 每個節點解 LP 鬆弛</para>
        /// </summary>
        public int? MiqcpStrategy { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.QPMakePSD</c>：控制前處理是否嘗試把非凸二元 QP 改寫成凸模型。
        /// <para>值：0 = 不嘗試將二元模型改成半正定（PSD）｜1 = 嘗試改成 PSD（預設）</para>
        /// </summary>
        public bool? QpMakePsd { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.QToLin</c>：把 QP / MIQP 的二次項線性化。
        /// <para>值：-1 = 由 CPLEX 決定（預設）｜0 = 不將 QP / MIQP 目標式的二次項線性化｜1 = 將這些二次項線性化</para>
        /// </summary>
        public int? QpToLinear { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.SOS1Reform</c>：是否將特殊有序集合 SOS1 改寫成線性限制式。
        /// <para>值：-1 = 不將 SOS1 改成線性限制式｜0 = 由 CPLEX 決定（預設）｜1 = 使用對數形式的線性改寫</para>
        /// </summary>
        public int? Sos1Reformulation { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.SOS2Reform</c>：是否將特殊有序集合 SOS2 改寫成線性限制式。
        /// <para>值：-1 = 不將 SOS2 改成線性限制式｜0 = 由 CPLEX 決定（預設）｜1 = 使用對數形式的線性改寫</para>
        /// </summary>
        public int? Sos2Reformulation { get; set; }

        /// <summary>
        /// <c>Param.MIP.SubMIP.SubAlg</c>：啟發式方法內部建立的小型 MIP（subMIP），在非根節點使用哪個演算法。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = Primal Simplex｜2 = Dual Simplex｜3 = Network Simplex｜4 = Barrier｜5 = Sifting</para>
        /// </summary>
        public int? SubMipNodeAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.MIP.SubMIP.NodeLimit</c>：啟發式方法內部求解小型 MIP（subMIP）時，最多處理多少節點。
        /// <para>最小值 1，設 0 會被拒（Error 1014）。</para>
        /// </summary>
        public long? SubMipNodeLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.SubMIP.StartAlg</c>：啟發式方法內部建立的小型 MIP（subMIP），在根節點使用哪個鬆弛演算法。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = Primal Simplex｜2 = Dual Simplex｜3 = Network Simplex｜4 = Barrier｜5 = Sifting</para>
        /// </summary>
        public int? SubMipRootAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.MIP.SubMIP.Scale</c>：subMIP 的縮放設定。
        /// <para>值：-1 = 不縮放｜0 = 平衡行列的係數尺度（預設）｜1 = 更積極縮放</para>
        /// </summary>
        public int? SubMipScaling { get; set; }

        #endregion

        #region 非 tuning 參數（35）

        /// <summary>
        /// <c>Param.Barrier.Display</c>：barrier log 詳細度。
        /// </summary>
        public int? BarrierDisplay { get; set; }

        /// <summary>
        /// <c>Param.Output.CloneLog</c>：平行求解時，是否讓各份模型副本各自寫一份 log，供問題診斷。
        /// </summary>
        public bool? CloneLog { get; set; }

        /// <summary>
        /// <c>Param.Read.Variables</c>：讀取模型檔時允許的變數數量上限。
        /// <para>值：0 到 CPX_BIGINT 的整數；預設 60 000。</para>
        /// <para>CPLEX 已標為過時；僅供相容既有程式，新程式不宜依賴。</para>
        /// </summary>
        public int? ColumnRead { get; set; }

        /// <summary>
        /// <c>Param.Conflict.Algorithm</c>：選擇分析衝突限制式的演算法，協助找出模型無解的原因。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = 簡單快速方法｜2 = 界限傳遞｜3 = 前處理｜4 = 連續模型的不可約衝突集合（IIS）｜5 = 限制求解投入｜6 = 完整求解</para>
        /// </summary>
        public int? ConflictAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Conflict.Display</c>：衝突限制式分析要輸出多詳細的 log。
        /// <para>值：0 = 不顯示｜1 = 顯示摘要（預設）｜2 = 顯示詳細內容</para>
        /// </summary>
        public int? ConflictDisplay { get; set; }

        /// <summary>
        /// <c>Param.Read.DataCheck</c>：輸入資料一致性檢查與建模建議的層級。
        /// <para>值：0 = 不檢查（預設）｜1 = 檢查資料（Python API 的預設）｜2 = 檢查資料並提供建模建議，兩者都會輸出警告</para>
        /// </summary>
        public int? DataCheck { get; set; }

        /// <summary>
        /// <c>Param.Read.FileEncoding</c>：讀寫檔案的編碼。
        /// <para>值：有效的編碼名稱（code page）；預設 ISO-8859-1 或空字串。</para>
        /// </summary>
        public string FileEncoding { get; set; }

        /// <summary>
        /// <c>Param.Output.IntSolFilePrefix</c>：每找到一個整數解就存檔的檔名前綴。
        /// <para>值：有效的檔名前綴；預設空字串，表示關閉此功能。</para>
        /// </summary>
        public string IntSolFilePrefix { get; set; }

        /// <summary>
        /// <c>Param.MIP.Display</c>：MIP 節點 log 的詳細度。
        /// <para>值：0 = 找到最佳解前不顯示｜1 = 顯示整數可行解｜2 = 另按 MipInterval 顯示節點紀錄（預設）｜3 = 另顯示新增 cuts 數、成功 MIP start 的處理資訊，以及找到整數可行解時的秒數與 ticks｜4 = 另顯示根節點的 LP 資訊｜5 = 另顯示其他節點的 LP 資訊</para>
        /// </summary>
        public int? MipDisplay { get; set; }

        /// <summary>
        /// <c>Param.MIP.Interval</c>：每幾個節點印一行 log。
        /// <para>值：n &lt; 0 = 顯示新最佳解，節點 log 起初較密、之後逐漸減少｜0 = 由 CPLEX 決定頻率（預設）｜n &gt; 0 = 顯示新最佳解，且每 n 個節點輸出一行 log</para>
        /// </summary>
        public long? MipInterval { get; set; }

        /// <summary>
        /// <c>Param.Output.MPSLong</c>：MPS / REW 輸出的數值精度。
        /// </summary>
        public bool? MpsLongNumerics { get; set; }

        /// <summary>
        /// <c>Param.MultiObjective.Display</c>：多目標求解的 log 詳細度。
        /// </summary>
        public int? MultiObjectiveDisplay { get; set; }

        /// <summary>
        /// <c>Param.Network.Display</c>：network simplex log 詳細度。
        /// </summary>
        public int? NetworkDisplay { get; set; }

        /// <summary>
        /// <c>Param.Read.Nonzeros</c>：讀檔時的非零元素數量上限。
        /// <para>值：0 到 CPX_BIGINT 或 CPX_BIGLONG 的整數，依整數型別而定；預設 250 000。</para>
        /// <para>CPLEX 已標為過時；僅供相容既有程式，新程式不宜依賴。</para>
        /// </summary>
        public long? NonzeroRead { get; set; }

        /// <summary>
        /// <c>Param.ParamDisplay</c>：求解前是否印出被改過的參數清單。
        /// </summary>
        public bool? ParamDisplay { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.Populate</c>：一次 Populate 呼叫最多產生多少個解。
        /// <para>值：非負整數；預設 20.</para>
        /// <para>設 0 會觸發 Error 1014，請至少設 1。</para>
        /// </summary>
        public int? PopulateLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.ProbeDetTime</c>：試探變數值（probing）最多可使用多少計算工作量，單位為 ticks。
        /// <para>值：非負數；預設 1e+75.</para>
        /// </summary>
        public double? ProbeDetTimeLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.ProbeTime</c>：試探變數值（probing）的時間上限，單位為秒。
        /// <para>值：非負數；預設 1e+75.</para>
        /// </summary>
        public double? ProbeTimeLimit { get; set; }

        /// <summary>
        /// <c>Param.Read.QPNonzeros</c>：讀檔時 Q 矩陣的非零元素上限。
        /// <para>值：0 到 CPX_BIGINT 或 CPX_BIGLONG 的整數，依整數型別而定；預設 5 000。</para>
        /// <para>CPLEX 已標為過時；僅供相容既有程式，新程式不宜依賴。</para>
        /// </summary>
        public long? QpNonzeroRead { get; set; }

        /// <summary>
        /// <c>Param.Read.WarningLimit</c>：同一類讀檔警告最多印幾次。
        /// <para>值：n &gt;= 0，表示每類警告的顯示次數上限；預設 10。</para>
        /// </summary>
        public long? ReadWarningLimit { get; set; }

        /// <summary>
        /// <c>Param.Record</c>：錄下這次呼叫序列供 IBM 重現問題（診斷用）。
        /// <para>值：0 = 不記錄（預設）｜1 = 記錄呼叫序列</para>
        /// </summary>
        public bool? Record { get; set; }

        /// <summary>
        /// <c>Param.Read.Constraints</c>：讀檔時的限制式數量上限。
        /// <para>值：0 到 CPX_BIGINT 的整數；預設 30 000。</para>
        /// <para>CPLEX 已標為過時；僅供相容既有程式，新程式不宜依賴。</para>
        /// </summary>
        public int? RowRead { get; set; }

        /// <summary>
        /// <c>Param.Sifting.Display</c>：sifting log 詳細度。
        /// <para>值：0 = 不顯示 sifting 資訊｜1 = 顯示主要迭代（預設）｜2 = 另顯示每次 sifting 迭代中的 LP 子問題資訊</para>
        /// </summary>
        public int? SiftingDisplay { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Display</c>：simplex 迭代 log 詳細度。
        /// <para>值：0 = 找到解前不顯示迭代資訊｜1 = 每次重新分解基底後顯示（預設）｜2 = 每次迭代都顯示</para>
        /// </summary>
        public int? SimplexDisplay { get; set; }

        /// <summary>
        /// <c>Param.MIP.Pool.AbsGap</c>：解的目標值與最佳解差距最多可有多大，才可保留在 solution pool。
        /// <para>值：非負實數；預設 1.0e+75.</para>
        /// </summary>
        public double? SolutionPoolAbsGap { get; set; }

        /// <summary>
        /// <c>Param.MIP.Pool.Capacity</c>：solution pool（額外解清單）最多保留多少個解。
        /// <para>值：非負整數；0 關閉 solution pool 的所有功能；預設 2100000000。</para>
        /// </summary>
        public int? SolutionPoolCapacity { get; set; }

        /// <summary>
        /// <c>Param.MIP.Pool.Intensity</c>：找額外解的積極程度。
        /// <para>值：0 = 由 CPLEX 決定（預設）｜1 = 快速產生少量解｜2 = 產生較多解｜3 = 盡量產生大量解，可能增加耗時｜4 = 盡可能列舉所有實際可找出的解</para>
        /// </summary>
        public int? SolutionPoolIntensity { get; set; }

        /// <summary>
        /// <c>Param.MIP.Pool.RelGap</c>：解的目標值與最佳解的相對差距最多可有多大，才可保留在 solution pool。
        /// <para>值：非負實數；預設 1.0e+75.</para>
        /// </summary>
        public double? SolutionPoolRelGap { get; set; }

        /// <summary>
        /// <c>Param.MIP.Pool.Replace</c>：額外解清單已滿時，選擇要移除哪個舊解。
        /// <para>值：0 = 新解取代最早加入的解（預設）｜1 = 取代目標值最差的解｜2 = 優先保留彼此差異較大的解</para>
        /// </summary>
        public int? SolutionPoolReplace { get; set; }

        /// <summary>
        /// <c>Param.Tune.DetTimeLimit</c>：CPLEX 內建參數調校工具的計算工作量上限，單位為 ticks。
        /// </summary>
        public double? TuningDetTimeLimit { get; set; }

        /// <summary>
        /// <c>Param.Tune.Display</c>：CPLEX 內建參數調校工具要輸出多詳細的 log。
        /// <para>值：0 = 不顯示｜1 = 基本摘要（預設）｜2 = 另顯示正在嘗試的參數設定｜3 = 完整報告與 log</para>
        /// </summary>
        public int? TuningDisplay { get; set; }

        /// <summary>
        /// <c>Param.Tune.Measure</c>：CPLEX 內建參數調校工具如何依求解時間評選設定。
        /// <para>值：CPX_TUNE_AVERAGE = 比較平均耗時（預設）｜CPX_TUNE_MINMAX = 盡量降低最慢一次的耗時</para>
        /// <para>最小值 1，設 0 會被拒（Error 1014）。</para>
        /// </summary>
        public int? TuningMeasure { get; set; }

        /// <summary>
        /// <c>Param.Tune.Repeat</c>：CPLEX 內建參數調校時，重新排列模型並重測的次數。
        /// <para>值：非負整數；預設 1</para>
        /// <para>設 0 會觸發 Error 1014，請至少設 1。</para>
        /// </summary>
        public int? TuningRepeat { get; set; }

        /// <summary>
        /// <c>Param.Tune.TimeLimit</c>：CPLEX 內建參數調校工具的時間上限，單位為秒。
        /// </summary>
        public double? TuningTimeLimit { get; set; }

        /// <summary>
        /// <c>Param.Output.WriteLevel</c>：寫 MST / SOL 檔時要包含哪些變數。
        /// <para>值：0 = 由 CPLEX 決定｜1 = 所有變數及解值｜2 = 只有離散變數及解值｜3 = 只有解值非零的變數｜4 = 只有解值非零的離散變數</para>
        /// </summary>
        public int? WriteLevel { get; set; }

        #endregion

        #region ISolverConfig 介面別名

        /// <summary>
        /// 以 ISolverConfig 的 int 屬性讀寫 <see cref="PreIndicator"/>：0 關閉、非 0 開啟、null 沿用 CPLEX 預設。
        /// 兩個屬性共用同一項設定（<c>Param.Preprocessing.Presolve</c>），修改其中一個也會改變另一個的讀值。
        /// </summary>
        public int? Presolve
        {
            get => PreIndicator.HasValue ? (PreIndicator.Value ? 1 : 0) : (int?)null;
            set => PreIndicator = value.HasValue ? value.Value != 0 : (bool?)null;
        }

        #endregion
    }
}
