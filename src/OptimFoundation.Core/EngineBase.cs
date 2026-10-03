using System;
using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Internal;

namespace OptimFoundation.Core
{
    #region Interfaces and Enums
    /// <summary>
    /// 各求解器共用的設定，例如時間上限、執行緒數與演算法選項。各求解器的 config 實作此介面，
    /// 將這些設定轉成該求解器使用的參數。null 表示使用求解器預設值。
    /// </summary>
    public interface ISolverConfig
    {
        /// <summary>求解時間上限（秒）；null = 不限制。</summary>
        double? TimeLimit { get; set; }

        /// <summary>相對 MIP gap 收斂門檻；null = 用 solver 預設。</summary>
        double? MipGap { get; set; }

        /// <summary>可用執行緒數；null = 由 solver 自行決定。</summary>
        int? Threads { get; set; }

        /// <summary>隨機種子（CPLEX randomSeed）。要重現結果就固定它。</summary>
        int? Seed { get; set; }

        /// <summary>求解重點（CPLEX mipEmphasis）。整數值的意思由各求解器決定，框架不轉換。</summary>
        int? Emphasis { get; set; }

        /// <summary>可行性容差（CPLEX epRHS / Solver Epsilon）。</summary>
        double? FeasibilityTol { get; set; }

        /// <summary>最佳性容差（CPLEX epOpt）。</summary>
        double? OptimalityTol { get; set; }

        /// <summary>根節點使用的 LP 演算法（CPLEX algorithm）。各整數值代表的演算法由求解器決定。</summary>
        int? RootAlgorithm { get; set; }

        /// <summary>前處理開關（CPLEX PreInd）；0 = 關閉。</summary>
        int? Presolve { get; set; }

        /// <summary>啟發式投入程度（CPLEX HeuristicEffort）。</summary>
        double? HeuristicEffort { get; set; }

        /// <summary>記憶體上限 MB（CPLEX workMemory）。</summary>
        double? MemoryLimitMb { get; set; }

        /// <summary>求解前的變數數量警告門檻。VariableCount 超過門檻時只記錄警告，仍會繼續求解；未覆寫時使用此預設值。</summary>
        int ScaleWarnThreshold => 10_000_000;
    }


    /// <summary>求解引擎共用的操作：建立模型、求解，以及讀取解值與求解紀錄。EngineBase 提供共用實作。</summary>
    public interface ISolverEngine : IDisposable
    {
        /// <summary>本引擎使用的求解設定。</summary>
        ISolverConfig Config { get; }

        /// <summary>求解狀態；未求解為 NotSolved。</summary>
        SolveStatus Status { get; }

        /// <summary>最近一次 Solve() 記錄的求解狀態、耗時與結果；尚未求解為 null。</summary>
        SolveMetrics LastMetrics { get; }

        /// <summary>目前模型的問題類型（LP / MILP / IP / BP）。每次讀取都依求解器內的變數與特殊結構重新判定，求解前即可讀取。</summary>
        ModelType ModelType { get; }

        /// <summary>建立求解器模型並套用設定；建立變數或限制式前必須先呼叫。</summary>
        void Build();

        /// <summary>求解。回傳 true 代表取得 Optimal 或 Feasible 解。</summary>
        bool Solve();

        /// <summary>取得目標式的解值；必須在求解成功後呼叫。</summary>
        double GetObjectiveValue();

        /// <summary>依變數全名取得解值（TypeName@s1@s2@…）；必須在求解成功後呼叫。</summary>
        double GetVariableValue(string name);

        /// <summary>取解結果字典；varTypeName = null 回傳所有變數，否則只回該型別（名稱為 TypeName 或以 "TypeName@" 開頭）。</summary>
        IReadOnlyDictionary<string, double> GetSolution(string varTypeName = null);

        /// <summary>
        /// 以「變數全名 → 值」提供一組 MIP start；名稱格式同 <see cref="GetSolution"/>，可直接把上一個 engine 的解接過來。
        /// 必須在模型建完、Solve() 之前呼叫。LP 模型不使用 MIP start，會記錄警告後略過。
        /// </summary>
        /// <returns>實際套用的變數數；略過時為 0。</returns>
        int AddMIPStart(IReadOnlyDictionary<string, double> values, string name = null);
    }

    /// <summary>特殊限制式的選用介面（SOS1/2、indicator、lazy）；只有支援的 solver 實作。</summary>
    public interface ISpecialConstraints<TVar, TExpr>
    {
        /// <summary>SOS1：這組變數中最多只有一個可以非零。</summary>
        void AddSOS1(IEnumerable<TVar> vars);

        /// <summary>SOS2：這組變數中最多兩個非零，且必須相鄰（分段線性常用）。</summary>
        void AddSOS2(IEnumerable<TVar> vars);

        /// <summary>indicator：binary = 1 時才強制 expr (sense) rhs 成立；可避免自己湊 Big-M。</summary>
        void AddIndicator(TVar binary, TExpr expr, ConstraintSense sense, double rhs);

        /// <summary>lazy constraint：先不放進模型，solver 找到候選解時才檢查並補上。</summary>
        void AddLazyConstraint(TExpr expr, ConstraintSense sense, double rhs);
    }
    #endregion

    #region Enums

    /// <summary>求解結果狀態。</summary>
    public enum SolveStatus
    {
        /// <summary>尚未求解。</summary>
        NotSolved,

        /// <summary>找到並證明最佳解。</summary>
        Optimal,

        /// <summary>有可行解但未證明最佳（例如逾時停下）。Solve() 仍回傳 true。</summary>
        Feasible,

        /// <summary>無可行解；CPLEX 這一側會自動跑 conflict(IIS) 分析。</summary>
        Infeasible,

        /// <summary>目標式無界，多半是漏了某條限制式。</summary>
        Unbounded,

        /// <summary>時間到且沒有任何可用解。</summary>
        TimeLimit,

        /// <summary>求解過程發生錯誤。</summary>
        Error
    }

    /// <summary>變數型別。</summary>
    public enum VarType
    {
        /// <summary>連續變數。</summary>
        Continuous,

        /// <summary>整數變數。</summary>
        Integer,

        /// <summary>二元 0/1 變數。</summary>
        Binary
    }

    /// <summary>限制式比較方向。</summary>
    public enum ConstraintSense
    {
        /// <summary>小於等於 ≤。</summary>
        LessEqual,

        /// <summary>等於 =。</summary>
        Equal,

        /// <summary>大於等於 ≥。</summary>
        GreaterEqual
    }

    /// <summary>目標式最佳化方向。</summary>
    public enum ObjectiveSense
    {
        /// <summary>最小化。軟性限制式的 penalty 以正號併入目標式。</summary>
        Minimize,

        /// <summary>最大化。軟性限制式的 penalty 以負號併入目標式。</summary>
        Maximize
    }

    /// <summary>問題類型，由已組裝的 solver 模型推導（見 <see cref="ISolverEngine.ModelType"/>）。</summary>
    public enum ModelType
    {
        /// <summary>線性規劃：全部變數皆為連續變數。</summary>
        LP,

        /// <summary>混整數線性規劃：同時有連續變數與 Integer / Binary 變數。</summary>
        MILP,

        /// <summary>整數規劃：沒有連續變數，且至少一個 Integer 變數（可混 Binary）。</summary>
        IP,

        /// <summary>二元規劃：全部變數皆為 Binary。</summary>
        BP
    }

    #endregion

    /// <summary>框架的變數界限常數。</summary>
    public static class OptBounds
    {
        /// <summary>「無上限」：採 CPLEX 的 infinity（1E20），solver 會把 ≥ 此值的界限視為無界。</summary>
        public const double Infinity = 1E20;
    }

    /// <summary>
    /// 各求解器引擎共用的基底類別，負責整理變數、限制式與目標式。
    /// TModel/TVar/TExpr/TConstr 分別是求解器的模型、變數、運算式與限制式型別；子類別實作下方的 abstract 方法來呼叫求解器。
    /// 可批次建立變數並存入 Variables；用 AddLHS/AddRHS 暫存兩側的項，再由 Create* 移項並建立限制式。
    /// 也支援軟性限制式，將違反量的罰分（penalty）加入目標式。
    /// </summary>
    public abstract class EngineBase<TModel, TVar, TExpr, TConstr> : ISolverEngine, ITrajectorySource
    {
        /// <summary>各 solver 的模型物件（CPLEX->Cplex …）；LoadConfig() 建立、Dispose() 釋放。</summary>
        protected TModel Model;

        /// <summary>
        /// 變數池：key = 變數名，value = solver 原生變數。框架唯一的變數索引，含軟性限制式自動加的彈性變數。
        /// 依型別查詢（GetSetVarNames / GetSetVarValues / GetSolution(type)）直接以型別名篩選這個池。
        /// </summary>
        protected readonly Dictionary<string, TVar> Variables = new Dictionary<string, TVar>();

        /// <summary>Variables 字典中的變數總數，包含軟性限制式自動加入的彈性變數。</summary>
        public int VariableCount => Variables.Count;

        /// <summary>
        /// 目前模型的問題類型：無 Integer / Binary → LP；連續與 Integer / Binary 並存 → MILP；
        /// 全為 Binary → BP；無連續且含 Integer → IP。
        /// 判定資料直接取自求解器模型（見 <see cref="ReadModelComposition"/>），不使用框架建立時累計的數量，
        /// 因此自行建立或用 ReadModel 匯入的模型都適用；每次讀取會重新判定，求解前即可讀取。
        /// 軟性限制式的彈性變數是連續變數，因此 IP / BP 模型加了軟性限制式會判定為 MILP。
        /// </summary>
        public ModelType ModelType => ResolveModelType(ReadModelComposition());

