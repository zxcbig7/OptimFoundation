using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// CPLEX 求解器設定。涵蓋 ILOG.CPLEX 22.1.1 的 .NET API 全部可設參數，
    /// 每個參數只暴露一個 PascalCase property，值域與預設依 IBM 官方 Parameters Reference。
    /// </summary>
    /// <remarks>
    /// <para><b>設定規範</b></para>
    /// <para>
    /// 1. <b>null = 不設</b>。property 為 null 時 Configuration() 不會呼叫 SetParam，直接沿用 CPLEX 自己的預設值。
    ///    要調某顆旋鈕就只設那一顆，其餘留 null —— 一次塞滿參數就分不出是哪一顆造成差異。
    /// </para>
    /// <para>
    /// 2. <b>六個欄位有 property initializer</b>（Threads、RowRead、MemoryLimitMb、MipGap、OptimalityTol、FeasibilityTol），
    ///    它們<b>不是 null</b>，所以一定會被送進 CPLEX。其中只有 Threads = 32 真的偏離 CPLEX 官方預設（官方是 0 = automatic），
    ///    其餘五個數值與官方預設相同。要讓 CPLEX 自己決定執行緒數就明確設 <c>Threads = null</c>。
    /// </para>
    /// <para>
    /// 3. <b>分類決定一顆旋鈕能不能拿來做實驗</b>：
    ///    <b>停止條件</b>管「什麼時候算解完」，<b>執行資源</b>管「用多少機器」，<b>重複量測</b>是同一設定再量一次用的，
    ///    只有<b>搜尋策略</b>（109 顆）能進 variant 池。判準：改了之後兩次求解還算不算在做同一件事？不算 → 停止條件。
    ///    改了之後量測的尺還算不算同一把？不算 → 執行資源。每個 property 的註解都標了它屬於哪一類。
    /// </para>
    /// <para>
    /// 4. <b>順序陷阱</b>：Configuration() 在套用 <see cref="MemoryLimitMb"/> 時會強制把 node file 設為 0。
    ///    要做「記憶體爆掉就把節點寫到磁碟」MUST 同時設 <see cref="NodeFileStrategy"/>，
    ///    它在 MemoryLimitMb 之後套用才不會被蓋掉。另注意 CPLEX 對 node file 的官方預設是 1（壓縮後留記憶體），
    ///    比框架強制的 0 更安全。
    /// </para>
    /// <para>
    /// 5. <b>查證來源</b>：本機官方文件
    ///    <c>%CPLEX_STUDIO_DIR2211%\doc\html\en-US\CPLEX\Parameters\topics\&lt;ParamName&gt;.html</c>，
    ///    或互動式查詢 <c>@("set &lt;param&gt;","quit") | &amp; "$env:CPLEX_STUDIO_DIR2211\cplex\bin\x64_win64\cplex.exe"</c>。
    ///    每個 property 的註解都附了官方參數路徑與 Interactive Optimizer 指令名，可直接對回文件。
    /// </para>
    /// <para>
    /// 6. <b>未實作的三個</b>：
    ///    <c>Param.Benders.Tolerances.feasibilitycut</c> —— IBM 在 DLL 裡同時放了大小寫兩版，取 PascalCase 的 FeasibilityCut；
    ///    <c>Param.Benders.Tolerances.optimalitycut</c> —— IBM 在 DLL 裡同時放了大小寫兩版，取 PascalCase 的 OptimalityCut；
    ///    <c>Param.Preprocessing.Linear</c> —— 官方標記 deprecated，已由 Preprocessing.Reformulations 取代；
    ///    另外 <c>Param.Read.APIEncoding</c> 與螢幕輸出開關屬 C API，.NET 端分別無對應與改用 SetOut。
    /// </para>
    /// </remarks>
    public sealed class CplexConfig : ISolverConfig, ITunableConfig
    {
        /// <summary>Creates a shallow copy containing every current public setting.</summary>
        public CplexConfig Clone() => (CplexConfig)MemberwiseClone();

        #region 停止條件（27）

        // 定義「什麼叫解出來了」。
        // 同一個 tuning 週期內固定共用，NEVER 進 variant 池；改了它 = 終點線移動，前後數據不可比。

        /// <summary>
        /// <c>Param.MIP.Tolerances.AbsMIPGap</c>（Interactive <c>mip tolerances absmipgap</c>）—— 絕對 MIP gap。
        /// <para>值：Any nonnegative number; default : 1e-06.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? AbsoluteMipGap { get; set; }

        /// <summary>
        /// <c>Param.Barrier.ConvergeTol</c>（Interactive <c>barrier convergetol</c>）—— barrier 收斂容差（LP / QP）。
        /// <para>值：Any positive number greater than or equal to 1e-12; default : 1e-8.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? BarrierConvergeTol { get; set; }

        /// <summary>
        /// <c>Param.Barrier.Limits.Iteration</c>（Interactive <c>barrier limits iteration</c>）—— barrier 迭代次數上限。
        /// <para>值：0 = No barrier iterations｜9223372036800000000 = default｜Any positive integer = Number of barrier iterations before termination</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public long? BarrierIterationLimit { get; set; }

        /// <summary>
        /// <c>Param.Barrier.QCPConvergeTol</c>（Interactive <c>barrier qcpconvergetol</c>）—— barrier 收斂容差（QCP）。
        /// <para>值：Any positive number greater than or equal to 1e-12; default : 1e-7. For LPs and for QPs (that is, when all the constraints are linear) see convergence tolerance for LP and QP problems .</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? BarrierQcpConvergeTol { get; set; }

        /// <summary>
        /// <c>Param.DetTimeLimit</c>（Interactive <c>dettimelimit</c>）—— 決定論時間上限（ticks），跨機器可重現。
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? DeterministicTimeLimit { get; set; }

        /// <summary>
        /// <c>Param.Feasopt.Tolerance</c>（Interactive <c>feasopt tolerance</c>）—— FeasOpt 放鬆量的容差。
        /// <para>值：Any nonnegative value; default : 1e-6.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? FeasOptRelaxTolerance { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Tolerances.Feasibility</c>（Interactive <c>simplex tolerances feasibility</c>）—— simplex 可行性容差（constraint 違反量）。
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? FeasibilityTol { get; set; } = 1e-06;

        /// <summary>
        /// <c>Param.MIP.Limits.Solutions</c>（Interactive <c>mip limits solutions</c>）—— 找到 N 個整數解即停。
        /// <para>值：Any positive integer strictly greater than zero; zero is not allowed; default : 9223372036800000000.</para>
        /// <para>最小值 1，設 0 會被拒（Error 1014）。</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public long? IntegerSolutionLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.Integrality</c>（Interactive <c>mip tolerances integrality</c>）—— 整數容差：離整數多近算整數。
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? IntegralityTolerance { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.Linearization</c>（Interactive <c>mip tolerances linearization</c>）—— QP/MIQP 線性化時使用的 epsilon。
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? LinearizationTolerance { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.LowerCutoff</c>（Interactive <c>mip tolerances lowercutoff</c>）—— 下界剪枝：最大化問題的對應項。
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? LowerCutoff { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.LowerObjStop</c>（Interactive <c>mip limits lowerobjstop</c>）—— 目標值低於此值即停止（最小化問題的早停門檻）。
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? LowerObjectiveStop { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.MIPGap</c>（Interactive <c>mip tolerances mipgap</c>）—— 相對 MIP gap，達到即視為收斂停止。
        /// <para>值：Any number from 0.0 to 1.0; default : 1e-04.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? MipGap { get; set; } = 1e-4;

        /// <summary>
        /// <c>Param.Network.Tolerances.Feasibility</c>（Interactive <c>network tolerances feasibility</c>）—— network simplex 可行性容差。
        /// <para>值：Any number from 1e-11 to 1e-1; default : 1e-6.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? NetworkFeasibilityTol { get; set; }

        /// <summary>
        /// <c>Param.Network.Iterations</c>（Interactive <c>—</c>）—— network simplex 迭代次數上限。
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public long? NetworkIterationLimit { get; set; }

        /// <summary>
        /// <c>Param.Network.Tolerances.Optimality</c>（Interactive <c>network tolerances optimality</c>）—— network simplex 最佳性容差。
        /// <para>值：Any number from 1e-11 to 1e-1; default : 1e-6.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? NetworkOptimalityTol { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.Nodes</c>（Interactive <c>mip limits nodes</c>）—— B&amp;B 節點數上限。
        /// <para>值：Any nonnegative integer; default : 9223372036800000000.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public long? NodeLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.ObjDifference</c>（Interactive <c>mip tolerances objdifference</c>）—— 絕對目標差門檻：新 incumbent 至少要好這麼多才接受。
        /// <para>值：Any number; default : 0.0.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? ObjectiveDifference { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Tolerances.Optimality</c>（Interactive <c>simplex tolerances optimality</c>）—— simplex 最佳性容差（reduced cost）。
        /// <para>值：Any number from 1e-9 to 1e-1; default : 1e-06.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? OptimalityTol { get; set; } = 1e-06;

        /// <summary>
        /// <c>Param.MIP.Tolerances.RelObjDifference</c>（Interactive <c>mip tolerances relobjdifference</c>）—— 相對目標差門檻。
        /// <para>值：Any number from 0.0 to 1.0; default : 0.0.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? RelativeObjectiveDifference { get; set; }

        /// <summary>
        /// <c>Param.Sifting.Iterations</c>（Interactive <c>sifting iterations</c>）—— sifting 迭代次數上限。
        /// <para>值：Any nonnegative integer; default : 9223372036800000000.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public long? SiftingIterationLimit { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Limits.Iterations</c>（Interactive <c>simplex limits iterations</c>）—— simplex 迭代次數上限。
        /// <para>值：Any nonnegative integer; default : 9223372036800000000.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public long? SimplexIterationLimit { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Limits.LowerObj</c>（Interactive <c>simplex limits lowerobj</c>）—— 純 LP：目標值低於此值即停止。
        /// <para>值：Any number; default : -1e+75.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? SimplexLowerObjectiveLimit { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Limits.UpperObj</c>（Interactive <c>simplex limits upperobj</c>）—— 純 LP：目標值高於此值即停止。
        /// <para>值：Any number; default : 1e+75.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? SimplexUpperObjectiveLimit { get; set; }

        /// <summary>
        /// <c>Param.TimeLimit</c>（Interactive <c>timelimit</c>）—— 牆鐘求解時間上限（秒）。
        /// <para>值：Any nonnegative value in seconds; default : 1e+75.</para>
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? TimeLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Tolerances.UpperCutoff</c>（Interactive <c>mip tolerances uppercutoff</c>）—— 上界剪枝：已知最小化問題的解不會大於此值時填入，直接砍掉更差的分支。
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? UpperCutoff { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.UpperObjStop</c>（Interactive <c>mip limits upperobjstop</c>）—— 目標值超過此值即停止（最大化問題的早停門檻）。
        /// <para>分類：停止條件 —— 整期固定，NEVER 進 variant 池。</para>
        /// </summary>
        public double? UpperObjectiveStop { get; set; }

        #endregion

        #region 執行資源（9）

        // 定義量測基準（機器資源、計時、記憶體）。
        // R0 校準之前先單獨定版，之後整期凍結；改了它 = 量測的尺會伸縮。

        /// <summary>
        /// <c>Param.MIP.Limits.AuxRootThreads</c>（Interactive <c>mip limits auxrootthreads n</c>）—— root 節點輔助工作分到幾個執行緒。
        /// <para>值：-1 = Off: do not use additional threads for auxiliary tasks｜0 = Automatic: let CPLEX choose the number of threads to use（預設）｜N &gt; n &gt; 0 = Use n threads for auxiliary root tasks</para>
        /// <para>分類：執行資源 —— R0 前定版後凍結，NEVER 與搜尋策略同輪比較。</para>
        /// </summary>
        public int? AuxiliaryRootThreads { get; set; }

        /// <summary>
        /// <c>Param.CPUmask</c>（Interactive <c>cpumask</c>）—— 把執行緒綁到指定 core，用來壓低多執行緒時序造成的量測雜訊。
        /// <para>值："off" = CPLEX performs no binding｜"auto" = Default CPLEX decides whether to bind threads to cores｜A string consisting of digits or characters from the set {0-9, a-f, A-F} = CPLEX binds the threads in round-robin fashion to the cores specified by the mask</para>
        /// <para><b>本機實測不支援</b>：設任何值（含官方文件列的 "auto"）都會丟 <c>CpxException: CPLEX Error 1811 Attempt to invoke unsupported operation</c>，CPLEX 自己的 Interactive Optimizer 執行 <c>set cpumask auto</c> 也是同一個錯。留著這個欄位是為了涵蓋支援綁核的平台；在本機 NEVER 設它。</para>
        /// <para>分類：執行資源 —— R0 前定版後凍結，NEVER 與搜尋策略同輪比較。</para>
        /// </summary>
        public string CpuMask { get; set; }

        /// <summary>
        /// <c>Param.Emphasis.Memory</c>（Interactive <c>emphasis memory</c>）—— 記憶體節約模式：犧牲速度換記憶體。
        /// <para>值：0 = Off; do not conserve memory（預設）｜1 = On; conserve memory where possible</para>
        /// <para>分類：執行資源 —— R0 前定版後凍結，NEVER 與搜尋策略同輪比較。</para>
        /// </summary>
        public bool? MemoryEmphasis { get; set; }

        /// <summary>
        /// <c>Param.WorkMem</c>（Interactive <c>workmem</c>）—— 工作記憶體 (MB)，管的是 live tree 大小不是行程總記憶體。
        /// <para>分類：執行資源 —— R0 前定版後凍結，NEVER 與搜尋策略同輪比較。</para>
        /// </summary>
        public double? MemoryLimitMb { get; set; } = 2048;

        /// <summary>
        /// <c>Param.MIP.Strategy.File</c>（Interactive <c>mip strategy file</c>）—— 樹超過記憶體上限時節點怎麼存。
        /// <para>分類：執行資源 —— R0 前定版後凍結，NEVER 與搜尋策略同輪比較。</para>
        /// </summary>
        public int? NodeFileStrategy { get; set; }

        /// <summary>
        /// <c>Param.Parallel</c>（Interactive <c>parallel</c>）—— 平行模式：決定搜尋路徑可不可重現。
        /// <para>值：-1 = Enable opportunistic parallel search mode｜0 = Automatic: let CPLEX decide whether to invoke deterministic or opportunistic search（預設）｜1 = Enable deterministic parallel search mode</para>
        /// <para>分類：執行資源 —— R0 前定版後凍結，NEVER 與搜尋策略同輪比較。</para>
        /// </summary>
        public int? ParallelMode { get; set; }

        /// <summary>
        /// <c>Param.Threads</c>（Interactive <c>threads</c>）—— 求解可用的工作執行緒上限。
        /// <para>值：0 = Automatic: let CPLEX decide（預設）｜1 = Sequential; single threaded｜N = Uses up to N threads; N is limited by available processors and Processor Value Units (PVU)</para>
        /// <para>分類：執行資源 —— R0 前定版後凍結，NEVER 與搜尋策略同輪比較。</para>
        /// </summary>
        public int? Threads { get; set; } = 32;

        /// <summary>
        /// <c>Param.MIP.Limits.TreeMemory</c>（Interactive <c>mip limits treememory</c>）—— B&amp;B 樹記憶體上限 (MB)。
        /// <para>值：Any nonnegative number; default : 1e+75.</para>
        /// <para>分類：執行資源 —— R0 前定版後凍結，NEVER 與搜尋策略同輪比較。</para>
        /// </summary>
        public double? TreeMemoryLimitMb { get; set; }

        /// <summary>
        /// <c>Param.WorkDir</c>（Interactive <c>workdir</c>）—— 節點檔等暫存檔的目錄；NodeFileStrategy 設 2/3 時才有意義。
        /// <para>值：Any existing directory; default : ‘.’</para>
        /// <para>分類：執行資源 —— R0 前定版後凍結，NEVER 與搜尋策略同輪比較。</para>
        /// </summary>
        public string WorkDir { get; set; }

        #endregion

        #region 重複量測（2）

        // 同一個設定再量一次用的工具，不是候選設定。
        // Seed 換一個只是同一設定再量一次。

        /// <summary>
        /// <c>Param.ClockType</c>（Interactive <c>clocktype</c>）—— 計時基準：1 CPU time / 2 wall-clock。
        /// <para>分類：重複量測 —— 同一設定再量一次用的，NEVER 當候選設定排名。</para>
        /// </summary>
        public int? ClockType { get; set; }

        /// <summary>
        /// <c>Param.RandomSeed</c>（Interactive <c>randomseed</c>）—— 隨機種子。重複量測用的自變數，NEVER 當成候選設定排名。
        /// <para>值：Any nonnegative integer; that is, an integer in the interval [0, BIGINT]. The default value of this parameter changes with each release.</para>
        /// <para><b>上限 2100000000</b>，超過會被拒（Error 1015 Parameter value too big）。官方 Parameters Reference 只寫「any integer」沒給上限，這個數字是 CPLEX 自己回報的：<c>set randomseed 2147483647</c> 會回「Value can be no larger than 2100000000」。</para>
        /// <para>分類：重複量測 —— 同一設定再量一次用的，NEVER 當候選設定排名。</para>
        /// </summary>
        public int? Seed { get; set; }

        #endregion

        #region 搜尋策略 · emphasis 與搜尋分支（18）

        // 改求解路徑、終點不變。
        // 可進 variant 池，但一輪只改一顆。

        /// <summary>
        /// <c>Param.Advance</c>（Interactive <c>advance</c>）—— 是否使用 advanced basis / 起始向量。
        /// <para>值：0 = Do not use advanced start information｜1 = Use an advanced basis supplied by the user（預設）｜2 = Crush an advanced basis or starting vector supplied by the user</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? AdvancedStart { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.Backtrack</c>（Interactive <c>mip strategy backtrack</c>）—— 回溯容差：越小越傾向繼續往深處走。
        /// <para>值：Any number from 0.0 to 1.0; default : 0.9999</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public double? BacktrackTolerance { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.BBInterval</c>（Interactive <c>mip strategy bbinterval</c>）—— best-estimate 搜尋中每隔幾個節點強制選一次 best-bound。
        /// <para>值：0 = Never select best bound node; always select best estimate｜1 = Always select best bound node｜7 = Select best bound node occasionally（預設）｜Any positive integer = Select best bound node less frequently than best estimate node</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public long? BestBoundInterval { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.Branch</c>（Interactive <c>mip strategy branch</c>）—— 分支方向：先試哪一邊。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? BranchDirection { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.Dive</c>（Interactive <c>mip strategy dive</c>）—— 潛降策略。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Traditional dive｜2 = Probing dive｜3 = Guided dive</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? DiveType { get; set; }

        /// <summary>
        /// <c>Param.Emphasis.MIP</c>（Interactive <c>emphasis mip</c>）—— MIP emphasis：整體偏向可行解、最佳性還是推 bound。
        /// <para>值：0 = Balance optimality and feasibility（預設）｜1 = Emphasize feasibility over optimality｜2 = Emphasize optimality over feasibility｜3 = Emphasize moving best bound｜4 = Emphasize finding hidden feasible solutions｜5 = Emphasize finding high quality feasible solutions earlier</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? Emphasis { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.KappaStats</c>（Interactive <c>mip strategy kappastats</c>）—— 計算 MIP kappa（條件數）統計，判斷數值不穩的量化證據。
        /// <para>值：–1 = No MIP kappa statistics｜0 = Automatic: let CPLEX decide（預設）｜1 = Compute MIP kappa for a sample of subproblems｜2 = Compute MIP kappa for all subproblems</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? KappaStatistics { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.Search</c>（Interactive <c>mip strategy search</c>）—— 搜尋模式：dynamic search 或傳統 B&amp;C。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Apply traditional branch and cut strategy; disable dynamic search｜2 = Apply dynamic search</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? MipSearch { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.NodeSelect</c>（Interactive <c>mip strategy nodeselect</c>）—— 節點選擇策略。
        /// <para>值：0 = Depth-first search｜1 = Best-bound search（預設）｜2 = Best-estimate search｜3 = Alternative best-estimate search</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? NodeSelect { get; set; }

        /// <summary>
        /// <c>Param.Emphasis.Numerical</c>（Interactive <c>emphasis numerical</c>）—— 數值穩定優先（犧牲速度換精度）。
        /// <para>值：0 = Do not emphasize numerical precision（預設）｜1 = Exercise extreme caution in computation</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public bool? NumericalEmphasis { get; set; }

        /// <summary>
        /// <c>Param.OptimalityTarget</c>（Interactive <c>optimalitytarget</c>）—— 非凸 QP 要求全域最佳還是局部最佳。
        /// <para>值：0 = Automatic: let CPLEX decide（預設）｜1 = Searches for a globally optimal solution to a convex model｜2 = Searches for a solution that satisfies first-order optimality conditions, but is not necessarily globally optimal｜3 = Searches for a globally optimal solution to a nonconvex model; changes problem type to MIQP if necessary</para>
        /// <para><b>值 2（非凸問題求全域最佳）不能用在 MIP</b>，會丟 <c>Error 1017: Not available for mixed-integer problems</c>。</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? OptimalityTarget { get; set; }

        /// <summary>
        /// <c>Param.MIP.OrderType</c>（Interactive <c>mip ordertype</c>）—— 沒有優先序時讓 CPLEX 自動產生一份，依什麼規則產。
        /// <para>值：0 = Do not generate a priority order｜1 = Use decreasing cost｜2 = Use increasing bound range｜3 = Use increasing cost per coefficient count</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? PriorityOrderType { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.Probe</c>（Interactive <c>mip strategy probe</c>）—— 探測強度。
        /// <para>值：-1 = No probing｜0 = Automatic: let CPLEX choose（預設）｜1 = Moderate probing level｜2 = Aggressive probing level｜3 = Very aggressive probing level</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? Probe { get; set; }

        /// <summary>
        /// <c>Param.SolutionType</c>（Interactive <c>solutiontype</c>）—— LP/QP 要回基底解還是非基底解。
        /// <para>值：0 = Automatic: let CPLEX decide（預設）｜1 = CPLEX computes a basic solution｜2 = CPLEX computes a primal-dual pair of solution-vectors</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? SolutionType { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.StrongCand</c>（Interactive <c>mip limits strongcand</c>）—— strong branching 候選清單長度。
        /// <para>值：Any positive number; default : 10.</para>
        /// <para>最小值 1（預設 10），設 0 會被拒（Error 1014）。</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? StrongBranchingCandidateLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.StrongIt</c>（Interactive <c>mip limits strongit</c>）—— strong branching 每個候選跑幾次 simplex 迭代。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜Any positive integer = Limit of the simplex iterations performed on each candidate variable</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public long? StrongBranchingIterationLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.Order</c>（Interactive <c>mip strategy order</c>）—— 是否套用分支優先序。
        /// <para>值：0 = Off: do not use priority order｜1 = On: use priority order, if it exists（預設）</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public bool? UsePriorityOrder { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.VariableSelect</c>（Interactive <c>mip strategy variableselect</c>）—— 分支變數選擇策略。
        /// <para>值：-1 = Branch on variable with minimum infeasibility｜0 = Automatic: let CPLEX choose variable to branch on（預設）｜1 = Branch on variable with maximum infeasibility｜2 = Branch based on pseudo costs｜3 = Strong branching｜4 = Branch based on pseudo reduced costs</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? VariableSelect { get; set; }

        #endregion

        #region 搜尋策略 · 啟發式與 solution polishing（13）

        // 找可行解、改善 incumbent。
        // 找不到解或 gap 收不下來時的主力。

        /// <summary>
        /// <c>Param.MIP.Strategy.CardLs</c>（Interactive <c>mip strategy cardls</c>）—— 基數限制式的區域搜尋啟發式。
        /// <para>值：-1 = None: do not apply the CLSH（預設）｜0 = Automatic: let CPLEX choose｜1 = Apply the CLSH only at the root node｜2 = Apply the CLSH at the nodes of the branch and bound tree</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? CardinalityLocalSearch { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.FPHeur</c>（Interactive <c>mip strategy fpheur</c>）—— 可行解幫浦：專門對付「連第一個可行解都找不到」。
        /// <para>值：-1 = Do not apply the feasibility pump heuristic｜0 = Automatic: let CPLEX choose（預設）｜1 = Apply the feasibility pump heuristic with an emphasis on finding a feasible solution｜2 = Apply the feasibility pump heuristic with an emphasis on finding a feasible solution with a good objective value</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? FeasibilityPumpHeuristic { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.HeuristicEffort</c>（Interactive <c>mip strategy heuristiceffort</c>）—— 啟發式投入程度（0 關閉 / 1 預設 / &gt;1 更積極）。
        /// <para>值：0 = Disable heuristics｜&lt;1 = Decrease effort｜1 = default｜&gt;1 = Increase effort</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public double? HeuristicEffort { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.HeuristicFreq</c>（Interactive <c>mip strategy heuristicfreq</c>）—— 週期性啟發式的頻率：每 N 個節點跑一次。
        /// <para>值：-1 = None｜0 = Automatic: let CPLEX choose（預設）｜Any positive integer = Apply the periodic heuristic at this frequency</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public long? HeuristicFrequency { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.LBHeur</c>（Interactive <c>mip strategy lbheur</c>）—— local branching：對每個新 incumbent 再試著改善。
        /// <para>值：0 = Local branching heuristic is off（預設）｜1 = Apply local branching heuristic to new incumbent</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public bool? LocalBranchingHeuristic { get; set; }

        /// <summary>
        /// <c>Param.MIP.PolishAfter.AbsMIPGap</c>（Interactive <c>mip polishafter absmipgap</c>）—— 絕對 gap 收到多小之後開始 polishing。
        /// <para>值：Any nonnegative value; default : 0.0.</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public double? PolishAfterAbsoluteMipGap { get; set; }

        /// <summary>
        /// <c>Param.MIP.PolishAfter.DetTime</c>（Interactive <c>mip polishafter dettime</c>）—— 跑滿幾個 ticks 後開始 polishing（決定論版）。
        /// <para>值：Any nonnegative value in deterministic ticks; default :1.0E+75 ticks.</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public double? PolishAfterDetTime { get; set; }

        /// <summary>
        /// <c>Param.MIP.PolishAfter.MIPGap</c>（Interactive <c>mip polishafter mipgap</c>）—— 相對 gap 收到多小之後開始 polishing。
        /// <para>值：Any number from 0.0 to 1.0, inclusive; default : 0.0.</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public double? PolishAfterMipGap { get; set; }

        /// <summary>
        /// <c>Param.MIP.PolishAfter.Nodes</c>（Interactive <c>mip polishafter nodes</c>）—— 處理幾個節點後開始 polishing。
        /// <para>值：Any nonnegative integer; default : 9223372036800000000</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public long? PolishAfterNodes { get; set; }

        /// <summary>
        /// <c>Param.MIP.PolishAfter.Solutions</c>（Interactive <c>mip polishafter solutions</c>）—— 找到幾個整數解後開始 polishing。
        /// <para>值：Any positive integer strictly greater than zero; zero is not allowed; default : 9223372036800000000</para>
        /// <para>最小值 1，設 0 會被拒（Error 1014）。</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public long? PolishAfterSolutions { get; set; }

        /// <summary>
        /// <c>Param.MIP.PolishAfter.Time</c>（Interactive <c>mip polishafter time</c>）—— 跑滿幾秒後開始 solution polishing。
        /// <para>值：Any nonnegative value in seconds; default :1.0E+75 seconds.</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public double? PolishAfterTime { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.RepairTries</c>（Interactive <c>mip limits repairtries</c>）—— MIP start 不可行時嘗試修復幾次。
        /// <para>值：-1 = None: do not try to repair｜0 = Automatic: let CPLEX choose（預設）｜Any positive integer = Number of attempts</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public long? RepairTries { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.RINSHeur</c>（Interactive <c>mip strategy rinsheur</c>）—— RINS 啟發式頻率。
        /// <para>值：-1 = None: do not apply RINS heuristic｜0 = Automatic: let CPLEX choose（預設）｜Any positive integer = Frequency to apply RINS heuristic</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public long? RinsHeuristicFrequency { get; set; }

        #endregion

        #region 搜尋策略 · 切割平面（22）

        // 推 dual bound 用。
        // 每族 -1 關閉 / 0 自動 / 1..N 漸積極。

        /// <summary>
        /// <c>Param.MIP.Limits.AggForCut</c>（Interactive <c>mip limits aggforcut</c>）—— 生 cut 時最多聚合幾條限制式。
        /// <para>值：Any nonnegative integer; default : 3</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? AggregationLimitForCut { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.BQP</c>（Interactive <c>mip cuts bqp</c>）—— Boolean Quadric Polytope cuts（非凸 QP / MIQP）。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? BqpCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.Cliques</c>（Interactive <c>mip cuts cliques</c>）—— clique cuts。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? CliqueCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.Covers</c>（Interactive <c>mip cuts covers</c>）—— cover cuts。
        /// <para>值：-1 = Do not generate cover cuts｜0 = Automatic: let CPLEX choose（預設）｜1 = Generate cover cuts moderately｜2 = Generate cover cuts aggressively｜3 = Generate cover cuts very aggressively</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? CoverCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.CutPasses</c>（Interactive <c>mip limits cutpasses</c>）—— cut 生成回合數。
        /// <para>值：-1 = None｜0 = Automatic: let CPLEX choose（預設）｜Any positive integer = Number of passes to perform</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public long? CutPasses { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.CutsFactor</c>（Interactive <c>mip limits cutsfactor</c>）—— cut 總數上限倍數（相對原始列數）。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public double? CutsFactor { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.Disjunctive</c>（Interactive <c>mip cuts disjunctive</c>）—— disjunctive cuts。
        /// <para>值：-1 = Do not generate disjunctive cuts｜0 = Automatic: let CPLEX choose（預設）｜1 = Generate disjunctive cuts moderately｜2 = Generate disjunctive cuts aggressively｜3 = Generate disjunctive cuts very aggressively</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? DisjunctiveCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.EachCutLimit</c>（Interactive <c>mip limits eachcutlimit</c>）—— 每一族 cut 各自的數量上限。
        /// <para>值：0 = No cuts｜Any positive number = Limit each type of cut｜2100000000 = default</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? EachCutLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.FlowCovers</c>（Interactive <c>mip cuts flowcovers</c>）—— flow cover cuts。
        /// <para>值：-1 = Do not generate flow cover cuts｜0 = Automatic: let CPLEX choose（預設）｜1 = Generate flow cover cuts moderately｜2 = Generate flow cover cuts aggressively</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? FlowCoverCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.PathCut</c>（Interactive <c>mip cuts pathcut</c>）—— flow path cuts。
        /// <para>值：-1 = Do not generate flow path cuts｜0 = Automatic: let CPLEX choose（預設）｜1 = Generate flow path cuts moderately｜2 = Generate flow path cuts aggressively</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? FlowPathCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.GomoryCand</c>（Interactive <c>mip limits gomorycand</c>）—— Gomory cut 的候選數上限。
        /// <para>值：Any positive integer; default : 200.</para>
        /// <para>最小值 1（預設 200），設 0 會被拒（Error 1014）。</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? GomoryCandidateLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.Gomory</c>（Interactive <c>mip cuts gomory</c>）—— Gomory fractional cuts。
        /// <para>值：-1 = Do not generate Gomory fractional cuts｜0 = Automatic: let CPLEX choose（預設）｜1 = Generate Gomory fractional cuts moderately｜2 = Generate Gomory fractional cuts aggressively</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? GomoryCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.GomoryPass</c>（Interactive <c>mip limits gomorypass</c>）—— Gomory cut 的回合數上限。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜Any positive integer = Number of passes to generate Gomory fractional cuts</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public long? GomoryPassLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.GUBCovers</c>（Interactive <c>mip cuts gubcovers</c>）—— GUB cover cuts：對「一組 0-1 變數只能挑一個」的結構有效。
        /// <para>值：-1 = Do not generate GUB cuts｜0 = Automatic: let CPLEX choose（預設）｜1 = Generate GUB cuts moderately｜2 = Generate GUB cuts aggressively</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? GubCoverCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.Implied</c>（Interactive <c>mip cuts implied</c>）—— 全域有效的 implied bound cuts：big-M / indicator 結構的標配。
        /// <para>值：-1 = Do not generate implied bound cuts｜0 = Automatic: let CPLEX choose（預設）｜1 = Generate implied bound cuts moderately｜2 = Generate implied bound cuts aggressively</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? ImpliedBoundCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.LiftProj</c>（Interactive <c>mip cuts liftproj</c>）—— lift-and-project cuts。
        /// <para>值：-1 = Do not generate lift-and-project cuts｜0 = Automatic: let CPLEX choose（預設）｜1 = Generate lift-and-project cuts moderately｜2 = Generate lift-and-project cuts aggressively｜3 = Generate lift-and-project cuts very aggressively</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? LiftAndProjectCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.LocalImplied</c>（Interactive <c>mip cuts localimplied</c>）—— 只在子樹內有效的 implied bound cuts。
        /// <para>值：-1 = Do not generate locally valid implied bound cuts｜0 = Automatic: let CPLEX choose（預設）｜1 = Generate locally valid implied bound cuts moderately｜2 = Generate locally valid implied bound cuts aggressively｜3 = Generate locally valid implied bound cuts very aggressively</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? LocalImpliedBoundCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.MCFCut</c>（Interactive <c>mip cuts mcfcut</c>）—— multi-commodity flow cuts：網路流結構。
        /// <para>值：-1 = Turn off MCF cuts｜0 = Automatic: let CPLEX decide whether to generate MCF cuts（預設）｜1 = Generate a moderate number of MCF cuts｜2 = Generate MCF cuts aggressively</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? McfCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.MIRCut</c>（Interactive <c>mip cuts mircut</c>）—— mixed-integer rounding cuts。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? MirCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.Nodecuts</c>（Interactive <c>mip cuts nodecuts</c>）—— root 之外的節點要不要繼續生 cut。
        /// <para>值：-1 = Do not generate node cuts｜0 = Automatic: let CPLEX choose（預設）｜1 = Generate node cuts moderately｜2 = Generate node cuts aggressively｜3 = Generate node cuts very aggressively</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? NodeCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.RLT</c>（Interactive <c>mip cuts rlt</c>）—— Reformulation Linearization Technique cuts（非凸 QP / MIQP）。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? RltCuts { get; set; }

        /// <summary>
        /// <c>Param.MIP.Cuts.ZeroHalfCut</c>（Interactive <c>mip cuts zerohalfcut</c>）—— zero-half cuts：對純 0-1 問題特別有效。
        /// <para>值：-1 = Do not generate zero-half cuts｜0 = Automatic: let CPLEX choose（預設）｜1 = Generate zero-half cuts moderately｜2 = Generate zero-half cuts aggressively</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? ZeroHalfCuts { get; set; }

        #endregion

        #region 搜尋策略 · 前處理（16）

        // presolve、對稱破除、縮放。

        /// <summary>
        /// <c>Param.Preprocessing.Fill</c>（Interactive <c>preprocessing fill</c>）—— 聚合器允許產生的 fill-in 量。
        /// <para>值：Any nonnegative integer; default : 10</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public long? AggregatorFill { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Aggregator</c>（Interactive <c>preprocessing aggregator</c>）—— 前處理聚合器套用次數上限。
        /// <para>值：-1 = Automatic (1 for LP, infinite for MIP) default｜0 = Do not use any aggregator｜Any positive integer = Number of times to apply aggregator</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? AggregatorLimit { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.BoundStrength</c>（Interactive <c>preprocessing boundstrength</c>）—— 界限收緊。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? BoundStrengthening { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.CoeffReduce</c>（Interactive <c>preprocessing coeffreduce</c>）—— 係數縮減，會影響 LP 鬆弛的緊度。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? CoefficientReduction { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Dependency</c>（Interactive <c>preprocessing dependency</c>）—— 偵測並移除相依（重複）的限制式。
        /// <para>值：-1 = Automatic: let CPLEX choose（預設）｜0 = Off: do not use dependency checker｜1 = Turn on only at the beginning of preprocessing｜2 = Turn on only at the end of preprocessing｜3 = Turn on at the beginning and at the end of preprocessing</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? DependencyCheck { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Folding</c>（Interactive <c>preprocessing folding</c>）—— 純 LP 的 folding 縮減。
        /// <para>值：-1 = Automatic: let CPLEX choose（預設）｜0 = Turn off folding｜1 = Exert a moderate level of folding｜2 = Exert an aggressive level of folding｜3 = Exert a very aggressive level of folding｜4 = Exert a highly aggressive level of folding｜5 = Exert an extremely aggressive level of folding</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? LpFolding { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.PresolveNode</c>（Interactive <c>mip strategy presolvenode</c>）—— 節點上要不要做 presolve；單節點太貴時的正面手段。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? NodePresolve { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Presolve</c>（Interactive <c>preprocessing presolve</c>）—— 是否啟用前處理。infeasible 找不出原因時可關掉它再跑 IIS。
        /// <para>值：0 = Do not apply presolve｜1 = Apply presolve（預設）</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public bool? PreIndicator { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Dual</c>（Interactive <c>preprocessing dual</c>）—— 是否對 LP 走對偶形式做 presolve。
        /// <para>值：-1 = Turn off this feature｜0 = Automatic: let CPLEX choose（預設）｜1 = Turn on this feature</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? PresolveDual { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.NumPass</c>（Interactive <c>preprocessing numpass</c>）—— presolve 回合數上限。
        /// <para>值：-1 = Automatic: let CPLEX choose; presolve continues as long as helpful（預設）｜0 = Do not use presolve; other reductions may still occur｜Any positive integer = Apply presolve specified number of times</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? PresolvePasses { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Reduce</c>（Interactive <c>preprocessing reduce</c>）—— 做 primal / dual / 兩者 / 都不做 的縮減。
        /// <para>值：0 = No primal or dual reductions｜1 = Only primal reductions｜2 = Only dual reductions｜3 = Both primal and dual reductions（預設）</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? PresolveReduce { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Reformulations</c>（Interactive <c>preprocessing reformulations</c>）—— 允許哪些 presolve reformulation。
        /// <para>值：0 = no reformulations｜1 = allow reformulations that interfere with crushing forms｜2 = allow reformulations that interfere with uncrushing forms｜3 = allow all reformulations（預設）</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? PresolveReformulations { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Relax</c>（Interactive <c>preprocessing relax</c>）—— root 鬆弛是否額外做一次 LP presolve。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? RelaxedLpPresolve { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.RepeatPresolve</c>（Interactive <c>preprocessing repeatpresolve</c>）—— root 處理完後要不要重跑一次 presolve。
        /// <para>值：-1 = Automatic: let CPLEX choose（預設）｜0 = Turn off represolve｜1 = Represolve without cuts｜2 = Represolve with cuts｜3 = Represolve with cuts and allow new root cuts</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? RepeatPresolve { get; set; }

        /// <summary>
        /// <c>Param.Read.Scale</c>（Interactive <c>read scale</c>）—— 矩陣縮放方式：係數量級差距大時的第二手段（第一手段是 NumericalEmphasis）。
        /// <para>值：-1 = No scaling｜0 = Equilibration scaling（預設）｜1 = More aggressive scaling</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? Scaling { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.Symmetry</c>（Interactive <c>preprocessing symmetry</c>）—— 對稱破除強度：排班、指派這類同質資源的題目值得試。
        /// <para>值：-1 = Automatic: let CPLEX choose（預設）｜0 = Turn off symmetry breaking｜1 = Exert a moderate level of symmetry breaking｜2 = Exert an aggressive level of symmetry breaking｜3 = Exert a very aggressive level of symmetry breaking｜4 = Exert a highly aggressive level of symmetry breaking｜5 = Exert an extremely aggressive level of symmetry breaking</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? Symmetry { get; set; }

        #endregion

        #region 搜尋策略 · root 與節點的 LP 演算法（25）

        // Simplex / Barrier / Sifting / Network 的選擇與內部設定。

        /// <summary>
        /// <c>Param.Barrier.Algorithm</c>（Interactive <c>barrier algorithm</c>）—— barrier 演算法選擇。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? BarrierAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Barrier.ColNonzeros</c>（Interactive <c>barrier colnonzeros</c>）—— 被視為稠密行的非零數門檻。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? BarrierColumnNonzeros { get; set; }

        /// <summary>
        /// <c>Param.Barrier.Limits.Corrections</c>（Interactive <c>barrier limits corrections</c>）—— barrier 中心化修正次數上限。
        /// <para>值：-1 = Automatic; let CPLEX choose（預設）｜0 = None｜Any positive integer = Maximum number of centering corrections per iteration</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public long? BarrierCorrectionLimit { get; set; }

        /// <summary>
        /// <c>Param.Barrier.Crossover</c>（Interactive <c>barrier crossover</c>）—— barrier 之後要不要 crossover 回基底解。
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? BarrierCrossover { get; set; }

        /// <summary>
        /// <c>Param.Barrier.Limits.Growth</c>（Interactive <c>barrier limits growth</c>）—— barrier 不穩定判定的成長上限。
        /// <para>值：1.0 or greater; default : 1e12.</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public double? BarrierGrowthLimit { get; set; }

        /// <summary>
        /// <c>Param.Barrier.Limits.ObjRange</c>（Interactive <c>barrier limits objrange</c>）—— barrier 目標值的可接受範圍。
        /// <para>值：Any nonnegative number; default : 1e20</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public double? BarrierObjectiveRange { get; set; }

        /// <summary>
        /// <c>Param.Barrier.Ordering</c>（Interactive <c>barrier ordering</c>）—— barrier 的排序演算法。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Approximate minimum degree (AMD)｜2 = Approximate minimum fill (AMF)｜3 = Nested dissection (ND)</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? BarrierOrdering { get; set; }

        /// <summary>
        /// <c>Param.Barrier.StartAlg</c>（Interactive <c>barrier startalg</c>）—— barrier 起始點演算法。
        /// <para>最小值 1，設 0 會被拒（Error 1014）。</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? BarrierStartAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Simplex.DGradient</c>（Interactive <c>simplex dgradient</c>）—— dual simplex 定價法。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Standard dual pricing｜2 = Steepest-edge pricing｜3 = Steepest-edge pricing in slack space｜4 = Steepest-edge pricing, unit initial norms｜5 = devex pricing</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? DualSimplexPricing { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Tolerances.Markowitz</c>（Interactive <c>simplex tolerances markowitz</c>）—— Markowitz 樞紐容差：數值不穩時調大。
        /// <para>值：Any number from 0.0001 to 0.99999; default : 0.01.</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public double? MarkowitzTolerance { get; set; }

        /// <summary>
        /// <c>Param.Network.NetFind</c>（Interactive <c>network netfind</c>）—— 從模型中萃取網路結構的積極程度。
        /// <para>值：1 = Extract pure network only｜2 = Try reflection scaling（預設）｜3 = Try general scaling</para>
        /// <para>最小值 1（預設 2），設 0 會被拒（Error 1014）。</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? NetworkExtractionLevel { get; set; }

        /// <summary>
        /// <c>Param.Network.Pricing</c>（Interactive <c>network pricing</c>）—— network simplex 定價法。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Partial pricing｜2 = Multiple partial pricing｜3 = Multiple partial pricing with sorting</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? NetworkPricing { get; set; }

        /// <summary>
        /// <c>Param.NodeAlgorithm</c>（Interactive <c>mip strategy subalgorithm</c>）—— 子問題（非根節點）用哪個連續最佳化器。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Primal simplex｜2 = Dual simplex｜3 = Network simplex｜4 = Barrier｜5 = Sifting</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? NodeAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Simplex.PGradient</c>（Interactive <c>simplex pgradient</c>）—— primal simplex 定價法。
        /// <para>值：-1 = Reduced-cost pricing｜0 = Hybrid reduced-cost &amp; devex pricing（預設）｜1 = Devex pricing｜2 = Steepest-edge pricing｜3 = Steepest-edge pricing with slack initial norms｜4 = Full pricing</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? PrimalSimplexPricing { get; set; }

        /// <summary>
        /// <c>Param.RootAlgorithm</c>（Interactive <c>mip strategy startalgorithm</c>）—— root 鬆弛用哪個連續最佳化器。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Primal Simplex｜2 = Dual Simplex｜3 = Network Simplex｜4 = Barrier｜5 = Sifting｜6 = Concurrent (Dual, Barrier, and Primal in opportunistic mode; Dual and Barrier in deterministic mode)</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? RootAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Sifting.Algorithm</c>（Interactive <c>sifting algorithm</c>）—— sifting 子問題用哪個演算法。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Primal Simplex｜2 = Dual Simplex｜3 = Network Simplex｜4 = Barrier</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? SiftingAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Sifting.Simplex</c>（Interactive <c>sifting simplex</c>）—— 是否允許 simplex 內部切換到 sifting。
        /// <para>值：1 = default CPLEX executes sifting during simplex optimization under appropriate conditions｜0 = CPLEX turns off sifting during simplex optimization</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public bool? SiftingFromSimplex { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Crash</c>（Interactive <c>simplex crash</c>）—— 起始基底的 crash 排序。
        /// <para>值：LP Primal = LP Primal｜-1 = Alternate ways of using objective coefficients｜0 = Ignore objective coefficients during crash｜1 = Alternate ways of using objective coefficients（預設）｜LP Dual = LP Dual｜-1 = Aggressive starting basis｜0 = Aggressive starting basis｜1 = Default starting basis（預設）｜QP Primal = QP Primal｜-1 = Slack basis｜0 = Ignore Q terms and use LP solver for crash｜1 = Ignore objective and use LP solver for crash（預設）｜QP Dual = QP Dual｜-1 = Slack basis｜0 = Use Q terms for crash｜1 = Use Q terms for crash（預設）</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? SimplexCrash { get; set; }

        /// <summary>
        /// <c>Param.Simplex.DynamicRows</c>（Interactive <c>simplex dynamicrows</c>）—— dual simplex 的動態列管理。
        /// <para>值：-1 = automatic: Let CPLEX decide. default｜0 = Tell CPLEX to keep all rows｜1 = Let CPLEX manage rows</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? SimplexDynamicRows { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Perturbation.Constant</c>（Interactive <c>simplex perturbationlimit no/yes C</c>）—— 擾動常數。
        /// <para>值：Any positive number greater than or equal to 1e-8; default : 1e-6.</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public double? SimplexPerturbationConstant { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Perturbation.Indicator</c>（Interactive <c>simplex perturbationlimit</c>）—— 一開始就強制擾動（退化嚴重時）。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Turn on perturbation from beginning</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public bool? SimplexPerturbationIndicator { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Limits.Perturbation</c>（Interactive <c>simplex limits perturbation</c>）—— 停滯幾次之後自動擾動。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜Any positive integer = Number of degenerate iterations before perturbation</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? SimplexPerturbationLimit { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Pricing</c>（Interactive <c>simplex pricing</c>）—— 定價候選清單大小。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜Any positive integer = Number of pricing candidates</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? SimplexPricingCandidateList { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Refactor</c>（Interactive <c>simplex refactor</c>）—— 重新分解基底的頻率。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜Integer from 1 to 10 000 = Number of iterations between refactoring of the basis matrix</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? SimplexRefactorFrequency { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Limits.Singularity</c>（Interactive <c>simplex limits singularity</c>）—— 奇異基底修復次數上限。
        /// <para>值：Any nonnegative integer; default : 10.</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池，一輪只改一顆。</para>
        /// </summary>
        public int? SimplexSingularityLimit { get; set; }

        #endregion

        #region 搜尋策略 · 特定模型類別（15）

        // Benders、QP / MIQCP、SOS、subMIP、FeasOpt。
        // 模型沒有對應結構時設了也沒作用。

        /// <summary>
        /// <c>Param.Benders.Tolerances.FeasibilityCut</c>（Interactive <c>benders tolerances feasibilitycut</c>）—— Benders 可行性割的容差。
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public double? BendersFeasibilityCutTol { get; set; }

        /// <summary>
        /// <c>Param.Benders.Tolerances.OptimalityCut</c>（Interactive <c>benders tolerances optimalitycut</c>）—— Benders 最佳性割的容差。
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public double? BendersOptimalityCutTol { get; set; }

        /// <summary>
        /// <c>Param.Benders.Strategy</c>（Interactive <c>benders strategy value</c>）—— Benders 分解策略；模型要有 Benders annotation 才有作用。
        /// <para>值：-1 = Execute conventional branch and bound; ignore any Benders annotations. That is, do not use Benders algorithm even if a Benders partition of the current model is present｜0 = default Let CPLEX decide. case 1: If the user supplies no annotations with the model, CPLEX executes conventional branch and bound. case 2: If annotations specifying a Benders partition of the current model are available, CPLEX attempts to decompose the model. CPLEX uses the master as given by the annotations, and attempts to partition the subproblems further, if possible, before applying Benders algorithm to solve the model. If the user supplied annotations, but the annotations supplied do not lead to a complete decomposition into master and disjoint subproblems (that is, if the annotations are wrong in that sense), CPLEX produces the error CPXERR_BAD_DECOMPOSITION ｜1 = CPLEX applies Benders algorithm to a decomposition based on annotations supplied by the user. If no annotations to decompose the model are available, this setting produces the error CPXERR_NO_DECOMPOSITION . If the user supplies annotations, but the supplied annotations do not lead to a complete partition of the original model into disjoint master and subproblems, then this setting produces the error CPXERR_BAD_DECOMPOSITION ｜2 = CPLEX accepts the master as given and attempts to decompose the remaining elements into disjoint subproblems to assign to workers. It then solves the Benders decomposition of the model. If no annotations to decompose the model are available, this setting produces the error CPXERR_NO_DECOMPOSITION . If the user supplies annotations, but the supplied annotations do not lead to a complete partition of the original model into disjoint master and subproblems, then this setting produces the error CPXERR_BAD_DECOMPOSITION ｜3 = CPLEX ignores any annotation file supplied with the model; CPLEX applies presolve; CPLEX then automatically generates a Benders partition, putting integer variables in master and continuous linear variables into disjoint subproblems. CPLEX then solves the Benders decomposition of the model. If the problem is a strictly linear program (LP), that is, there are no integer-constrained variables to put into master, then CPLEX reports the error CPXERR_PARAM_INCOMPATIBLE . If the problem is a mixed integer linear program (MILP) where all variables are integer-constrained, (that is, there are no continuous linear variables to decompose into disjoint subproblems) then CPLEX reports the error CPXERR_NO_DECOMPOSITION . If the problem is a mixed integer linear program (MILP) where all variables are continuous, (that is, there are no integer-constrained variables to decompose into master) then CPLEX reports the error CPXERR_NO_DECOMPOSITION </para>
        /// <para><b>模型沒有 Benders 分解註記時，設 1／2／3 會直接丟例外</b>（<c>Error 2000: No Benders decomposition available</c>），不是「設了沒作用」。只有 -1 與 0 在一般 MIP 上安全。</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public int? BendersStrategy { get; set; }

        /// <summary>
        /// <c>Param.Benders.WorkerAlgorithm</c>（Interactive <c>benders workeralgorithm value</c>）—— Benders 子問題用哪個演算法。
        /// <para>值：0 = default Let CPLEX decide｜1 = CPLEX applies the primal simplex algorithm to workers｜2 = CPLEX applies the dual simplex algorithm to workers｜3 = CPLEX applies the network simplex algorithm to workers｜4 = CPLEX applies the barrier algorithm to workers｜5 = CPLEX applies the sifting algorithm to workers</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public int? BendersWorkerAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.QCPDuals</c>（Interactive <c>preprocessing qcpduals</c>）—— 是否計算 QCP 的對偶值。
        /// <para>值：0 = no｜1 = if_possible｜2 = force</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public int? CalculateQcpDuals { get; set; }

        /// <summary>
        /// <c>Param.Feasopt.Mode</c>（Interactive <c>feasopt mode</c>）—— FeasOpt 放鬆不可行模型時的目標。
        /// <para>值：0 = Minimize the sum of all required relaxations in first phase only（預設）｜1 = Minimize the sum of all required relaxations in first phase and execute second phase to find optimum among minimal relaxations｜2 = Minimize the number of constraints and bounds requiring relaxation in first phase only｜3 = Minimize the number of constraints and bounds requiring relaxation in first phase and execute second phase to find optimum among minimal relaxations｜4 = Minimize the sum of squares of required relaxations in first phase only｜5 = Minimize the sum of squares of required relaxations in first phase and execute second phase to find optimum among minimal relaxations</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public int? FeasOptMode { get; set; }

        /// <summary>
        /// <c>Param.MIP.Strategy.MIQCPStrat</c>（Interactive <c>mip strategy miqcpstrat</c>）—— MIQCP 用哪種鬆弛。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Solve a QCP node relaxation at each node｜2 = Solve an LP node relaxation at each node</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public int? MiqcpStrategy { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.QPMakePSD</c>（Interactive <c>preprocessing qpmakepsd</c>）—— 把非凸二元 QP 重新表述成凸的。
        /// <para>值：0 = Turn off attempts to make binary model PSD｜1 = On: CPLEX attempts to make binary model PSD（預設）</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public bool? QpMakePsd { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.QToLin</c>（Interactive <c>preprocessing qtolin</c>）—— 把 QP / MIQP 的二次項線性化。
        /// <para>值：-1 = Automatic: let CPLEX decide ( default )｜0 = Off: CPLEX does not linearize quadratic terms in the objective function of QP, MIQP｜1 = On: CPLEX linearizes quadratic terms in the objective function of QP, MIQP</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public int? QpToLinear { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.SOS1Reform</c>（Interactive <c>preprocessing sos1reform</c>）—— SOS1 重新表述。
        /// <para>值：-1 = No reformulation: CPLEX does not reformulate special ordered sets of type 1 (SOS1) as linear constraints｜0 = Automatic: let CPLEX decide（預設）｜1 = Logarithmic: CPLEX reformulates special ordered sets of type 1 (SOS1) as linear constraints, with a reformulation which is logarithmic in the size of the special ordered sets</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public int? Sos1Reformulation { get; set; }

        /// <summary>
        /// <c>Param.Preprocessing.SOS2Reform</c>（Interactive <c>preprocessing sos2reform</c>）—— SOS2 重新表述。
        /// <para>值：-1 = No reformulation: CPLEX does not reformulate special ordered sets of type 2 (SOS2) as linear constraints｜0 = Automatic: let CPLEX decide（預設）｜1 = Logarithmic: CPLEX reformulates special ordered sets of type 2 (SOS2) as linear constraints, with a reformulation which is logarithmic in the size of the special ordered sets</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public int? Sos2Reformulation { get; set; }

        /// <summary>
        /// <c>Param.MIP.SubMIP.SubAlg</c>（Interactive <c>mip submip subalg</c>）—— subMIP 子問題的演算法。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Primal Simplex｜2 = Dual Simplex｜3 = Network Simplex｜4 = Barrier｜5 = Sifting</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public int? SubMipNodeAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.MIP.SubMIP.NodeLimit</c>（Interactive <c>mip submip nodelimit</c>）—— 啟發式內部解 subMIP 時的節點上限。
        /// <para>最小值 1，設 0 會被拒（Error 1014）。</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public long? SubMipNodeLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.SubMIP.StartAlg</c>（Interactive <c>mip submip startalg</c>）—— subMIP 初始鬆弛的演算法。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Primal Simplex｜2 = Dual Simplex｜3 = Network Simplex｜4 = Barrier｜5 = Sifting</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public int? SubMipRootAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.MIP.SubMIP.Scale</c>（Interactive <c>mip submip scale</c>）—— subMIP 的縮放設定。
        /// <para>值：-1 = No scaling｜0 = Equilibration scaling（預設）｜1 = More aggressive scaling</para>
        /// <para>分類：搜尋策略 —— 可進 variant 池；模型沒有對應結構時設了不會有作用。</para>
        /// </summary>
        public int? SubMipScaling { get; set; }

        #endregion

        #region 非 tuning 參數（35）

        // 輸出、顯示、讀檔上限、診斷、solution pool、CPLEX 內建 tune。
        // 這些不改求解策略，NEVER 放進 variant 池掃描。

        /// <summary>
        /// <c>Param.Barrier.Display</c>（Interactive <c>barrier display</c>）—— barrier log 詳細度。
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? BarrierDisplay { get; set; }

        /// <summary>
        /// <c>Param.Output.CloneLog</c>（Interactive <c>output clonelog</c>）—— 平行求解時每個 clone 各寫一份 log（診斷用）。
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public bool? CloneLog { get; set; }

        /// <summary>
        /// <c>Param.Read.Variables</c>（Interactive <c>read variables</c>）—— 讀檔時的變數（行）數量上限。
        /// <para>值：Any integer from 0 (zero) to CPX_BIGINT ; default : 60 000.</para>
        /// <para><b>IBM 自 V20.1.0 起將此參數標為過時</b>（仍可設定）。保留是因為 RowRead 早已是公開 API，
        /// 同族四顆一起留才不會出現「有的能設有的不能設」。新程式不建議依賴它們。</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? ColumnRead { get; set; }

        /// <summary>
        /// <c>Param.Conflict.Algorithm</c>（Interactive <c>conflict i</c>）—— conflict refiner 找最小衝突集的演算法；IIS 取證用。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Simple, fast algorithm｜2 = Bound propagation｜3 = Presolve｜4 = Irreducibly inconsistent set (IIS) for continuous models｜5 = Limited solve｜6 = Full solve</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? ConflictAlgorithm { get; set; }

        /// <summary>
        /// <c>Param.Conflict.Display</c>（Interactive <c>conflict display i</c>）—— conflict refiner 的 log 詳細度。
        /// <para>值：0 = No display｜1 = Summary display（預設）｜2 = Detailed display</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? ConflictDisplay { get; set; }

        /// <summary>
        /// <c>Param.Read.DataCheck</c>（Interactive <c>read datacheck</c>）—— 輸入資料一致性檢查與建模建議的層級。
        /// <para>值：0 = Data checking off; do not check（預設）｜1 = Data checking on（預設） in Python API｜2 = Data checking on; modeling assistance on; warnings issued for both</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? DataCheck { get; set; }

        /// <summary>
        /// <c>Param.Read.FileEncoding</c>（Interactive <c>read fileencoding</c>）—— 讀寫檔案的編碼。
        /// <para>值：valid string for the name of an encoding (code page); default : ISO-8859-1 or the empty string (“ “)</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public string FileEncoding { get; set; }

        /// <summary>
        /// <c>Param.Output.IntSolFilePrefix</c>（Interactive <c>output intsolfileprefix</c>）—— 每找到一個整數解就存檔的檔名前綴。
        /// <para>值：valid string for the prefix of a file name; default : ” “ (the empty string; that is, the switch is off)</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public string IntSolFilePrefix { get; set; }

        /// <summary>
        /// <c>Param.MIP.Display</c>（Interactive <c>mip display</c>）—— MIP 節點 log 的詳細度。
        /// <para>值：0 = No display until optimal solution has been found｜1 = Display integer feasible solutions｜2 = Display integer feasible solutions plus an entry at a frequency set by MIP node log interval （預設）｜3 = Display the number of cuts added since previous display; information about the processing of each successful MIP start; elapsed time in seconds and elapsed time in deterministic ticks for integer feasible solutions｜4 = Display information available from previous options and information about the LP subproblem at root｜5 = Display information available from previous options and information about the LP subproblems at root and at nodes</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? MipDisplay { get; set; }

        /// <summary>
        /// <c>Param.MIP.Interval</c>（Interactive <c>mip interval</c>）—— 每幾個節點印一行 log。
        /// <para>值：n &lt; 0 = Display new incumbents, and display a log line frequently at the beginning of solving and less frequently as solving progresses｜0 (zero) = automatic: let CPLEX decide the frequency to log nodes ( default )｜n &gt; 0 = Display new incumbents, and display a log line every n nodes</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public long? MipInterval { get; set; }

        /// <summary>
        /// <c>Param.Output.MPSLong</c>（Interactive <c>output mpslong</c>）—— MPS / REW 輸出的數值精度。
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public bool? MpsLongNumerics { get; set; }

        /// <summary>
        /// <c>Param.MultiObjective.Display</c>（Interactive <c>multiobjective display</c>）—— 多目標求解的 log 詳細度。
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? MultiObjectiveDisplay { get; set; }

        /// <summary>
        /// <c>Param.Network.Display</c>（Interactive <c>network display</c>）—— network simplex log 詳細度。
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? NetworkDisplay { get; set; }

        /// <summary>
        /// <c>Param.Read.Nonzeros</c>（Interactive <c>read nonzeros</c>）—— 讀檔時的非零元素數量上限。
        /// <para>值：Any integer from 0 to CPX_BIGINT or CPX_BIGLONG, depending on integer type; default : 250 000.</para>
        /// <para><b>IBM 自 V20.1.0 起將此參數標為過時</b>（仍可設定）。保留是因為 RowRead 早已是公開 API，
        /// 同族四顆一起留才不會出現「有的能設有的不能設」。新程式不建議依賴它們。</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public long? NonzeroRead { get; set; }

        /// <summary>
        /// <c>Param.ParamDisplay</c>（Interactive <c>—</c>）—— 求解前是否印出被改過的參數清單。
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public bool? ParamDisplay { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.Populate</c>（Interactive <c>mip limits populate</c>）—— populate 一次最多產生幾個解。
        /// <para>值：Any nonnegative integer; default: 20.</para>
        /// <para>官方文件寫「any nonnegative integer」，但實測設 0 會被拒（Error 1014），最小值是 1。</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? PopulateLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.ProbeDetTime</c>（Interactive <c>mip limits probedettime</c>）—— probing 花的 ticks 上限。
        /// <para>值：Any nonnegative number; default : 1e+75.</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public double? ProbeDetTimeLimit { get; set; }

        /// <summary>
        /// <c>Param.MIP.Limits.ProbeTime</c>（Interactive <c>mip limits probetime</c>）—— probing 花的時間上限（秒）。
        /// <para>值：Any nonnegative number; default : 1e+75.</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public double? ProbeTimeLimit { get; set; }

        /// <summary>
        /// <c>Param.Read.QPNonzeros</c>（Interactive <c>read qpnonzeros</c>）—— 讀檔時 Q 矩陣的非零元素上限。
        /// <para>值：Any integer from 0 (zero) to CPX_BIGINT or CPX_BIGLONG , depending on the type of integer; default : 5 000.</para>
        /// <para><b>IBM 自 V20.1.0 起將此參數標為過時</b>（仍可設定）。保留是因為 RowRead 早已是公開 API，
        /// 同族四顆一起留才不會出現「有的能設有的不能設」。新程式不建議依賴它們。</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public long? QpNonzeroRead { get; set; }

        /// <summary>
        /// <c>Param.Read.WarningLimit</c>（Interactive <c>read warninglimit</c>）—— 同一類讀檔警告最多印幾次。
        /// <para>值：n&gt;=0 = Limits number of warnings of each type displayed（預設） is 10</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public long? ReadWarningLimit { get; set; }

        /// <summary>
        /// <c>Param.Record</c>（Interactive <c>record yes</c>）—— 錄下這次呼叫序列供 IBM 重現問題（診斷用）。
        /// <para>值：0 = Recording is off by default｜1 = Turn on recording</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public bool? Record { get; set; }

        /// <summary>
        /// <c>Param.Read.Constraints</c>（Interactive <c>read constraints</c>）—— 讀檔時的限制式數量上限。
        /// <para>值：Any integer from 0 (zero) to CPX_BIGINT ; default : 30 000.</para>
        /// <para><b>IBM 自 V20.1.0 起將此參數標為過時</b>（仍可設定）。保留是因為 RowRead 早已是公開 API，
        /// 同族四顆一起留才不會出現「有的能設有的不能設」。新程式不建議依賴它們。</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? RowRead { get; set; } = 30000;

        /// <summary>
        /// <c>Param.Sifting.Display</c>（Interactive <c>sifting display</c>）—— sifting log 詳細度。
        /// <para>值：0 = No display of sifting information｜1 = Display major iterations（預設）｜2 = Display LP subproblem information within each sifting iteration</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? SiftingDisplay { get; set; }

        /// <summary>
        /// <c>Param.Simplex.Display</c>（Interactive <c>simplex display</c>）—— simplex 迭代 log 詳細度。
        /// <para>值：0 = No iteration messages until solution｜1 = Iteration information after each refactoring（預設）｜2 = Iteration information for each iteration</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? SimplexDisplay { get; set; }

        /// <summary>
        /// <c>Param.MIP.Pool.AbsGap</c>（Interactive <c>mip pool absgap</c>）—— 進 pool 的絕對品質門檻。
        /// <para>值：Any nonnegative real number; default : 1.0e+75.</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public double? SolutionPoolAbsGap { get; set; }

        /// <summary>
        /// <c>Param.MIP.Pool.Capacity</c>（Interactive <c>mip pool capacity</c>）—— solution pool 最多保留幾個解。
        /// <para>值：Any nonnegative integer; 0 (zero) turns off all features of the solution pool; default : 2100000000.</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? SolutionPoolCapacity { get; set; }

        /// <summary>
        /// <c>Param.MIP.Pool.Intensity</c>（Interactive <c>mip pool intensity</c>）—— 找額外解的積極程度。
        /// <para>值：0 = Automatic: let CPLEX choose（預設）｜1 = Mild: generate few solutions quickly｜2 = Moderate: generate a larger number of solutions｜3 = Aggressive: generate many solutions and expect performance penalty｜4 = Very aggressive: enumerate all practical solutions</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? SolutionPoolIntensity { get; set; }

        /// <summary>
        /// <c>Param.MIP.Pool.RelGap</c>（Interactive <c>mip pool relgap</c>）—— 進 pool 的相對品質門檻。
        /// <para>值：Any nonnegative real number; default : 1.0e+75.</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public double? SolutionPoolRelGap { get; set; }

        /// <summary>
        /// <c>Param.MIP.Pool.Replace</c>（Interactive <c>mip pool replace</c>）—— pool 滿了要替換掉哪一個。
        /// <para>值：0 = Replace the first solution (oldest) by the most recent solution; first in, first out（預設）｜1 = Replace the solution which has the worst objective｜2 = Replace solutions in order to build a set of diverse solutions</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? SolutionPoolReplace { get; set; }

        /// <summary>
        /// <c>Param.Tune.DetTimeLimit</c>（Interactive <c>tune dettimelimit</c>）—— 內建 tune 的 ticks 上限。
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public double? TuningDetTimeLimit { get; set; }

        /// <summary>
        /// <c>Param.Tune.Display</c>（Interactive <c>tune display</c>）—— 內建 tune 的 log 詳細度。
        /// <para>值：0 = Turn off display｜1 = Display standard, minimal reporting（預設）｜2 = Display standard report plus parameter settings being tried｜3 = Display exhaustive report and log</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? TuningDisplay { get; set; }

        /// <summary>
        /// <c>Param.Tune.Measure</c>（Interactive <c>tune measure</c>）—— CPLEX 內建 tune 的評分方式。
        /// <para>值：CPX_TUNE_AVERAGE = mean time（預設）｜CPX_TUNE_MINMAX = minmax time</para>
        /// <para>最小值 1，設 0 會被拒（Error 1014）。</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? TuningMeasure { get; set; }

        /// <summary>
        /// <c>Param.Tune.Repeat</c>（Interactive <c>tune repeat</c>）—— 內建 tune 對模型做幾次 permutation 重測。
        /// <para>值：Any nonnegative integer; default : 1 (one)</para>
        /// <para>官方文件寫「any nonnegative integer」，但實測設 0 會被拒（Error 1014），最小值是 1。</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? TuningRepeat { get; set; }

        /// <summary>
        /// <c>Param.Tune.TimeLimit</c>（Interactive <c>tune timelimit</c>）—— 內建 tune 的時間上限（秒）。
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public double? TuningTimeLimit { get; set; }

        /// <summary>
        /// <c>Param.Output.WriteLevel</c>（Interactive <c>output writelevel</c>）—— 寫 MST / SOL 檔時要包含哪些變數。
        /// <para>值：0 = Automatic: let CPLEX decide｜1 = CPLEX writes all variables and their values｜2 = CPLEX writes only discrete variables and their values｜3 = CPLEX writes only nonzero variables and their values｜4 = CPLEX writes only nonzero discrete variables and their values</para>
        /// <para>分類：非 tuning —— 不改求解路線，NEVER 放進 variant 池掃描。</para>
        /// </summary>
        public int? WriteLevel { get; set; }

        #endregion

        #region ITunableConfig 介面別名

        /// <summary>
        /// ITunableConfig 介面名，是 <see cref="PreIndicator"/> 的 int 視角：0 = off、非 0 = on、null = 用 CPLEX 預設。
        /// 兩者是同一顆旋鈕（<c>Param.Preprocessing.Presolve</c>），設任一邊都會反映到另一邊。
        /// </summary>
        public int? Presolve
        {
            get => PreIndicator.HasValue ? (PreIndicator.Value ? 1 : 0) : (int?)null;
            set => PreIndicator = value.HasValue ? value.Value != 0 : (bool?)null;
        }

        #endregion
    }
}