        /// <summary>建構時傳入的求解器組態；由各 engine 在 LoadConfig() 內逐項套用到 solver。</summary>
        public ISolverConfig Config { get; protected set; }

        /// <summary>求解狀態；各 engine 完成 SolveCore() 後會寫入結果，未求解前為 NotSolved。</summary>
        public SolveStatus Status { get; protected set; } = SolveStatus.NotSolved;

        /// <summary>求得的最佳目標值；SolveCore() 完成後會寫入結果，未求解前為 0。</summary>
        public double BestObjValue { get; protected set; }

        /// <summary>求解結束時的 MIP gap（相對誤差）；SolveCore() 完成後會寫入結果，LP 問題為 0。</summary>
        public double MIPGap { get; protected set; }

        /// <summary>最近一次 Solve() 的狀態、耗時與結果，由各求解器引擎填入。</summary>
        public SolveMetrics LastMetrics { get; protected set; }

        /// <summary>已建立的限制式數量；預設為 0，由需要提供此數量的引擎覆寫。</summary>
        public virtual int ConstraintCount => 0;

        // 建立統計的計數單位：Expected = 應該建幾個，Actual = 實際成功建了幾個（兩者不等即代表有被略過或失敗）
        private sealed class BuildCount
        {
            /// <summary>預期建立數：各維度所有組合的變數數量，或嘗試建立的限制式條數。</summary>
            public int Expected;

            /// <summary>實際建立數；重複名稱被略過或建立失敗都不計入。</summary>
            public int Actual;
        }

        private readonly Dictionary<string, BuildCount> _variableBuildCounts = new Dictionary<string, BuildCount>();
        private readonly Dictionary<string, BuildCount> _constraintBuildCounts = new Dictionary<string, BuildCount>();
        private bool _buildSummaryDirty = true;

        // 記錄限制式或目標式用過的變數。CPLEX 不收沒被引用的變數，也不會說是哪幾個；只有這份紀錄列得出名單。
        private readonly HashSet<TVar> _referencedVariables = new HashSet<TVar>();

        /// <summary>
        /// 各變數型別的預期與實際建立數，與摘要 log 相同。
        /// 每次讀取都回傳當下的複本，修改複本不影響引擎。
        /// </summary>
        public IReadOnlyDictionary<string, (int Expected, int Actual)> VariableBuildCounts
            => _variableBuildCounts.ToDictionary(kv => kv.Key, kv => (kv.Value.Expected, kv.Value.Actual));

        /// <summary>各限制式群組的「預期 / 實際」建立數；和 <see cref="VariableBuildCounts"/> 一樣，每次讀取都回傳當下的複本。</summary>
        public IReadOnlyDictionary<string, (int Expected, int Actual)> ConstraintBuildCounts
            => _constraintBuildCounts.ToDictionary(kv => kv.Key, kv => (kv.Value.Expected, kv.Value.Actual));

        /// <summary>
        /// 讀取求解器提供的整數統計值，例如節點數或迭代數。因為各求解器的方法名稱不同，
        /// 會依序尋找傳入的無參數方法；找到就呼叫。都不存在或呼叫失敗時回傳 null。
        /// </summary>
        protected static long? TryInvokeLong(object target, params string[] methodNames)
        {
            if (target == null) return null;
            foreach (var name in methodNames)
            {
                var mi = target.GetType().GetMethod(name, Type.EmptyTypes);
                if (mi == null) continue;
                try { return Convert.ToInt64(mi.Invoke(target, null)); }
                catch (Exception ex)
                {
                    Logging.Warn($"[TELEMETRY_READ_FAILED] 無法讀取求解遙測 | method={name} target={target.GetType().FullName} reason={ex.GetBaseException().Message} result=null");
                    return null;
                }
            }
            return null;
        }

        // ITrajectorySource 預設不記錄求解過程；支援此功能的引擎（如 CPLEX）會覆寫。
        /// <summary>是否能記錄求解過程中的目標值與最佳界。預設 false，CPLEX 引擎覆寫為 true。</summary>
        public virtual bool SupportsTrajectory => false;

        /// <summary>開啟求解過程記錄，必須在 Solve() 之前呼叫。不支援的引擎不做任何事，也不拋例外。</summary>
        public virtual void EnableTrajectory() { /* 預設不做任何事；支援此功能的引擎會覆寫 */ }

        /// <summary>最近一次求解的取樣紀錄，包含時間、當時最佳可行解的目標值與最佳界；未開啟或不支援時回空清單。</summary>
        public virtual IReadOnlyList<ConvergencePoint> Trajectory =>
            LastMetrics != null ? (IReadOnlyList<ConvergencePoint>)LastMetrics.Convergence
                                : new List<ConvergencePoint>();

        private readonly List<(double coef, TVar var)> _lhsTerms = new List<(double, TVar)>();
        private readonly List<(double coef, TVar var)> _rhsTerms = new List<(double, TVar)>();
        private double _lhsConst = 0;
        private double _rhsConst = 0;

        // 以 constraint name 為 key，而非 variable set：名稱含迴圈索引（如 "Cap@TruckA"），不同條件不會誤判重複
        private readonly HashSet<string> _verifyConstraints = new HashSet<string>();

        // 目標式追蹤：供軟性限制式把 penalty 併入目標式。
        // 各求解器新增或取代目標式的方式不同，由 AddObjectiveTerm 統一重新設定。
        private readonly List<(double coef, TVar var)> _objectiveTerms = new List<(double, TVar)>();
        private readonly List<(double coef, TVar var)> _softPenaltyTerms = new List<(double, TVar)>();
        private double _objectiveConstant = 0;
        private ObjectiveSense _objectiveSense = ObjectiveSense.Minimize;
        private int _softCount = 0;

        // 建模進度與暫存內容（唯讀）。

        /// <summary>目前暫存的式子：左側與右側各有幾個變數項、常數合計多少。<see cref="CreateEqual(string)"/> 等方法建立限制式後會清空。</summary>
        public (int LhsTerms, double LhsConst, int RhsTerms, double RhsConst) PoolState
            => (_lhsTerms.Count, _lhsConst, _rhsTerms.Count, _rhsConst);

        /// <summary>目標式方向（Minimize / Maximize）。</summary>
        public ObjectiveSense ObjectiveSense => _objectiveSense;

        /// <summary>目標式的變數項數，不包含軟性限制式的罰分項。</summary>
        public int ObjectiveTermCount => _objectiveTerms.Count;

        /// <summary>目標式常數項，取自建目標式時 pool 的 LHS 常數（<see cref="AddLHS(double)"/>）。</summary>
        public double ObjectiveConstant => _objectiveConstant;

        /// <summary>軟性限制式自動命名目前使用的序號；指定名稱的限制式不計入，建立失敗也不會退回序號。</summary>
        public int SoftConstraintCount => _softCount;

        /// <summary>已加入目標式的軟性限制式罰分項數。</summary>
        public int SoftPenaltyTermCount => _softPenaltyTerms.Count;

        /// <summary>
        /// 建立引擎時只保存設定；呼叫 <see cref="Build"/> 才會建立求解器模型。
        /// </summary>
        /// <param name="config">求解器組態；傳 null 時 PreSolveGuard 的規模檢查會被跳過。</param>
        protected EngineBase(ISolverConfig config)
        {
            Config = config;
        }

        #region Solver Contract
        // ════════════════════════════════════════════════════════════════
        // 接入新求解器時，子類別必須實作下列 abstract 方法：
        // LoadConfig：建立 Model 物件並套用 Config 參數
        // AddVariable：向求解器新增單一變數，並存入 Variables 字典
        // LinearExpr：把 (係數, 變數) 清單轉成求解器的線性運算式
        // AddConstraint：新增一般限制式（<=、=、>=）
        // AddRangeConstraint：新增範圍限制式（lb <= expr <= ub）
        // SetObjective：設定目標式（含常數項）與方向（Minimize / Maximize）
        // SetVariableBounds：修改已建立變數的上下界
        // AddMIPStartCore：把已找到的 (變數, 值) 交給求解器作為 MIP start
        // ReadModelComposition：讀取求解器中的變數型別數量，供 ModelType 判定問題類型
        // BuildCore：由 Build() 呼叫，負責透過 LoadConfig(Config) 初始化模型
        // SolveCore：由 Solve() 完成規模檢查後呼叫；找到 Optimal 或 Feasible 解時回傳 true
        // GetObjectiveValue / GetVariableValue / Dispose：讀取解值與釋放資源
        //
        // 下列 virtual 方法已有預設實作，子類別可視需要覆寫：
        // AddVariables：批次建立變數；可改用求解器的批次 API 加速
        // BuildCVs / BuildIVs / BuildBVs：批次建立變數並存入 Variables
        // CreateLessEqualSoft / CreateGreaterEqualSoft / CreateEqualSoft：建立軟性限制式並加入違反罰分
        // ════════════════════════════════════════════════════════════════

        /// <summary>建立 solver 原生模型物件並把 config 的每一項參數套用上去。由 BuildCore() 呼叫。</summary>
        public abstract void LoadConfig(ISolverConfig config);

        /// <summary>向 solver 新增單一變數，並以 name 為 key 存入 Variables 字典。</summary>
        /// <param name="name">變數全名（Build*Vs 產生的格式為 TypeName@s1@s2@…）。</param>
        /// <param name="lb">下界。</param>
        /// <param name="ub">上界。</param>
        /// <param name="type">Continuous / Integer / Binary。</param>
        /// <returns>solver 的原生變數物件。</returns>
        protected abstract TVar AddVariable(string name, double lb, double ub, VarType type);

        /// <summary>把 (係數, 變數) 序列組成求解器的線性運算式；實作應只列舉 terms 一次。</summary>
        protected abstract TExpr LinearExpr(IEnumerable<(double coef, TVar var)> terms);

        /// <summary>新增一般限制式 lhs (≤ | = | ≥) rhs，並把 name 設為 solver 端的限制式名稱。</summary>
        protected abstract TConstr AddConstraint(string name, TExpr lhs, ConstraintSense sense, double rhs);

        /// <summary>新增範圍限制式 lb ≤ expr ≤ ub。</summary>
        protected abstract TConstr AddRangeConstraint(string name, TExpr expr, double lb, double ub);

        /// <summary>
        /// 設定目標式 expr + constant。重複呼叫時必須取代舊目標式，
        /// 因為每加入一項軟性限制式罰分，都會重新設定完整目標式；保留舊目標式會造成重複。
        /// </summary>
        /// <param name="expr">目標式的變數項。</param>
        /// <param name="constant">目標式常數項（offset）；必須傳給求解器，否則回傳目標值會與 Model.md 相差此常數。</param>
        /// <param name="sense">最小化或最大化。</param>
        protected abstract void SetObjective(TExpr expr, double constant, ObjectiveSense sense);

        /// <summary>
        /// 把一組 MIP start 交給 solver。entries 已由 <see cref="AddMIPStart"/> 完成名稱解析與過濾，非空。
        /// </summary>
        protected abstract void AddMIPStartCore(IReadOnlyList<(TVar var, double value)> entries, string name);

        /// <summary>直接改已建立變數的界限；傳 null 表示該側不動。</summary>
        protected abstract void SetVariableBounds(TVar variable, double? lb, double? ub);

        /// <summary>
        /// 向 solver 模型讀出模型組成；<see cref="ModelType"/> 與求解前的模型類型 log 由此判定。
        /// 必須讀取求解器模型（CPLEX 的 Ncols / NbinVars / NintVars / IsMIP），不能只計算 Variables 字典，
        /// 才能同時正確處理自行建立與匯入的模型。模型尚未建立時回傳零值。
        /// </summary>
        /// <returns>
        /// 三個型別的變數數，加上模型是否含離散結構。後者涵蓋 Integer / Binary 以外的離散元素
        /// （semi-continuous、SOS），有它才能在沒有任何 Integer / Binary 變數時仍判定為 MILP。
        /// </returns>
        protected abstract (int Continuous, int Integer, int Binary, bool HasDiscreteStructure) ReadModelComposition();

        /// <summary>
        /// 建立模型的入口，由外部呼叫。先清空建立統計，再呼叫 BuildCore()（各 engine 實作）。
        /// </summary>
        public void Build()
        {
            try
            {
                ResetBuildStatistics();
                BuildCore();
            }
            catch (Exception ex)
            {
                LogUnexpectedBoundaryFailure(nameof(Build), ex);
                throw;
            }
        }

        /// <summary>
        /// 開始求解：先記錄建模摘要，再點名沒被引用的變數，接著檢查變數數量門檻，最後呼叫各引擎的 SolveCore()。
        /// 有沒被引用的變數時只記錄 <c>[UNREFERENCED_VARIABLES]</c> 警告，仍會繼續求解。
        /// </summary>
        public bool Solve()
        {
            try
            {
                LogBuildSummary();
                WarnUnreferencedVariables();
                PreSolveGuard();
                return SolveCore();
            }
            catch (Exception ex)
            {
                LogUnexpectedBoundaryFailure(nameof(Solve), ex);
                throw;
            }
        }

        private void LogUnexpectedBoundaryFailure(string operation, Exception exception)
        {
            Logging.ErrorOnce(
                exception,
                "ENGINE_API_FAILED",
                "公開 API 執行失敗",
                operation,
                GetType().FullName,
                exception.GetBaseException().Message);
        }

        /// <summary>各引擎實作模型初始化的方法；使用者應呼叫 <see cref="Build"/>，由它清空統計後再呼叫此方法。</summary>
        protected abstract void BuildCore();

        /// <summary>各 engine 的求解實作；由 <see cref="Solve"/> 呼叫。回傳 true 代表取得 Optimal 或 Feasible 解。</summary>
        protected abstract bool SolveCore();

        // VariableCount > Config.ScaleWarnThreshold → Logging.Warn（只警告不阻擋，大但合法的模型不該被擋）。
        // 量的是整個 Variables 池，含軟性限制式的彈性變數與匯入的變數。Config 為 null 時直接略過檢查。
        private void PreSolveGuard()
        {
            if (Config == null) return;
            if (VariableCount > Config.ScaleWarnThreshold)
                Logging.Warn($"[MODEL_SCALE_WARNING] 變數規模超過警告門檻 | count={VariableCount} threshold={Config.ScaleWarnThreshold} result=continued");
        }

        /// <summary>取得目標式解值。必須在 Solve() 回傳 true 後呼叫，否則求解器可能拋出例外。</summary>
        public abstract double GetObjectiveValue();

        /// <summary>依變數全名取得解值（格式 TypeName@s1@s2@…），必須在求解成功後呼叫。</summary>
        public abstract double GetVariableValue(string name);

        /// <summary>釋放 solver 原生資源（CPLEX 的 native handle）。用 using 包住 engine，或交由 OptProject / OptExperiment 管理。</summary>
        public abstract void Dispose();

        #endregion

        #region VariableManager — 批次建立變數

        /// <summary>
        /// 批次建立變數並存入 Variables 字典。
        /// 預設逐筆呼叫 AddVariable。
        /// 子類別可覆寫成求解器的陣列 API（如 CPLEX NumVarArray），
        /// 減少 C# 與求解器原生程式之間的呼叫次數。
        /// </summary>
        protected virtual void AddVariables(IReadOnlyList<string> names, double lb, double ub, VarType type)
        {
            foreach (var name in names)
            {
                AddVariable(name, lb, ub, type);
            }
        }

        // 批次建立某型別的所有變數：組出全部變數名 → 建到 solver 並登記進 Variables；之後依型別名篩選即可查回
        private void BatchBuild<TVariable>(double lb, double ub, VarType type, object[] sets)
            => BatchBuild(typeof(TVariable).Name, () => VariableBuilder.GetVarNames<TVariable>(sets), lb, ub, type);

        // string 版以 setName 作為變數名稱的開頭，其餘流程與泛型版相同。
        private void BatchBuild(string setName, double lb, double ub, VarType type, object[] sets)
            => BatchBuild(setName, () => VariableBuilder.GetVarNames(setName, sets), lb, ub, type);

        private void BatchBuild(string setName, Func<IEnumerable<string>> nameFactory, double lb, double ub, VarType type)
        {
            int before = Variables.Count;
            List<string> names = null;
            string stage = "key_generation";
            try
            {
                // 1) 由 sets 笛卡兒積組出所有變數名（TypeName@s1@s2@…）
                names = nameFactory().ToList();

                // 2) 實際在 solver 建立這些變數並登記進 Variables（子類別可用原生 batch API 加速）
                stage = "solver_creation";
                AddVariables(names, lb, ub, type);

                int actual = Variables.Count - before;
                RecordVariableBuild(setName, names.Count, actual);
                Logging.Info($"[變數建立完成] type={setName} count={actual}/{names.Count}");
            }
            catch (Exception ex)
            {
                int actual = Math.Max(0, Variables.Count - before);
                string expected = names == null ? "unknown" : names.Count.ToString();
                if (names != null)
                    RecordVariableBuild(setName, names.Count, actual);
                Logging.ErrorOnce(
                    ex,
                    "VARIABLE_BUILD_FAILED",
                    "變數建立失敗",
                    "BatchBuild",
                    stage,
                    ex.GetBaseException().Message,
                    $"type={setName} varType={type} bounds=[{lb},{ub}] count={actual}/{expected}");
                throw;
            }
        }

        private static bool TryResolveVariableType(string className, out VarType type)
        {
            if (!VariablePrefixNaming.TryResolve(className, out var typeName))
            {
                type = default;
                return false;
            }

            if (Enum.TryParse(typeName, ignoreCase: false, out type))
                return true;

            var exception = new InvalidOperationException(
                $"Variable 前綴解析器回傳未知的 VarType 成員名稱：{typeName}。");
            throw Logging.ErrorOnce(
                exception,
                "VARIABLE_TYPE_RESOLUTION_FAILED",
                "變數型別解析失敗",
                nameof(TryResolveVariableType),
                typeName,
                "unknown_var_type");
        }

        private static void ValidateExplicitVariableType<TVariable>(VarType requestedType, string operation)
        {
            string className = typeof(TVariable).Name;

            // 明確 builder 入口：完全沒有正式前綴的類別可由呼叫方法決定型別。
            if (!TryResolveVariableType(className, out var declaredType) || declaredType == requestedType)
                return;

            string message = $"{operation}<{className}> 要建立 {requestedType} 變數，但類別名前綴宣告為 {declaredType}。" +
                $"命名規則：{VariablePrefixNaming.NamingGuide}。";
            throw Logging.ErrorOnce(
                new ArgumentException(message),
                "VARIABLE_TYPE_MISMATCH",
                "變數前綴與建構方法型別不一致",
                operation,
                className,
                "declared_type_mismatch",
                $"type={className} declared={declaredType} requested={requestedType}");
        }


        /// <summary>
        /// 批次建立連續變數，界限 [0, <see cref="OptBounds.Infinity"/>]（1E20 = CPLEX 的無上限）。
        /// </summary>
        /// <typeparam name="TVariable">變數類別；property 宣告順序必須與 sets 順序一致，否則 AddLHS 組出的名稱會查不到變數。</typeparam>
        /// <param name="sets">各維度的集合；框架取各集合的所有組合（笛卡兒積）產生變數名稱。</param>
        public virtual void BuildCVs<TVariable>(params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Continuous, nameof(BuildCVs));
            BatchBuild<TVariable>(0, OptBounds.Infinity, VarType.Continuous, sets);
        }

        /// <summary>批次建立連續變數並指定界限 [lb, ub]。</summary>
        public virtual void BuildCVs<TVariable>(double lb, double ub, params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Continuous, nameof(BuildCVs));
            BatchBuild<TVariable>(lb, ub, VarType.Continuous, sets);
        }

        /// <summary>批次建立整數變數，界限 [0, <see cref="OptBounds.Infinity"/>]。維度順序要求同 <see cref="BuildCVs{TVariable}(object[])"/>。</summary>
        public virtual void BuildIVs<TVariable>(params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Integer, nameof(BuildIVs));
            BatchBuild<TVariable>(0, OptBounds.Infinity, VarType.Integer, sets);
        }

        /// <summary>批次建立整數變數並指定界限 [lb, ub]。</summary>
        public virtual void BuildIVs<TVariable>(double lb, double ub, params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Integer, nameof(BuildIVs));
            BatchBuild<TVariable>(lb, ub, VarType.Integer, sets);
        }

        /// <summary>批次建立 0/1 二元變數。維度順序要求同 <see cref="BuildCVs{TVariable}(object[])"/>。</summary>
        public virtual void BuildBVs<TVariable>(params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Binary, nameof(BuildBVs));
            BatchBuild<TVariable>(0, 1, VarType.Binary, sets);
        }

        /// <summary>
        /// 依類別名前綴決定變數型別：VariableB_（Binary，界限 [0,1]）、
        /// VariableC_（Continuous）/ VariableI_（Integer）。
        /// 需要自訂上下界，或類別名未使用這些前綴時，可用 BuildCVs / BuildIVs / BuildBVs 指定型別。
        /// </summary>
        public virtual void BuildVars<TVariable>(params object[] sets)
        {
            string name = typeof(TVariable).Name;
            if (!TryResolveVariableType(name, out var type))
            {
                string msg = $"BuildVars<{name}> 無法從類別名前綴判定變數型別。" +
                    $"命名天條：{VariablePrefixNaming.NamingGuide}，例：VariableC_Start；" +
                    "不依天條命名請改用 BuildCVs / BuildIVs / BuildBVs。";
                throw Logging.ErrorOnce(
                    new ArgumentException(msg),
                    "VARIABLE_TYPE_UNKNOWN",
                    "無法判定變數型別",
                    nameof(BuildVars),
                    name,
                    "invalid_prefix",
                    $"type={name}");
            }

            // BuildVars 是統一入口；實際建構委派給型別專用方法，三者再共用 BatchBuild。
            switch (type)
            {
                case VarType.Binary:
                    BuildBVs<TVariable>(sets);
                    break;
                case VarType.Integer:
                    BuildIVs<TVariable>(sets);
                    break;
                default:
                    BuildCVs<TVariable>(sets);
                    break;
            }
        }

        // 以下是上面每個 Build*Vs 的 string 版：功能完全相同，只是用 setName 取代 TVariable。
        // 沒有類別資訊，因此不檢查維度數量，也不檢查類別名前綴與型別是否一致。
        // setName 仍會經 ModelNaming 驗證（不可空白、不可含保留字元、不可數字開頭），確保產出的名稱送得進 solver。

        /// <summary>批次建立連續變數的 string 版，界限 [0, <see cref="OptBounds.Infinity"/>]。</summary>
        /// <param name="setName">變數名稱的開頭，取代泛型版的類別名；依型別查詢時也以它篩選變數池。</param>
        /// <param name="sets">各維度的集合；框架取各集合的所有組合（笛卡兒積）產生變數名稱。</param>
        public virtual void BuildCVs(string setName, params object[] sets)
            => BatchBuild(setName, 0, OptBounds.Infinity, VarType.Continuous, sets);

        /// <summary>批次建立連續變數並指定界限 [lb, ub] 的 string 版。</summary>
        public virtual void BuildCVs(string setName, double lb, double ub, params object[] sets)
            => BatchBuild(setName, lb, ub, VarType.Continuous, sets);

        /// <summary>批次建立整數變數的 string 版，界限 [0, <see cref="OptBounds.Infinity"/>]。</summary>
        public virtual void BuildIVs(string setName, params object[] sets)
            => BatchBuild(setName, 0, OptBounds.Infinity, VarType.Integer, sets);

        /// <summary>批次建立整數變數並指定界限 [lb, ub] 的 string 版。</summary>
        public virtual void BuildIVs(string setName, double lb, double ub, params object[] sets)
            => BatchBuild(setName, lb, ub, VarType.Integer, sets);

        /// <summary>批次建立 0/1 二元變數的 string 版。</summary>
        public virtual void BuildBVs(string setName, params object[] sets)
            => BatchBuild(setName, 0, 1, VarType.Binary, sets);

        /// <summary>
        /// <see cref="BuildVars{TVariable}(object[])"/> 的 string 版。泛型版由類別名前綴推導型別，
        /// string 版沒有類別可推，改由 type 參數明確指定；界限比照對應的 Build*Vs。
        /// </summary>
        public virtual void BuildVars(string setName, VarType type, params object[] sets)
        {
            switch (type)
            {
                case VarType.Binary:
                    BuildBVs(setName, sets);
                    break;
                case VarType.Integer:
                    BuildIVs(setName, sets);
                    break;
                default:
                    BuildCVs(setName, sets);
                    break;
            }
        }

        #endregion

        #region VariableManager — 查詢

        /// <summary>
        /// 依變數實例查出 solver 原生變數：實例的 ToString() 就是變數全名（TypeName@…），直接查 Variables。
        /// </summary>
        /// <param name="searchData">填好各維度值的變數類別實例，例：new VariableB_Assign { EMP = "E1", DATE = "D1" }。</param>
        /// <exception cref="KeyNotFoundException">該型別未建立，或這組索引值不在建立範圍內。</exception>
        protected TVar ReadVar(object searchData)
            => ReadVar(searchData.ToString());

        /// <summary>
        /// 依變數全名查詢 Variables 字典並回傳求解器變數。相較於 <see cref="ReadVar(object)"/>，
        /// 不需要建立變數類別實例。
        /// </summary>
        /// <param name="varName">變數全名，例：VariableB_Assign@E1@D1。import 進來、不符框架命名慣例的名稱同樣可查。</param>
        /// <exception cref="KeyNotFoundException">這個名稱的變數不存在。</exception>
        protected TVar ReadVar(string varName)
        {
            if (varName != null && Variables.TryGetValue(varName, out var v))
                return v;

            throw Logging.ErrorOnce(
                new KeyNotFoundException($"找不到變數 '{varName}'"),
                "VARIABLE_NOT_FOUND",
                "變數不存在",
                nameof(ReadVar),
                varName,
                "variable_not_built");
        }

        // 依型別查詢一律篩 Variables：名稱等於型別名（0 維變數）或以「型別名@」開頭。
        private IEnumerable<string> FilterVarNames(string typeName)
        {
            if (typeName == null) return Enumerable.Empty<string>();
            string prefix = typeName + ModelNaming.Separator;
            return Variables.Keys.Where(name => name == typeName || name.StartsWith(prefix, StringComparison.Ordinal));
        }

        /// <summary>變數池內的全部變數名，含軟性限制式的彈性變數與匯入的變數。</summary>
        public string[] GetAllVarNames()
            => Variables.Keys.ToArray();

        /// <summary>某變數型別的全部變數名；沒有符合的變數回空陣列（不丟例外）。</summary>
        public string[] GetSetVarNames<TVariable>()
            => GetSetVarNames(typeof(TVariable).Name);

        /// <summary><see cref="GetSetVarNames{TVariable}"/> 的 string 版：以 setName 篩選變數池；沒有符合的回空陣列（不丟例外）。</summary>
        public string[] GetSetVarNames(string setName)
            => FilterVarNames(setName).ToArray();

        /// <summary>取某變數型別的全部解值，key = 完整變數名（TypeName@…）。求解後呼叫；沒有符合的變數回空字典。</summary>
        public Dictionary<string, double> GetSetVarValues<TVariable>()
            => GetSetVarValues(typeof(TVariable).Name);

        /// <summary><see cref="GetSetVarValues{TVariable}"/> 的 string 版：以 setName 篩選變數池；沒有符合的回空字典。</summary>
        public Dictionary<string, double> GetSetVarValues(string setName)
        {
            try
            {
                return FilterVarNames(setName).ToDictionary(name => name, GetVariableValue);
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "SOLUTION_READ_FAILED", "公開 API 執行失敗", nameof(GetSetVarValues), setName,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        /// <summary>取解值字典；varTypeName=null 回全部變數，否則篩選該型別（名稱為 TypeName 或以 "TypeName@" 開頭）。</summary>
        public virtual IReadOnlyDictionary<string, double> GetSolution(string varTypeName = null)
        {
            try
            {
                var names = varTypeName == null ? Variables.Keys : FilterVarNames(varTypeName);
                return names.ToDictionary(name => name, GetVariableValue);
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "SOLUTION_READ_FAILED", "公開 API 執行失敗", nameof(GetSolution), varTypeName,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        #endregion

        #region MIP Start

        /// <summary>
        /// 以「變數全名 → 值」提供一組 MIP start；名稱格式同 <see cref="GetSolution"/>，
        /// 所以上一段的解可直接接過來：<c>next.AddMIPStart(prev.GetSolution())</c>。
        /// 不必給全部變數，給部分值由 solver 自行補齊（依 solver 的 effort 設定）。
        /// </summary>
        /// <remarks>
        /// 必須在模型建完、Solve() 之前呼叫。LP 模型不使用 MIP start，會記錄警告後略過；
        /// 模型內不存在的名稱 → warn 後略過該項（常見於前後段模型不同），其餘照常套用。
        /// </remarks>
        /// <param name="values">變數全名 → 起始值。</param>
        /// <param name="name">MIP start 名稱；null 由 solver 自動命名。</param>
        /// <returns>實際套用的變數數；略過時為 0。</returns>
        /// <exception cref="ArgumentNullException">values 為 null。</exception>
        public int AddMIPStart(IReadOnlyDictionary<string, double> values, string name = null)
        {
            string label = name ?? "<auto>";
            if (values == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(values)),
                    "MIP_START_INVALID", "MIP start 不合法", nameof(AddMIPStart), label, "values_is_null");

            try
            {
                if (ModelType == ModelType.LP)
                {
                    Logging.Warn($"[MIP_START_SKIPPED] 略過 MIP start | name={label} values={values.Count} reason=model_is_lp result=skipped");
                    return 0;
                }

                var entries = new List<(TVar var, double value)>(values.Count);
                var unknown = new List<string>();
                foreach (var kv in values)
                {
                    if (kv.Key != null && Variables.TryGetValue(kv.Key, out var v))
                        entries.Add((v, kv.Value));
                    else
                        unknown.Add(kv.Key ?? "<null>");
                }

                if (unknown.Count > 0)
                    Logging.Warn($"[MIP_START_UNKNOWN_VARIABLE] MIP start 含模型內不存在的變數 | name={label} unknown={unknown.Count} sample={string.Join(",", unknown.Take(5))} reason=variable_not_in_model result=entries_skipped");

                if (entries.Count == 0)
                {
                    Logging.Warn($"[MIP_START_SKIPPED] 略過 MIP start | name={label} values={values.Count} reason=no_matching_variable result=skipped");
                    return 0;
                }

                AddMIPStartCore(entries, name);
                Logging.Info($"[MIP start 建立完成] name={label} applied={entries.Count}/{values.Count} modelVars={VariableCount}");
                return entries.Count;
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "MIP_START_FAILED", "MIP start 套用失敗", nameof(AddMIPStart), label,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        #endregion

        #region VariableManager — 設定變數界限

        /// <summary>把單一變數的下界改成 lb（上界不動）。用於固定變數或加開發期的暫時界限。</summary>
        protected void SetVarLB(object searchData, double lb)
            => SetVariableBounds(ReadVar(searchData), lb, null);

        /// <summary>把單一變數的上界改成 ub（下界不動）。</summary>
        protected void SetVarUB(object searchData, double ub)
            => SetVariableBounds(ReadVar(searchData), null, ub);

        /// <summary>把單一變數的界限改成 [lb, ub]；lb == ub 等同把變數固定成該值。</summary>
        protected void SetVarRange(object searchData, double lb, double ub)
            => SetVariableBounds(ReadVar(searchData), lb, ub);

        /// <summary><see cref="SetVarLB(object, double)"/> 的 string 版，以變數全名指定。</summary>
        protected void SetVarLB(string varName, double lb)
            => SetVariableBounds(ReadVar(varName), lb, null);

        /// <summary><see cref="SetVarUB(object, double)"/> 的 string 版，以變數全名指定。</summary>
        protected void SetVarUB(string varName, double ub)
            => SetVariableBounds(ReadVar(varName), null, ub);

        /// <summary><see cref="SetVarRange(object, double, double)"/> 的 string 版，以變數全名指定。</summary>
        protected void SetVarRange(string varName, double lb, double ub)
            => SetVariableBounds(ReadVar(varName), lb, ub);

        #endregion

        #region 重設與建立統計

        /// <summary>清空限制式的「已用名稱」集合與建立統計；清掉後同名限制式可再次送出（不再被當重複略過）。</summary>
        protected void ResetVerifyConstraints()
        {
            _verifyConstraints.Clear();
            _constraintBuildCounts.Clear();
            _buildSummaryDirty = true;
        }

        private void ResetBuildStatistics()
        {
            _variableBuildCounts.Clear();
            _constraintBuildCounts.Clear();
            _referencedVariables.Clear();
            _buildSummaryDirty = true;
        }

        private void RecordVariableBuild(string type, int expected, int actual)
        {
            if (!_variableBuildCounts.TryGetValue(type, out var count))
            {
                count = new BuildCount();
                _variableBuildCounts[type] = count;
            }
            count.Expected += expected;
            count.Actual += actual;
            _buildSummaryDirty = true;
        }

        #region 建模記帳 — 給 engine 子類別的入口

        /// <summary>
        /// 匯入模型並建立查詢索引後呼叫：清掉舊模型的建立統計，並以檔案裡的目標式方向同步 <see cref="ObjectiveSense"/>，
        /// 否則匯入 maximize 模型會被當成 minimize，軟性 penalty 也會反號。
        /// </summary>
        /// <param name="objective">檔案裡的目標式方向；null = 沒有目標式。</param>
        protected void RecordImportedModel(ObjectiveSense? objective)
        {
            // 匯入會取代求解器模型，因此先清除舊模型的變數建立統計與引用紀錄。
            _variableBuildCounts.Clear();
            _referencedVariables.Clear();
            if (objective.HasValue) _objectiveSense = objective.Value;
            // 匯入的變數都來自模型本身的矩陣，本來就在 solver 模型內，視同已引用
            foreach (var v in Variables.Values) _referencedVariables.Add(v);
            _buildSummaryDirty = true;
        }

        /// <summary>子類別直接呼叫求解器 API 建立變數後，用此方法補上建立統計（如 OptEngine.CreateVar）。</summary>
        protected void RecordDirectVariable(string name)
            => RecordVariableBuild(VariableGroup(name), 1, 1);

        /// <summary>子類別直接呼叫求解器 API 建立限制式後，用此方法補上建立統計（如 OptEngine.AddLE）。</summary>
        protected void RecordDirectConstraint(string name)
            => RecordConstraintBuild(name, true);

        /// <summary>子類別直接設定求解器目標式後，用此方法記錄方向並更新 <see cref="ObjectiveSense"/>（如 OptEngine.Maximize）。</summary>
        protected void RecordDirectObjective(ObjectiveSense sense)
        {
            _objectiveSense = sense;
            _buildSummaryDirty = true;
        }

        /// <summary>
        /// solver 端的限制式與目標式全部移除、變數保留時呼叫（例：OptEngine.ResetConstraint）。
        /// 清除上一輪的變數引用紀錄，才能找出重建後未使用的變數。
        /// </summary>
        protected void ResetReferencedVariables()
        {
            _referencedVariables.Clear();
            _buildSummaryDirty = true;
        }

        private static string VariableGroup(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "<unnamed>";
            int separator = name.IndexOf('@');
            return separator > 0 ? name.Substring(0, separator) : name;
        }

        private void MarkReferenced(IEnumerable<(double coef, TVar var)> terms)
        {
            foreach (var term in terms) _referencedVariables.Add(term.var);
        }

        #endregion

        private void RecordConstraintBuild(string name, bool created)
        {
            string group = ConstraintGroup(name);
            if (!_constraintBuildCounts.TryGetValue(group, out var count))
            {
                count = new BuildCount();
                _constraintBuildCounts[group] = count;
            }
            count.Expected++;
            if (created) count.Actual++;
            _buildSummaryDirty = true;
        }

        private static string ConstraintGroup(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "<unnamed>";
            int separator = name.IndexOf('@');
            string group = separator > 0 ? name.Substring(0, separator) : name;
            int suffix = group.LastIndexOf('_');
            if (group.StartsWith("Soft_", StringComparison.Ordinal) && suffix > 0 &&
                int.TryParse(group.Substring(suffix + 1), out _))
                return group.Substring(0, suffix);
            return group;
        }

        /// <summary>
        /// 統計建模過程中建立的變數與限制式數量，印出 Summary log。
        /// </summary>
        private void LogBuildSummary()
        {
            if (!_buildSummaryDirty) return;

            // 同時列出建立時累計的數量與目前模型數量，方便查看差異：
            // 已建立=實際/預期：由 RecordVariableBuild / RecordConstraintBuild 累計。
            // 模型內合計：Variables 字典數量；solver 實際持有：ConstraintCount 提供的限制式數量。
            // 數量不同時，可能有建立操作未更新統計，或模型在中途被重設。
            // （例：再次呼叫 LoadConfig() 會清空 solver 的限制式，但不會清 Core 的計數器）。
            int expectedVariables = _variableBuildCounts.Values.Sum(x => x.Expected);
            int actualVariables = _variableBuildCounts.Values.Sum(x => x.Actual);
            Logging.Info($"[變數建立摘要] 已建立={actualVariables}/{expectedVariables}（實際/預期） 變數類別={_variableBuildCounts.Count} 種 模型內合計={VariableCount}");

            foreach (var entry in _constraintBuildCounts.OrderBy(x => x.Key, StringComparer.Ordinal))
                Logging.Info($"[限制式建立] 群組={entry.Key} 已建立={entry.Value.Actual}/{entry.Value.Expected}（實際/預期）");
            int expectedConstraints = _constraintBuildCounts.Values.Sum(x => x.Expected);
            int actualConstraints = _constraintBuildCounts.Values.Sum(x => x.Actual);
            Logging.Info($"[限制式建立摘要] 已建立={actualConstraints}/{expectedConstraints}（實際/預期） 群組={_constraintBuildCounts.Count} 個 solver 實際持有={ConstraintCount}");

            var composition = ReadModelComposition();
            Logging.Info($"[模型類型] type={ResolveModelType(composition)} continuous={composition.Continuous} integer={composition.Integer} binary={composition.Binary}");

            _buildSummaryDirty = false;
        }

        private static ModelType ResolveModelType((int Continuous, int Integer, int Binary, bool HasDiscreteStructure) composition)
        {
            if (!composition.HasDiscreteStructure) return ModelType.LP;
            // 有離散結構但沒有 Integer / Binary 變數 → 離散性來自 semi-continuous / SOS，歸 MILP
            if (composition.Continuous > 0 || composition.Integer + composition.Binary == 0) return ModelType.MILP;
            return composition.Integer == 0 ? ModelType.BP : ModelType.IP;
        }
        #endregion

        #region 未引用變數

        // CPLEX 不收沒被限制式或目標式引用的變數：Ncols 看得出少了幾個，看不出是哪幾個，名單只有框架列得出。
        // 只在 CPLEX 收的比框架宣告的少時才列：OptEngine.AddLE 等直接建模入口不經 pool，用到的變數不在引用紀錄裡。
        private void WarnUnreferencedVariables()
        {
            var composition = ReadModelComposition();
            if (VariableCount <= composition.Continuous + composition.Integer + composition.Binary) return;

            var unreferenced = Variables.Where(kv => !_referencedVariables.Contains(kv.Value)).Select(kv => kv.Key).ToList();
            if (unreferenced.Count == 0) return;
            Logging.Warn($"[UNREFERENCED_VARIABLES] {unreferenced.Count} 個變數已宣告但沒被任何限制式或目標式引用，CPLEX 不會收進模型 | groups={GroupSummary(unreferenced)} sample={string.Join(",", unreferenced.Take(5))} result=continued");
        }

        // 把未使用的變數依名稱中第一個 @ 之前的部分分組（如 VariableB_Pick=2），最多列出 5 組。
        private static string GroupSummary(IEnumerable<string> names)
        {
            var groups = names.GroupBy(VariableGroup).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
            string shown = string.Join(", ", groups.Take(5).Select(g => $"{g.Key}={g.Count()}"));
            return groups.Count > 5 ? $"{shown}, …共 {groups.Count} 組" : shown;
        }

        #endregion

        #region Pool — 狀態管理

        /// <summary>pool 目前是否有變數項（只看變數項，純常數不算）。用於送出前確認自己有沒有漏加項。</summary>
        public bool HasPool => _lhsTerms.Count > 0 || _rhsTerms.Count > 0;

        /// <summary>
        /// 丟棄 pool 內累積的所有項與常數。
        /// Create* 正常建立後會清空；要放棄尚未建完的式子，或丟棄提早返回、例外後留下的內容時，可自行呼叫。
        /// </summary>
        public void ClearPool()
        {
            _lhsTerms.Clear();
            _rhsTerms.Clear();
            _lhsConst = 0;
            _rhsConst = 0;
        }

        private bool CheckHasPool(string constraintName)
        {
            if (_lhsTerms.Count == 0 && _rhsTerms.Count == 0)
            {
                Logging.Warn($"[CONSTRAINT_EMPTY] 未建立限制式 | name={constraintName ?? "<unnamed>"} reason=pool_empty result=skipped");
                return false;
            }
            return true;
        }

        // CreateRange 與建立目標式只採用左側；右側有暫存項目時記錄警告，提醒呼叫端這些項目不會被使用。
        private void WarnIfRhsPoolIgnored(string operation, string name, string reason)
        {
            if (_rhsTerms.Count == 0 && _rhsConst == 0) return;
            Logging.Warn($"[POOL_RHS_IGNORED] 右側 pool 不被採用 | operation={operation} name={name} rhsTerms={_rhsTerms.Count} rhsConst={_rhsConst} reason={reason} result=rhs_discarded");
        }

        private void LogDuplicateConstraint(string name)
            => Logging.Warn($"[CONSTRAINT_DUPLICATE] 略過重複限制式 | name={name} reason=duplicate_name result=kept_existing");

        // 列舉時才把右側係數取負並接到左側後面，供 LinearExpr 讀取，不另建 List。
        private IEnumerable<(double coef, TVar var)> CombinedLhsMinusRhs()
            => _lhsTerms.Concat(_rhsTerms.Select(t => (-t.coef, t.var)));

        #endregion

        #region Pool — AddLHS / AddRHS

        /// <summary>
        /// 往限制式左側累加一項 coeff·變數。連續呼叫即為求和；送出前一直留在 pool。
        /// </summary>
        /// <param name="coeff">係數。若需查詢 Parameter，先存成區域變數再傳入，方便檢查取值是否正確。</param>
        /// <param name="varSpec">填好各維度值的變數類別實例；框架以它的 ToString() 當變數名查表。</param>
        /// <returns>true = 已加入；false = varSpec 為 null（該項被略過，只寫 warn log 不中斷建模）。</returns>
        /// <exception cref="KeyNotFoundException">變數名查不到——多半是 property 宣告順序與 Build*Vs 的 set 順序不一致。</exception>
        public bool AddLHS(double coeff, object varSpec)
        {
            if (varSpec == null)
            {
                Logging.Warn("[VARIABLE_NULL] 略過空變數項 | operation=AddLHS reason=null_variable result=term_skipped");
                return false;
            }
            string key = varSpec.ToString();
            if (!Variables.TryGetValue(key, out var v))
            {
                throw Logging.ErrorOnce(
                    new KeyNotFoundException($"AddLHS: 找不到變數 '{key}'（type: {varSpec.GetType().Name}）。請確認 property 宣告順序與 Build*Vs 傳入 set 順序一致。"),
                    "VARIABLE_NOT_FOUND",
                    "變數不存在",
                    nameof(AddLHS),
                    key,
                    "variable_not_built",
                    $"type={varSpec.GetType().Name}");
            }
            _lhsTerms.Add((coeff, v));
            return true;
        }

        /// <summary>往左側累加一個常數項。送出時框架會把它移到右側抵銷（rhs − lhsConst），數學上等價。</summary>
        /// <returns>恆為 true（僅為與另一個 overload 的簽名一致）。</returns>
        public bool AddLHS(double constant)
        {
            _lhsConst += constant;
            return true;
        }

        /// <summary>
        /// 往限制式右側累加一項 coeff·變數。
        /// Model.md 右側的變數項可直接加在這裡；框架建立限制式時會移項，讓建模程式保留原式的左右位置。
        /// </summary>
        /// <returns>true = 已加入；false = varSpec 為 null（略過該項）。</returns>
        /// <exception cref="KeyNotFoundException">變數名查不到（同 <see cref="AddLHS(double, object)"/>）。</exception>
        public bool AddRHS(double coeff, object varSpec)
        {
            if (varSpec == null)
            {
                Logging.Warn("[VARIABLE_NULL] 略過空變數項 | operation=AddRHS reason=null_variable result=term_skipped");
                return false;
            }
            string key = varSpec.ToString();
            if (!Variables.TryGetValue(key, out var v))
            {
                throw Logging.ErrorOnce(
                    new KeyNotFoundException($"AddRHS: 找不到變數 '{key}'（type: {varSpec.GetType().Name}）。請確認 property 宣告順序與 Build*Vs 傳入 set 順序一致。"),
                    "VARIABLE_NOT_FOUND",
                    "變數不存在",
                    nameof(AddRHS),
                    key,
                    "variable_not_built",
                    $"type={varSpec.GetType().Name}");
            }
            _rhsTerms.Add((coeff, v));
            return true;
        }

        /// <summary>往右側累加一個常數項（等號右邊的數值上限 / 需求量等）。</summary>
        /// <returns>恆為 true。</returns>
        public bool AddRHS(double constant)
        {
            _rhsConst += constant;
            return true;
        }
        #endregion

        #region Pool — 建立限制式

        private static string ComposeConstraintName(ConstraintBase owner, object[] dims)
        {
            if (owner == null)
            {
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(owner), "建立限制式名稱時 owner 不可為 null。"),
                    "CONSTRAINT_OWNER_INVALID",
                    "限制式名稱建立失敗",
                    nameof(ComposeConstraintName),
                    null,
                    "owner_is_null");
            }

            return ModelNaming.Compose(owner.GetType().Name, dims);
        }

        private static string ValidateConstraintName(string operation, string name)
            => ModelNaming.ValidateComposedName(operation, name);

        /// <summary>
        /// 送出 pool 為「LHS ≥ RHS」限制式，對應 Model.md 的 <c>≥</c>。
        /// 實際送進 solver 的形式是 (LHS項 − RHS項) ≥ (RHS常數 − LHS常數)，由框架自動整理，呼叫端不需要也不應該自己移項。
        /// </summary>
        /// <param name="name">限制式名稱；同名只建立第一條，之後略過並記錄警告。迴圈建立時須在名稱包含維度值，例如 "Cap@TruckA"。</param>
        /// <returns>true = pool 有內容（含被判定重複而略過的情況）；false = pool 是空的，什麼都沒建。</returns>
        /// <remarks>建立成功或因同名略過後會清空 pool；若沒有變數項而提早返回，或建立時拋出例外，pool 會保留。</remarks>
        public bool CreateGreaterEqual(string name)
            => CreateLinearConstraint(ValidateConstraintName(nameof(CreateGreaterEqual), name), ConstraintSense.GreaterEqual);

        /// <summary>以 owner 類別名與維度值自動組名，送出「LHS ≥ RHS」。</summary>
        public bool CreateGreaterEqual(ConstraintBase owner, params object[] dims)
            => CreateLinearConstraint(ComposeConstraintName(owner, dims), ConstraintSense.GreaterEqual);

        /// <summary>
        /// 送出「LHS ≥ rhs」，右側直接給常數。
        /// rhs 會取代先前 AddRHS 累加的右側常數；右側變數項仍會保留。
        /// </summary>
        /// <returns>false = 左側沒有任何變數項，限制式未建立。</returns>
        public bool CreateGreaterEqual(double rhs, string name)
        {
            name = ValidateConstraintName(nameof(CreateGreaterEqual), name);
            if (_lhsTerms.Count == 0)
            {
                Logging.Warn($"[CONSTRAINT_EMPTY] 未建立限制式 | name={name} reason=lhs_empty result=skipped");
                RecordConstraintBuild(name, false);
                return false;
            }
            _rhsConst = rhs;
            return CreateLinearConstraint(name, ConstraintSense.GreaterEqual);
        }

        /// <summary>
        /// 送出 pool 為「LHS ≤ RHS」限制式，對應 Model.md 的 <c>≤</c>。
        /// 送出形式與清空 pool 的行為同 <see cref="CreateGreaterEqual(string)"/>，只有比較方向不同。
        /// </summary>
        /// <param name="name">限制式名稱；同名第二次起會被略過（warn log）。</param>
        /// <returns>true = pool 有內容；false = pool 是空的。</returns>
        public bool CreateLessEqual(string name)
            => CreateLinearConstraint(ValidateConstraintName(nameof(CreateLessEqual), name), ConstraintSense.LessEqual);

        /// <summary>以 owner 類別名與維度值自動組名，送出「LHS ≤ RHS」。</summary>
        public bool CreateLessEqual(ConstraintBase owner, params object[] dims)
            => CreateLinearConstraint(ComposeConstraintName(owner, dims), ConstraintSense.LessEqual);

        /// <summary>送出「LHS ≤ rhs」。rhs 取代先前累加的右側常數，行為同 <see cref="CreateGreaterEqual(double, string)"/>。</summary>
        /// <returns>false = 左側沒有任何變數項，限制式未建立。</returns>
        public bool CreateLessEqual(double rhs, string name)
        {
            name = ValidateConstraintName(nameof(CreateLessEqual), name);
            if (_lhsTerms.Count == 0)
            {
                Logging.Warn($"[CONSTRAINT_EMPTY] 未建立限制式 | name={name} reason=lhs_empty result=skipped");
                RecordConstraintBuild(name, false);
                return false;
            }
            _rhsConst = rhs;
            return CreateLinearConstraint(name, ConstraintSense.LessEqual);
        }

        /// <summary>
        /// 送出 pool 為「LHS = RHS」限制式，對應 Model.md 的 <c>=</c>。
        /// 送出形式與清空 pool 的行為同 <see cref="CreateGreaterEqual(string)"/>。
        /// </summary>
        /// <param name="name">限制式名稱；同名第二次起會被略過（warn log）。</param>
        /// <returns>true = pool 有內容；false = pool 是空的。</returns>
        public bool CreateEqual(string name)
            => CreateLinearConstraint(ValidateConstraintName(nameof(CreateEqual), name), ConstraintSense.Equal);

        /// <summary>以 owner 類別名與維度值自動組名，送出「LHS = RHS」。</summary>
        public bool CreateEqual(ConstraintBase owner, params object[] dims)
            => CreateLinearConstraint(ComposeConstraintName(owner, dims), ConstraintSense.Equal);

        /// <summary>送出「LHS = rhs」。rhs 取代先前累加的右側常數，行為同 <see cref="CreateGreaterEqual(double, string)"/>。</summary>
        /// <returns>false = 左側沒有任何變數項，限制式未建立。</returns>
        public bool CreateEqual(double rhs, string name)
        {
            name = ValidateConstraintName(nameof(CreateEqual), name);
            if (_lhsTerms.Count == 0)
            {
                Logging.Warn($"[CONSTRAINT_EMPTY] 未建立限制式 | name={name} reason=lhs_empty result=skipped");
                RecordConstraintBuild(name, false);
                return false;
            }
            _rhsConst = rhs;
            return CreateLinearConstraint(name, ConstraintSense.Equal);
        }

        /// <summary>
        /// 送出範圍限制式 lb ≤ LHS ≤ ub（只用 AddLHS 累積的左側；LHS 常數移到界上抵銷）。
        /// RHS pool（AddRHS 的變數項與常數）不屬於範圍限制式：有內容時寫 <c>[POOL_RHS_IGNORED]</c> warn 後捨棄。
        /// </summary>
        public bool CreateRange(double lb, double ub, string name)
            => CreateRangeCore(lb, ub, ValidateConstraintName(nameof(CreateRange), name));

        /// <summary>以 owner 類別名與維度值自動組名，送出範圍限制式。</summary>
        public bool CreateRange(double lb, double ub, ConstraintBase owner, params object[] dims)
            => CreateRangeCore(lb, ub, ComposeConstraintName(owner, dims));

        /// <summary>
        /// 送出 pool 為「LHS {sense} RHS」限制式，對應 Model.md 的 <c>≥</c> / <c>≤</c> / <c>=</c>。
        /// </summary>
        /// <param name="name">限制式名稱</param>
        /// <param name="sense">限制式類型</param>
        private bool CreateLinearConstraint(string name, ConstraintSense sense)
        {
            if (!CheckHasPool(name))
            {
                RecordConstraintBuild(name, false);
                return false;
            }

            bool created = false;
            try
            {
                if (!_verifyConstraints.Contains(name))
                {
                    AddConstraint(name, LinearExpr(CombinedLhsMinusRhs()), sense, _rhsConst - _lhsConst);
                    _verifyConstraints.Add(name);
                    MarkReferenced(CombinedLhsMinusRhs());
                    created = true;
                }
                else
                {
                    LogDuplicateConstraint(name);
                }
            }
            catch (Exception ex)
            {
                RecordConstraintBuild(name, false);
                Logging.ErrorOnce(
                    ex,
                    "CONSTRAINT_BUILD_FAILED",
                    "限制式建立失敗",
                    $"Create{sense}",
                    name,
                    ex.GetBaseException().Message);
                throw;
            }

            RecordConstraintBuild(name, created);
            ClearPool(); // 同名略過時也要清空，避免下一條限制式混入舊項目。
            return true;
        }

        private bool CreateRangeCore(double lb, double ub, string name)
        {
            if (_lhsTerms.Count == 0)
            {
                Logging.Warn($"[CONSTRAINT_EMPTY] 未建立限制式 | name={name} reason=lhs_empty result=skipped");
                RecordConstraintBuild(name, false);
                ClearPool();
                return false;
            }
            WarnIfRhsPoolIgnored(nameof(CreateRange), name, "range_uses_lhs_only");

            bool created = false;
            try
            {
                if (!_verifyConstraints.Contains(name))
                {
                    AddRangeConstraint(name, LinearExpr(_lhsTerms), lb - _lhsConst, ub - _lhsConst);
                    _verifyConstraints.Add(name);
                    MarkReferenced(_lhsTerms);
                    created = true;
                }
                else
                {
                    LogDuplicateConstraint(name);
                }
            }
            catch (Exception ex)
            {
                RecordConstraintBuild(name, false);
                Logging.ErrorOnce(
                    ex,
                    "CONSTRAINT_BUILD_FAILED",
                    "範圍限制式建立失敗",
                    nameof(CreateRange),
                    name,
                    ex.GetBaseException().Message,
                    $"bounds=[{lb},{ub}]");
                throw;
            }

            RecordConstraintBuild(name, created);
            ClearPool();
            return true;
        }

        #endregion

        #region Pool — 建立目標式

        /// <summary>以 pool 累積的 LHS（變數項 + 常數項）為目標式，設為最小化。RHS pool 不屬於目標式，有內容會 warn 後捨棄。</summary>
        public void CreateMinimize() => SetObjectiveFromPool(ObjectiveSense.Minimize);

        /// <summary>以 pool 累積的 LHS（變數項 + 常數項）為目標式，設為最大化。RHS pool 不屬於目標式，有內容會 warn 後捨棄。</summary>
        public void CreateMaximize() => SetObjectiveFromPool(ObjectiveSense.Maximize);

        private void SetObjectiveFromPool(ObjectiveSense sense)
        {
            int expectedTerms = _lhsTerms.Count + _softPenaltyTerms.Count;
            Logging.Info($"[目標式建構開始] sense={sense} terms={expectedTerms} constant={_lhsConst}");
            if (_lhsTerms.Count == 0 && _softPenaltyTerms.Count == 0)
            {
                Logging.Warn($"[目標式建構完成] sense={sense} terms=0 constant={_lhsConst} reason=no_terms result=skipped");
                ClearPool();
                return;
            }
            WarnIfRhsPoolIgnored($"Create{sense}", "<objective>", "objective_uses_lhs_only");
            try
            {
                _objectiveTerms.Clear();
                _objectiveTerms.AddRange(_lhsTerms);
                _objectiveConstant = _lhsConst;
                _objectiveSense = sense;
                ApplyObjective();
                ClearPool();
                Logging.Info($"[目標式建構完成] sense={sense} terms={expectedTerms} constant={_objectiveConstant} result=success");
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(
                    ex,
                    "OBJECTIVE_BUILD_FAILED",
                    "目標式建立失敗",
                    $"Create{sense}",
                    expectedTerms,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        // 以追蹤的目標式項 + 已累積的軟性 penalty 項重設目標式。
        private void ApplyObjective()
        {
            SetObjective(LinearExpr(_objectiveTerms.Concat(_softPenaltyTerms)), _objectiveConstant, _objectiveSense);
            MarkReferenced(_objectiveTerms.Concat(_softPenaltyTerms));
            _buildSummaryDirty = true;
        }

        /// <summary>
        /// 清掉框架這一側追蹤的目標式（變數項、常數項、soft penalty 項）。
        /// 求解器端移除目標式時（如 OptEngine.ResetConstraint）必須一併呼叫，否則新增軟性限制式時會把舊目標項加回去。
        /// </summary>
        protected void ResetObjectiveTracking()
        {
            _objectiveTerms.Clear();
            _softPenaltyTerms.Clear();
            _objectiveConstant = 0;
        }

        #endregion

        #region Pool — 軟性限制式

        /// <summary>pool 左側目前累積的項，供子類別自訂軟性限制式時取用（唯讀迭代，不要在迭代中改 pool）。</summary>
        protected IEnumerable<(double coef, TVar var)> PoolLhsTerms => _lhsTerms;

        /// <summary>pool 左側目前累積的常數，供子類別自訂軟性限制式時取用。</summary>
        protected double PoolLhsConst => _lhsConst;

        /// <summary>
        /// 是否支援軟性限制式。EngineBase 會用額外變數表示違反量，並將罰分加入目標式，
        /// 只需引擎實作 AddVariable / AddConstraint / SetObjective 即可使用，預設為 true。
        /// </summary>
        public virtual bool SupportsSoftConstraints => true;

        /// <summary>允許 LHS 超過 rhs：加入超出量 dp≥0，建立 lhs − dp &lt;= rhs；罰分為 penalty·dp，最小化時加到目標式，最大化時扣除。</summary>
        public virtual bool CreateLessEqualSoft(double rhs, double penalty)
            => BuildSoft(rhs, penalty, ConstraintSense.LessEqual, null);

        /// <summary>具名軟性 LHS &lt;= rhs：名稱會用於限制式、彈性變數與自動 log。</summary>
        public virtual bool CreateLessEqualSoft(double rhs, double penalty, string name)
            => BuildSoft(rhs, penalty, ConstraintSense.LessEqual,
                ValidateConstraintName(nameof(CreateLessEqualSoft), name));

        /// <summary>以 owner 類別名與維度值自動組名，建立軟性 LHS ≤ rhs。</summary>
        public virtual bool CreateLessEqualSoft(double rhs, double penalty, ConstraintBase owner, params object[] dims)
            => BuildSoft(rhs, penalty, ConstraintSense.LessEqual, ComposeConstraintName(owner, dims));

        /// <summary>允許 LHS 不足 rhs：加入不足量 dn≥0，建立 lhs + dn &gt;= rhs；罰分為 penalty·dn，最小化時加到目標式，最大化時扣除。</summary>
        public virtual bool CreateGreaterEqualSoft(double rhs, double penalty)
            => BuildSoft(rhs, penalty, ConstraintSense.GreaterEqual, null);

        /// <summary>具名軟性 LHS &gt;= rhs：名稱會用於限制式、彈性變數與自動 log。</summary>
        public virtual bool CreateGreaterEqualSoft(double rhs, double penalty, string name)
            => BuildSoft(rhs, penalty, ConstraintSense.GreaterEqual,
                ValidateConstraintName(nameof(CreateGreaterEqualSoft), name));

        /// <summary>以 owner 類別名與維度值自動組名，建立軟性 LHS ≥ rhs。</summary>
        public virtual bool CreateGreaterEqualSoft(double rhs, double penalty, ConstraintBase owner, params object[] dims)
            => BuildSoft(rhs, penalty, ConstraintSense.GreaterEqual, ComposeConstraintName(owner, dims));

        /// <summary>允許 LHS 偏離 rhs：加入不足量 dn 與超出量 dp（皆≥0），建立 lhs + dn − dp == rhs；最小化加上 penalty·(dn+dp)，最大化扣除。</summary>
        public virtual bool CreateEqualSoft(double rhs, double penalty, string name)
            => BuildSoft(rhs, penalty, ConstraintSense.Equal,
                ValidateConstraintName(nameof(CreateEqualSoft), name));

        /// <summary>以 owner 類別名與維度值自動組名，建立軟性 LHS = rhs。</summary>
        public virtual bool CreateEqualSoft(double rhs, double penalty, ConstraintBase owner, params object[] dims)
            => BuildSoft(rhs, penalty, ConstraintSense.Equal, ComposeConstraintName(owner, dims));

        // 軟性限制式通用建構：彈性變數放進變數池，penalty 放進目標式。
        // penalty 方向依目標式 sense：最小化 +penalty（懲罰違反量）、最大化 -penalty。
        private bool BuildSoft(double rhs, double penalty, ConstraintSense sense, string name)
        {
            if (!HasPool)
            {
                string skippedName = name ?? "<auto-soft>";
                Logging.Warn($"[CONSTRAINT_EMPTY] 未建立軟性限制式 | name={skippedName} sense={sense} reason=pool_empty result=skipped");
                RecordConstraintBuild(skippedName, false);
                return false;
            }
            if (name == null)
                name = ModelNaming.ValidateComposedName("automatic soft constraint", $"Soft_{sense}_{++_softCount}");

            double adjustedRhs = rhs + _rhsConst - _lhsConst;
            double p = _objectiveSense == ObjectiveSense.Maximize ? -penalty : penalty;
            var terms = new List<(double coef, TVar var)>(CombinedLhsMinusRhs());
            const double inf = OptBounds.Infinity;
            int expectedVariables = sense == ConstraintSense.Equal ? 2 : 1;
            int variablesBefore = Variables.Count;

            try
            {
                switch (sense)
                {
                    case ConstraintSense.LessEqual:
                        {
                            var dp = AddVariable($"Surplus_{name}", 0, inf, VarType.Continuous);
                            terms.Add((-1.0, dp));
                            AddConstraint(name, LinearExpr(terms), ConstraintSense.LessEqual, adjustedRhs);
                            AddObjectiveTerm(p, dp);
                            break;
                        }
                    case ConstraintSense.GreaterEqual:
                        {
                            var dn = AddVariable($"Deficit_{name}", 0, inf, VarType.Continuous);
                            terms.Add((1.0, dn));
                            AddConstraint(name, LinearExpr(terms), ConstraintSense.GreaterEqual, adjustedRhs);
                            AddObjectiveTerm(p, dn);
                            break;
                        }
                    default: // Equal
                        {
                            var dn = AddVariable($"Delta_Neg_{name}", 0, inf, VarType.Continuous);
                            var dp = AddVariable($"Delta_Pos_{name}", 0, inf, VarType.Continuous);
                            terms.Add((1.0, dn));
                            terms.Add((-1.0, dp));
                            AddConstraint(name, LinearExpr(terms), ConstraintSense.Equal, adjustedRhs);
                            AddObjectiveTerm(p, dn);
                            AddObjectiveTerm(p, dp);
                            break;
                        }
                }
                RecordVariableBuild("SoftConstraint", expectedVariables, Variables.Count - variablesBefore);
                RecordConstraintBuild(name, true);
                MarkReferenced(terms);
                Logging.Info($"[軟性限制式建立完成] name={name} sense={sense} rhs={rhs} penalty={penalty} result=success");
            }
            catch (Exception ex)
            {
                RecordVariableBuild("SoftConstraint", expectedVariables, Math.Max(0, Variables.Count - variablesBefore));
                RecordConstraintBuild(name, false);
                Logging.ErrorOnce(
                    ex,
                    "SOFT_CONSTRAINT_BUILD_FAILED",
                    "軟性限制式建立失敗",
                    nameof(BuildSoft),
                    name,
                    ex.GetBaseException().Message,
                    $"group={ConstraintGroup(name)}");
                throw;
            }
            ClearPool();
            return true;
        }

        /// <summary>
        /// 往目前目標式追加一個 penalty 項（coef·var）：累積後以 SetObjective 重設整個目標式。
        /// 各引擎的 SetObjective 必須取代舊目標式；CPLEX 會先移除舊目標式，再設定新的。
        /// </summary>
        protected virtual void AddObjectiveTerm(double coef, TVar variable)
        {
            _softPenaltyTerms.Add((coef, variable));
            ApplyObjective();
        }

        #endregion
    }
}
