using System;
using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Internal;

namespace OptimFoundation.Core
{
    #region Interfaces and Enums
    /// <summary>
    /// 跨引擎共通的求解設定（停止條件 / 資源 / tuning 旋鈕）；各 solver 的 config 實作此介面，
    /// 並把這些共通項目對映到自家專屬欄位。null = 使用 solver 預設。
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

        /// <summary>求解重點（CPLEX mipEmphasis）。只記原始整數值，各 solver 語意不同、不做正規化。</summary>
        int? Emphasis { get; set; }

        /// <summary>可行性容差（CPLEX epRHS / Solver Epsilon）。</summary>
        double? FeasibilityTol { get; set; }

        /// <summary>最佳性容差（CPLEX epOpt）。</summary>
        double? OptimalityTol { get; set; }

        /// <summary>根節點 LP 演算法（CPLEX algorithm）。取值語意依 solver。</summary>
        int? RootAlgorithm { get; set; }

        /// <summary>前處理開關（CPLEX PreInd）；0 = 關閉。</summary>
        int? Presolve { get; set; }

        /// <summary>啟發式投入程度（CPLEX HeuristicEffort）。</summary>
        double? HeuristicEffort { get; set; }

        /// <summary>記憶體上限 MB（CPLEX workMemory）。</summary>
        double? MemoryLimitMb { get; set; }

        /// <summary>Solve 前 scale guard 門檻：RegisteredVariableCount 超過此值 → PreSolveGuard 只 Warn 不阻擋。預設值 default interface member，不破壞既有實作者。</summary>
        int ScaleWarnThreshold => 10_000_000;
    }


    /// <summary>求解引擎的統一介面：建模型 → 求解 → 取解/telemetry。EngineBase 提供泛型實作。</summary>
    public interface ISolverEngine : IDisposable
    {
        /// <summary>本引擎使用的求解組態。</summary>
        ISolverConfig Config { get; }

        /// <summary>求解狀態；未求解為 NotSolved。</summary>
        SolveStatus Status { get; }

        /// <summary>最近一次 Solve() 的統一 telemetry；尚未求解為 null。</summary>
        SolveMetrics LastMetrics { get; }

        /// <summary>目前模型的問題類型（LP / MILP / IP / BP），向已組裝的 solver 模型取值判定。每次讀取都重新問一次模型，求解前即可呼叫。</summary>
        ModelType ModelType { get; }

        /// <summary>建立 solver 模型並套用組態；建變數 / 限制式前 MUST 先呼叫。</summary>
        void Build();

        /// <summary>求解。回傳 true 代表取得 Optimal 或 Feasible 解。</summary>
        bool Solve();

        /// <summary>目標式解值；MUST 在求解成功後呼叫。</summary>
        double GetObjectiveValue();

        /// <summary>依變數全名取解值（TypeName@s1@s2@…）；MUST 在求解成功後呼叫。</summary>
        double GetVariableValue(string name);

        /// <summary>取解結果字典；varTypeName = null 回傳所有變數，否則只回該型別（前綴 "TypeName@"）。</summary>
        IReadOnlyDictionary<string, double> GetSolution(string varTypeName = null);

        /// <summary>
        /// 以「變數全名 → 值」提供一組 MIP start；名稱格式同 <see cref="GetSolution"/>，可直接把上一個 engine 的解接過來。
        /// MUST 在模型建完、Solve() 之前呼叫。LP 模型沒有 MIP start 可用，會 warn 後略過。
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
    /// 所有 solver 引擎的泛型基底：把「建模」與「呼叫 solver」分離。
    /// 泛型參數 TModel/TVar/TExpr/TConstr 是各 solver 的原生型別；子類別只需實作 Solver Contract 那組 abstract。
    /// 提供三大共用機制：① 變數管理（Build*Vs 批次建立 + VariableSets/Variables 索引）；
    /// ② Pool API（AddLHS/AddRHS 累積左右兩側 → Create* 送出，框架自動算 LHS−RHS，免手動移項）；
    /// ③ 軟性限制式（penalty 法通用實作）。
    /// </summary>
    public abstract class EngineBase<TModel, TVar, TExpr, TConstr> : ISolverEngine, ITrajectorySource
    {
        /// <summary>各 solver 的模型物件（CPLEX->Cplex …）；Configuration() 建立、Dispose() 釋放。</summary>
        protected TModel Model;

        /// <summary>全部變數：key = 變數名，value = solver 原生變數。含軟性限制式自動加的彈性變數。</summary>
        protected readonly Dictionary<string, TVar> Variables = new Dictionary<string, TVar>();

        /// <summary>依變數型別分組：型別名 → (變數名 → 原生變數)。只含經 Build*Vs 建立的變數。</summary>
        protected readonly Dictionary<string, Dictionary<string, TVar>> VariableSets = new Dictionary<string, Dictionary<string, TVar>>();

        /// <summary>已建立的變數總數（Variables dict 的大小，含彈性變數）。</summary>
        public int VariableCount => Variables.Count;
        /// <summary>
        /// 經 Build*Vs 登記進 VariableSets 的變數總數。
        /// 與 <see cref="VariableCount"/> 的差別：後者算的是 Variables dict，額外含軟性限制式自動加的彈性變數（Surplus_/Deficit_/Delta_*）。
        /// </summary>
        public int RegisteredVariableCount => VariableSets.Values.Sum(s => s.Count);

        /// <summary>
        /// 目前模型的問題類型：無 Integer / Binary → LP；連續與 Integer / Binary 並存 → MILP；
        /// 全為 Binary → BP；無連續且含 Integer → IP。
        /// 值向已組裝的 solver 模型取得（見 <see cref="ReadModelComposition"/>），不是框架這一側的記帳，
        /// 因此自建與 ImportModel 匯入兩條路徑同一套答案；每次讀取都重新問一次模型，求解前即可呼叫。
        /// 軟性限制式的彈性變數是連續變數，因此 IP / BP 模型加了軟性限制式會判定為 MILP。
        /// </summary>
        public ModelType ModelType => ResolveModelType(ReadModelComposition());

        /// <summary>建構時傳入的求解器組態；由各 engine 在 Configuration() 內逐項套用到 solver。</summary>
        public ISolverConfig Config { get; protected set; }

        /// <summary>求解狀態；由各 engine 的 SolveCore() 回填，未求解前為 NotSolved。</summary>
        public SolveStatus Status { get; protected set; } = SolveStatus.NotSolved;

        /// <summary>求得的最佳目標值；由 SolveCore() 回填，未求解前為 0。</summary>
        public double BestObjValue { get; protected set; }

        /// <summary>求解結束時的 MIP gap（相對誤差）；由 SolveCore() 回填，LP 問題為 0。</summary>
        public double MIPGap { get; protected set; }

        /// <summary>最近一次 Solve() 的統一 telemetry；由各 engine 的 Solve() 回填。</summary>
        public SolveMetrics LastMetrics { get; protected set; }

        /// <summary>已建立的限制式數量；預設 0，需要的 engine override。</summary>
        public virtual int ConstraintCount => 0;

        // 建立統計的計數單位：Expected = 應該建幾個，Actual = 實際成功建了幾個（兩者不等即代表有被略過或失敗）
        private sealed class BuildCount
        {
            /// <summary>預期建立數（變數名笛卡兒積數量 / 嘗試送出的限制式條數。</summary>
            public int Expected;

            /// <summary>實際建立數；重複名稱被略過或建立失敗都不計入。</summary>
            public int Actual;
        }

        private readonly Dictionary<string, BuildCount> _variableBuildCounts = new Dictionary<string, BuildCount>();
        private readonly Dictionary<string, BuildCount> _constraintBuildCounts = new Dictionary<string, BuildCount>();
        private bool _buildSummaryDirty = true;

        // 建模記帳的型別分布、匯入部分與目標式：與 solver 模型對帳用（見 ReconcileModelStats）。
        // 匯入的記帳與 Build*Vs 的記帳分開放，VariableBuildCounts / ConstraintBuildCounts 維持「只記框架親手建的」語意。
        private readonly int[] _ledgerVarTypes = new int[3];
        private readonly int[] _importedVarTypes = new int[3];
        private int _importedConstraints;
        private bool _imported;
        private ObjectiveSense? _ledgerObjective;

        // 被限制式或目標式引用過的變數。solver 只收這些——宣告了卻沒用到的變數會讓框架比 solver 多，對帳時拿來點名
        private readonly HashSet<TVar> _referencedVariables = new HashSet<TVar>();

        /// <summary>
        /// 最近一次 <see cref="Solve"/> 前的模型統計對帳結果（框架建模記帳 vs solver 模型實際）；尚未求解為 null。
        /// 同一份也放在 <see cref="SolveMetrics.ModelStats"/>，實驗紀錄因此帶得到。要隨時對帳改呼叫 <see cref="ReconcileModelStats"/>。
        /// </summary>
        public ModelStatsReport ModelStats { get; private set; }

        /// <summary>
        /// 各變數型別的「預期 / 實際」建立數——摘要 log 印的同一份資料，開發時可直接看。
        /// 每次存取取當下的值，改動它不會影響引擎。
        /// </summary>
        public IReadOnlyDictionary<string, (int Expected, int Actual)> VariableBuildCounts
            => _variableBuildCounts.ToDictionary(kv => kv.Key, kv => (kv.Value.Expected, kv.Value.Actual));

        /// <summary>各限制式群組的「預期 / 實際」建立數。語意同 <see cref="VariableBuildCounts"/>。</summary>
        public IReadOnlyDictionary<string, (int Expected, int Actual)> ConstraintBuildCounts
            => _constraintBuildCounts.ToDictionary(kv => kv.Key, kv => (kv.Value.Expected, kv.Value.Actual));

        /// <summary>
        /// 盡力擷取選用的整數型 telemetry（如 node 數 / iteration 數）。各 solver 方法名不一，
        /// 依序嘗試傳入的無參數方法名，取不到回 null（不丟例外）。
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

        // ── ITrajectorySource 預設：不支援（CPLEX override）。未支援者呼叫端不報錯 ──
        /// <summary>本 engine 是否能記錄求解過程的收斂軌跡。預設 false，CPLEX override 為 true。</summary>
        public virtual bool SupportsTrajectory => false;

        /// <summary>開啟收斂軌跡記錄，MUST 在 Solve() 之前呼叫。不支援的 engine 為 no-op（不丟例外）。</summary>
        public virtual void EnableTrajectory() { /* no-op；支援的 engine override */ }

        /// <summary>最近一次求解的收斂軌跡（時間 / incumbent / bound 序列）；未開啟或不支援時回空清單。</summary>
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
        // 各 engine 目標式語意不同（CPLEX 新增 / Solver 覆寫），統一由 AddObjectiveTerm 處理。
        private readonly List<(double coef, TVar var)> _objectiveTerms = new List<(double, TVar)>();
        private readonly List<(double coef, TVar var)> _softPenaltyTerms = new List<(double, TVar)>();
        private double _objectiveConstant = 0;
        private ObjectiveSense _objectiveSense = ObjectiveSense.Minimize;
        private int _softCount = 0;

        // ── 建模當下狀態（唯讀；開發時看得見自己組到哪）────────────────

        /// <summary>目前 pool 累積的內容：LHS / RHS 各幾項、常數各多少。<see cref="CreateEqual(string)"/> 等會清空它。</summary>
        public (int LhsTerms, double LhsConst, int RhsTerms, double RhsConst) PoolState
            => (_lhsTerms.Count, _lhsConst, _rhsTerms.Count, _rhsConst);

        /// <summary>目標式方向（Minimize / Maximize）。</summary>
        public ObjectiveSense ObjectiveSense => _objectiveSense;

        /// <summary>目標式累積的項數（不含尚未併入的 soft penalty）。</summary>
        public int ObjectiveTermCount => _objectiveTerms.Count;

        /// <summary>目標式常數項，取自建目標式時 pool 的 LHS 常數（<see cref="AddLHS(double)"/>）。</summary>
        public double ObjectiveConstant => _objectiveConstant;

        /// <summary>已建立的軟性限制式條數。</summary>
        public int SoftConstraintCount => _softCount;

        /// <summary>soft penalty 併入目標式的項數。</summary>
        public int SoftPenaltyTermCount => _softPenaltyTerms.Count;

        /// <summary>
        /// 建立引擎，只記下組態不碰 solver；真正建立 solver 模型物件要等 <see cref="Build"/>。
        /// </summary>
        /// <param name="config">求解器組態；傳 null 時 PreSolveGuard 的規模檢查會被跳過。</param>
        protected EngineBase(ISolverConfig config)
        {
            Config = config;
        }

        #region Solver Contract
        // ════════════════════════════════════════════════════════════════
        // 新增 Solver 必須 override 的 abstract 方法：
        //   Configuration  — 建立 Model 物件並套用所有 Config 參數
        //   AddVariable    — 向 solver 新增單一變數，同時寫入 Variables dict
        //   LinearExpr     — 從 (coef, var) list 建立 solver 的線性表達式物件
        //   AddConstraint  — 新增一般限制式（<=  ==  >=）
        //   AddRangeConstraint — 新增範圍限制式（lb <= expr <= ub）
        //   SetObjective   — 設定目標式（含常數項）與方向（Minimize / Maximize）
        //   SetVariableBounds — 直接修改已建立變數的 LB / UB
        //   AddMIPStartCore — 把已解析的 (變數, 值) 交給 solver 當 MIP start
        //   ReadModelComposition — 向 solver 模型讀變數型別組成，供 ModelType 判定問題類型
        //   ReadSolverModelCounts — 向 solver 模型讀規模統計，供 ReconcileModelStats 與框架記帳對帳
        //   BuildCore      — 入口：呼叫 Configuration(Config) 完成初始化（由 Build() template method 呼叫）
        //   SolveCore      — 求解，回傳 bool（true = Optimal or Feasible）（由 Solve() template method 呼叫，前面先跑 PreSolveGuard）
        //   GetObjectiveValue / GetVariableValue / Dispose
        //
        // 可選 override 的 virtual 方法（EngineBase 有 default 實作）：
        //   AddVariables   — 批次建立變數（solver override 用原生 batch API 提升效能）
        //   BuildCVs / BuildIVs / BuildBVs — 批次建立變數，寫入 VariableSets
        //   CreateLeSoft / CreateGeSoft / CreateEqSoft — 軟性限制式（penalty 法）
        // ════════════════════════════════════════════════════════════════

        /// <summary>建立 solver 原生模型物件並把 config 的每一項參數套用上去。由 BuildCore() 呼叫。</summary>
        public abstract void Configuration(ISolverConfig config);

        /// <summary>向 solver 新增單一變數，並以 name 為 key 寫入 Variables dict。</summary>
        /// <param name="name">變數全名（Build*Vs 產生的格式為 TypeName@s1@s2@…）。</param>
        /// <param name="lb">下界。</param>
        /// <param name="ub">上界。</param>
        /// <param name="type">Continuous / Integer / Binary。</param>
        /// <returns>solver 的原生變數物件。</returns>
        protected abstract TVar AddVariable(string name, double lb, double ub, VarType type);

        /// <summary>把 (係數, 變數) 序列組成 solver 的線性表達式物件。terms 只會被迭代一次。</summary>
        protected abstract TExpr LinearExpr(IEnumerable<(double coef, TVar var)> terms);

        /// <summary>新增一般限制式 lhs (≤ | = | ≥) rhs，並把 name 設為 solver 端的限制式名稱。</summary>
        protected abstract TConstr AddConstraint(string name, TExpr lhs, ConstraintSense sense, double rhs);

        /// <summary>新增範圍限制式 lb ≤ expr ≤ ub。</summary>
        protected abstract TConstr AddRangeConstraint(string name, TExpr expr, double lb, double ub);

        /// <summary>
        /// 設定目標式 expr + constant。實作 MUST 為「覆寫」語意——重複呼叫要換掉舊目標式而非疊加，
        /// 否則軟性限制式的 penalty 累加（每加一項就重設一次目標式）會產生多個目標式。
        /// </summary>
        /// <param name="expr">目標式的變數項。</param>
        /// <param name="constant">目標式常數項（offset）；MUST 帶進 solver，否則求得的目標值會與 Model.md 差這個常數。</param>
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
        /// 實作 MUST 問模型本身（CPLEX 為 Ncols / NbinVars / NintVars / IsMIP），NEVER 改用框架的 Variables 索引——
        /// 匯入的模型只存在於 solver 那一側，兩條路徑要同一個答案就只能問模型。模型尚未建立時回零值。
        /// </summary>
        /// <returns>
        /// 三個型別的變數數，加上模型是否含離散結構。後者涵蓋 Integer / Binary 以外的離散元素
        /// （semi-continuous、SOS），有它才能在沒有任何 Integer / Binary 變數時仍判定為 MILP。
        /// </returns>
        protected abstract (int Continuous, int Integer, int Binary, bool HasDiscreteStructure) ReadModelComposition();

        /// <summary>
        /// 向 solver 模型讀出規模統計，供 <see cref="ReconcileModelStats"/> 與框架的建模記帳對帳。
        /// 實作 MUST 問模型本身（CPLEX 為 Ncols / Nrows / NbinVars / NintVars / NSOSs / NQCs / GetObjective…），
        /// NEVER 用框架的 Variables 索引或建立統計湊數——那樣兩邊永遠相等，對帳就失去意義。模型尚未建立時回全零。
        /// </summary>
        protected abstract ModelCounts ReadSolverModelCounts();

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
        /// 求解入口：建模摘要 → 模型統計對帳（寫 log、存進 <see cref="ModelStats"/>）→ PreSolveGuard()（scale guard）→ SolveCore()（各 engine 實作）。
        /// 對帳不一致只寫 <c>[MODEL_STATS_MISMATCH]</c> warn，不阻擋求解。
        /// </summary>
        public bool Solve()
        {
            try
            {
                LogBuildSummary();
                ModelStats = ReconcileModelStats();
                LogModelStats(ModelStats);
                PreSolveGuard();
                bool ok = SolveCore();
                if (LastMetrics != null) LastMetrics.ModelStats = ModelStats;
                return ok;
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

        /// <summary>各 engine 的建模實作；由 <see cref="Build"/> 這個 template method 呼叫，消費端不直接叫。</summary>
        protected abstract void BuildCore();

        /// <summary>各 engine 的求解實作；由 <see cref="Solve"/> 呼叫。回傳 true 代表取得 Optimal 或 Feasible 解。</summary>
        protected abstract bool SolveCore();

        // VariableCount > Config.ScaleWarnThreshold → Logging.Warn（只警告不阻擋，大但合法的模型不該被擋）。
        // 量的是 Variables 索引而非 RegisteredVariableCount：軟性限制式的彈性變數與匯入的模型都不會登記進 VariableSets，
        // 用後者會讓這些情況的規模恆為 0，guard 形同失效。Config 為 null 時防禦性跳過（不炸）。
        private void PreSolveGuard()
        {
            if (Config == null) return;
            if (VariableCount > Config.ScaleWarnThreshold)
                Logging.Warn($"[MODEL_SCALE_WARNING] 變數規模超過警告門檻 | count={VariableCount} threshold={Config.ScaleWarnThreshold} result=continued");
        }

        /// <summary>取目標式的解值。MUST 在 Solve() 回傳 true 之後呼叫，否則各 solver 會丟自己的例外。</summary>
        public abstract double GetObjectiveValue();

        /// <summary>依變數全名取解值（格式 TypeName@s1@s2@…）。MUST 在求解成功後呼叫。</summary>
        public abstract double GetVariableValue(string name);

        /// <summary>釋放 solver 原生資源（CPLEX 的 native handle）。用 using 包住 engine，或交由 OptProject / OptExperiment 管理。</summary>
        public abstract void Dispose();

        #endregion

        #region VariableManager — 批次建立變數

        /// <summary>
        /// 批次建立變數並寫入 Variables dict。
        /// Default：逐筆呼叫 AddVariable。
        /// Solver override 此方法可使用原生 array API（CPLEX NumVarArray）
        /// 大幅減少 .NET ↔ native interop 次數。
        /// </summary>
        protected virtual void AddVariables(IReadOnlyList<string> names, double lb, double ub, VarType type)
        {
            foreach (var name in names)
            {
                AddVariable(name, lb, ub, type);
            }
        }

        // 批次建立某型別的所有變數：組出全部變數名 → 建到 solver → 登記進 VariableSets[型別名] 供之後查詢
        private void BatchBuild<TVariable>(double lb, double ub, VarType type, object[] sets)
            => BatchBuild(typeof(TVariable).Name, () => VariableBuilder.GetVarNames<TVariable>(sets), lb, ub, type);

        // string 版：setName 直接當變數名 head，其餘流程與泛型版共用
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

                stage = "variable_set_initialization";
                if (!VariableSets.ContainsKey(setName))
                    VariableSets[setName] = new Dictionary<string, TVar>();

                // 2) 實際在 solver 建立這些變數（子類別可用原生 batch API 加速）
                stage = "solver_creation";
                AddVariables(names, lb, ub, type);

                // 3) 登記到該型別的 VariableSet，供 ReadVar / GetSetVarValues 依型別查詢
                stage = "variable_set_registration";
                var varSet = VariableSets[setName];
                foreach (var name in names)
                    varSet[name] = Variables[name];

                int actual = Variables.Count - before;
                RecordVariableBuild(setName, type, names.Count, actual);
                Logging.Info($"[變數建立完成] type={setName} count={actual}/{names.Count}");
            }
            catch (Exception ex)
            {
                int actual = Math.Max(0, Variables.Count - before);
                string expected = names == null ? "unknown" : names.Count.ToString();
                if (names != null)
                    RecordVariableBuild(setName, type, names.Count, actual);
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
        /// <typeparam name="TVariable">變數類別；其 property 宣告順序 MUST 與 sets 傳入順序一致，否則之後 AddLHS 會找不到變數。</typeparam>
        /// <param name="sets">各維度的 set；框架取笛卡兒積產生所有變數名。</param>
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

        /// <summary>批次建立整數變數，界限 [0, <see cref="OptBounds.Infinity"/>]。其餘語意同 <see cref="BuildCVs{TVariable}(object[])"/>。</summary>
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

        /// <summary>批次建立 0/1 二元變數。其餘語意同 <see cref="BuildCVs{TVariable}(object[])"/>。</summary>
        public virtual void BuildBVs<TVariable>(params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Binary, nameof(BuildBVs));
            BatchBuild<TVariable>(0, 1, VarType.Binary, sets);
        }

        /// <summary>
        /// 命名天條路徑：變數型別由類別名前綴決定——VariableB_（Binary, [0,1]）/
        /// VariableC_（Continuous）/ VariableI_（Integer）。
        /// 自訂 bounds 或不依天條命名的類別 → 改用 BuildCVs / BuildIVs / BuildBVs 顯式指定。
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
        // 少掉的兩項都是「靠類別才做得到」的檢查：維度數量比對，以及類別名前綴與型別的一致性驗證。
        // setName 仍會經 ModelNaming 驗證（不可空白、不可含保留字元、不可數字開頭），確保產出的名稱送得進 solver。

        /// <summary>批次建立連續變數的 string 版，界限 [0, <see cref="OptBounds.Infinity"/>]。</summary>
        /// <param name="setName">變數名 head，取代泛型版的類別名；同時是 VariableSets 的 key。</param>
        /// <param name="sets">各維度的 set；框架取笛卡兒積產生所有變數名。</param>
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
        /// 依變數實例查出 solver 原生變數：型別名找 VariableSet，該實例的 ToString() 找變數名。
        /// </summary>
        /// <param name="searchData">填好各維度值的變數類別實例，例：new VariableB_Assign { EMP = "E1", DATE = "D1" }。</param>
        /// <exception cref="KeyNotFoundException">該型別未建立，或這組索引值不在建立範圍內。</exception>
        protected TVar ReadVar(object searchData)
        {
            string setName = searchData.GetType().Name;
            string varName = searchData.ToString();

            if (VariableSets.TryGetValue(setName, out var set) && set.TryGetValue(varName, out var v))
                return v;

            throw Logging.ErrorOnce(
                new KeyNotFoundException($"找不到變數 '{varName}' in VariableSet '{setName}'"),
                "VARIABLE_NOT_FOUND",
                "變數不存在",
                nameof(ReadVar),
                varName,
                "variable_not_built",
                $"type={setName}");
        }

        /// <summary>
        /// 依變數全名查出 solver 原生變數。與 <see cref="ReadVar(object)"/> 等價，
        /// 差別只在跳過「用實例反推型別與名稱」那一步，直接拿名稱查 Variables 索引。
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

        /// <summary>取某變數型別的整組變數（key = 變數全名）。</summary>
        /// <exception cref="KeyNotFoundException">該型別尚未經 Build*Vs 建立。</exception>
        protected Dictionary<string, TVar> GetVariableSet(string setName)
        {
            if (VariableSets.TryGetValue(setName, out var set))
                return set;
            throw Logging.ErrorOnce(
                new KeyNotFoundException($"找不到 VariableSet '{setName}'"),
                "VARIABLE_SET_NOT_FOUND",
                "變數集合不存在",
                nameof(GetVariableSet),
                setName,
                "variable_set_not_built");
        }

        /// <summary>所有經 Build*Vs 建立的變數名（不含軟性限制式的彈性變數）。</summary>
        public string[] GetAllVarNames()
            => VariableSets.Values.SelectMany(s => s.Keys).ToArray();

        /// <summary>
        /// 變數名清單，可選擇是否納入沒有登記在任何 VariableSet 裡的變數
        /// （軟性限制式的彈性變數、以及不經 Build*Vs 直接建立的變數）。
        /// </summary>
        /// <param name="includeUnregistered">false 等同 <see cref="GetAllVarNames()"/>；true 改為列出 Variables 索引的全部變數。</param>
        public string[] GetAllVarNames(bool includeUnregistered)
            => includeUnregistered ? Variables.Keys.ToArray() : GetAllVarNames();

        /// <summary>某變數型別的全部變數名；型別不存在回空陣列（不丟例外）。</summary>
        public string[] GetSetVarNames<TVariable>()
            => GetSetVarNames(typeof(TVariable).Name);

        /// <summary><see cref="GetSetVarNames{TVariable}"/> 的 string 版；setName 不存在回空陣列（不丟例外）。</summary>
        public string[] GetSetVarNames(string setName)
        {
            return setName != null && VariableSets.TryGetValue(setName, out var set)
                ? set.Keys.ToArray()
                : Array.Empty<string>();
        }

        /// <summary>取某變數型別的全部解值，key = 完整變數名（TypeName@…）。求解後呼叫；型別不存在回空字典。</summary>
        public Dictionary<string, double> GetSetVarValues<TVariable>()
            => GetSetVarValues(typeof(TVariable).Name);

        /// <summary><see cref="GetSetVarValues{TVariable}"/> 的 string 版；setName 不存在回空字典。</summary>
        public Dictionary<string, double> GetSetVarValues(string setName)
        {
            try
            {
                if (setName == null || !VariableSets.TryGetValue(setName, out var set))
                    return new Dictionary<string, double>();
                return set.ToDictionary(kvp => kvp.Key, kvp => GetVariableValue(kvp.Key));
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "SOLUTION_READ_FAILED", "公開 API 執行失敗", nameof(GetSetVarValues), setName,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        /// <summary>取解值字典；varTypeName=null 回全部變數，否則只回該型別（前綴 "TypeName@"）。</summary>
        public virtual IReadOnlyDictionary<string, double> GetSolution(string varTypeName = null)
        {
            try
            {
                if (varTypeName != null && VariableSets.TryGetValue(varTypeName, out var set))
                    return set.ToDictionary(kvp => kvp.Key, kvp => GetVariableValue(kvp.Key));

                var result = new Dictionary<string, double>();
                string prefix = varTypeName == null ? null : varTypeName + "@";
                foreach (var key in Variables.Keys)
                {
                    if (prefix == null || key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        result[key] = GetVariableValue(key);
                }
                return result;
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
        /// MUST 在模型建完、Solve() 之前呼叫。LP 模型沒有 MIP start 可用 → warn 後略過；
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

        #region VariableManager — 重設

        /// <summary>
        /// 清掉框架這一側的變數索引與建立統計（solver 內已建的變數不會消失）。
        /// 用於同一個 process 重跑建模流程；跑實驗換組態時 ALWAYS 建新 engine，不要靠這個重置。
        /// </summary>
        public void VarSetsReset()
        {
            VariableSets.Clear();
            Variables.Clear();
            _variableBuildCounts.Clear();
            Array.Clear(_ledgerVarTypes, 0, _ledgerVarTypes.Length);
            Array.Clear(_importedVarTypes, 0, _importedVarTypes.Length);
            _referencedVariables.Clear();
            _buildSummaryDirty = true;
        }

        /// <summary>清空限制式的「已用名稱」集合與建立統計；清掉後同名限制式可再次送出（不再被當重複略過）。</summary>
        protected void ResetVerifyConstraints()
        {
            _verifyConstraints.Clear();
            _constraintBuildCounts.Clear();
            _importedConstraints = 0;
            _buildSummaryDirty = true;
        }

        private void ResetBuildStatistics()
        {
            _variableBuildCounts.Clear();
            _constraintBuildCounts.Clear();
            Array.Clear(_ledgerVarTypes, 0, _ledgerVarTypes.Length);
            Array.Clear(_importedVarTypes, 0, _importedVarTypes.Length);
            _importedConstraints = 0;
            _imported = false;
            _ledgerObjective = null;
            _referencedVariables.Clear();
            _buildSummaryDirty = true;
        }

        private void RecordVariableBuild(string type, VarType varType, int expected, int actual)
        {
            if (!_variableBuildCounts.TryGetValue(type, out var count))
            {
                count = new BuildCount();
                _variableBuildCounts[type] = count;
            }
            count.Expected += expected;
            count.Actual += actual;
            _ledgerVarTypes[(int)varType] += actual;
            _buildSummaryDirty = true;
        }

        #region 建模記帳 — 給 engine 子類別的入口

        /// <summary>
        /// 匯入模型檔 re-index 完成後登記記帳：匯入的內容不經 Build*Vs / Create*，由 engine 把索引到的數量報上來。
        /// 同時以檔案裡的目標式方向同步 <see cref="ObjectiveSense"/>，否則匯入 maximize 模型會被當成 minimize，軟性 penalty 也會反號。
        /// </summary>
        /// <param name="continuous">索引到的連續變數數。</param>
        /// <param name="integer">索引到的 Integer 變數數。</param>
        /// <param name="binary">索引到的 Binary 變數數。</param>
        /// <param name="constraints">索引到的線性限制式條數。</param>
        /// <param name="objective">檔案裡的目標式方向；null = 沒有目標式。</param>
        protected void RecordImportedModel(int continuous, int integer, int binary, int constraints, ObjectiveSense? objective)
        {
            // 匯入會整個換掉 solver 模型，先前 Build*Vs 建的東西已不在模型裡，記帳跟著歸零
            _variableBuildCounts.Clear();
            Array.Clear(_ledgerVarTypes, 0, _ledgerVarTypes.Length);
            _referencedVariables.Clear();

            _importedVarTypes[(int)VarType.Continuous] = continuous;
            _importedVarTypes[(int)VarType.Integer] = integer;
            _importedVarTypes[(int)VarType.Binary] = binary;
            _importedConstraints = constraints;
            _imported = true;
            _ledgerObjective = objective;
            if (objective.HasValue) _objectiveSense = objective.Value;
            // 匯入的變數都來自模型本身的矩陣，本來就在 solver 模型內，視同已引用
            foreach (var v in Variables.Values) _referencedVariables.Add(v);
            _buildSummaryDirty = true;
        }

        /// <summary>子類別不經 pool、直接以 primitive 建變數時登記記帳（例：OptEngine.CreateVar）。</summary>
        protected void RecordDirectVariable(string name, VarType varType)
            => RecordVariableBuild(VariableGroup(name), varType, 1, 1);

        /// <summary>子類別不經 pool、直接以 primitive 建限制式時登記記帳（例：OptEngine.AddLE）。</summary>
        protected void RecordDirectConstraint(string name)
            => RecordConstraintBuild(name, true);

        /// <summary>子類別不經 pool、直接以 primitive 設目標式時登記記帳，並同步 <see cref="ObjectiveSense"/>（例：OptEngine.Maximize）。</summary>
        protected void RecordDirectObjective(ObjectiveSense sense)
        {
            _objectiveSense = sense;
            _ledgerObjective = sense;
            _buildSummaryDirty = true;
        }

        /// <summary>
        /// solver 端的限制式與目標式全部移除、變數保留時呼叫（例：OptEngine.ResetConstraint）。
        /// 否則上一輪引用過的變數會一直算「已引用」，重建後沒用到的變數就點不出名。
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

            // 每行刻意印兩種來源的數字，互相對帳：
            //   已建立=實際/預期 → 建模端的記帳（RecordVariableBuild / RecordConstraintBuild 累加）
            //   模型內合計 / solver 實際持有 → solver 手上真正的庫存
            // 正常情況兩邊該對得上；對不上代表有建立路徑漏了記帳，或中途被 reset 過
            // （例：再次呼叫 Configuration() 會清空 solver 的限制式，但不會清 Core 的計數器）。
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

        #region 模型統計對帳

        /// <summary>
        /// 對帳框架的建模統計與 solver 模型實際持有的內容：變數總數與 Binary / Integer / Continuous 分布、限制式、
        /// 框架不索引的特殊元素、目標式方向。變數與限制式另外比對框架索引，落差才分得出是哪一段出問題。
        /// <see cref="Solve"/> 前會自動跑一次並寫 log；Build() 之後任何時候都能手動呼叫，不會改動模型。
        /// </summary>
        /// <returns>對帳結果；<see cref="ModelStatsReport.IsMatch"/> 為 false 時，Mismatches 逐項帶可能原因。</returns>
        public ModelStatsReport ReconcileModelStats()
        {
            var solver = ReadSolverModelCounts() ?? new ModelCounts();
            var framework = new ModelCounts
            {
                Continuous = _ledgerVarTypes[(int)VarType.Continuous] + _importedVarTypes[(int)VarType.Continuous],
                Integer = _ledgerVarTypes[(int)VarType.Integer] + _importedVarTypes[(int)VarType.Integer],
                Binary = _ledgerVarTypes[(int)VarType.Binary] + _importedVarTypes[(int)VarType.Binary],
                Constraints = _constraintBuildCounts.Values.Sum(c => c.Actual) + _importedConstraints,
                Objective = _ledgerObjective,
            };
            framework.Variables = framework.Continuous + framework.Integer + framework.Binary;

            var report = new ModelStatsReport
            {
                Source = !_imported ? "Authored"
                    : _variableBuildCounts.Count + _constraintBuildCounts.Count > 0 ? "Imported+Authored"
                    : "Imported",
                Framework = framework,
                IndexedVariables = VariableCount,
                IndexedConstraints = ConstraintCount,
                Solver = solver,
            };

            CheckVariables(report);
            bool variablesDiffer = report.Mismatches.Count > 0;
            CheckVariableType(report, "Binary", framework.Binary, solver.Binary, variablesDiffer);
            CheckVariableType(report, "Integer", framework.Integer, solver.Integer, variablesDiffer);
            CheckVariableType(report, "Continuous", framework.Continuous, solver.Continuous, variablesDiffer);
            CheckConstraints(report);
            CheckSpecialElements(report);
            CheckObjective(report);
            return report;
        }

        private void CheckVariables(ModelStatsReport report)
        {
            int ledger = report.Framework.Variables;
            int index = report.IndexedVariables;
            int solver = report.Solver.Variables;
            if (ledger == index && index == solver) return;

            var reasons = new List<string>();
            if (ledger != index)
                reasons.Add($"框架索引比建模記帳{(index > ledger ? "多" : "少")} {Math.Abs(index - ledger)} 個：有變數沒經過 Build*Vs / 軟性限制式 / 匯入等建模入口就進出索引");
            if (index > solver)
            {
                var unreferenced = Variables.Where(kv => !_referencedVariables.Contains(kv.Value)).Select(kv => kv.Key).ToList();
                if (unreferenced.Count > 0)
                    reasons.Add($"{unreferenced.Count} 個變數已宣告但沒被任何限制式或目標式引用，solver 不會收進模型（{GroupSummary(unreferenced)}）sample={string.Join(",", unreferenced.Take(5))}");
                if (unreferenced.Count != index - solver)
                    reasons.Add($"框架索引比 solver 多 {index - solver} 個、未引用 {unreferenced.Count} 個，兩者不符：另有變數被移出模型，或模型重建（再次 Configuration）後索引沒清");
            }
            else if (index < solver)
            {
                reasons.Add($"solver 模型有 {solver - index} 個框架不認得的變數：有程式繞過框架直接加進 solver 模型");
            }

            report.Mismatches.Add(new ModelStatsMismatch
            {
                Item = "Variables",
                Framework = Text(ledger),
                Index = Text(index),
                Solver = Text(solver),
                Reason = string.Join("；", reasons),
            });
        }

        private static void CheckVariableType(ModelStatsReport report, string item, int framework, int solver, bool variablesDiffer)
        {
            if (framework == solver) return;
            report.Mismatches.Add(new ModelStatsMismatch
            {
                Item = item,
                Framework = Text(framework),
                Solver = Text(solver),
                Reason = variablesDiffer
                    ? "與 Variables 列同因（該型別的變數有未引用或繞過框架的）"
                    : "總數對得上但型別分布不同：solver 端有變數型別被改動（例：繞過框架做型別轉換）",
            });
        }

        private static void CheckConstraints(ModelStatsReport report)
        {
            int ledger = report.Framework.Constraints;
            int index = report.IndexedConstraints;
            int solver = report.Solver.Constraints;
            if (ledger == index && index == solver) return;

            var reasons = new List<string>();
            if (ledger != index)
                reasons.Add($"框架索引比建模記帳{(index > ledger ? "多" : "少")} {Math.Abs(index - ledger)} 條：有限制式沒經過 Create* / 軟性限制式 / 匯入等建模入口就進出索引");
            if (index < solver)
                reasons.Add($"solver 模型有 {solver - index} 條框架不認得的限制式：有程式繞過框架直接加進 solver 模型");
            else if (index > solver)
                reasons.Add($"框架索引有 {index - solver} 條 solver 模型沒有：限制式被移出模型，或模型重建（再次 Configuration）後索引沒清");

            report.Mismatches.Add(new ModelStatsMismatch
            {
                Item = "Constraints",
                Framework = Text(ledger),
                Index = Text(index),
                Solver = Text(solver),
                Reason = string.Join("；", reasons),
            });
        }

        private static void CheckSpecialElements(ModelStatsReport report)
        {
            if (report.Framework.SpecialElements == report.Solver.SpecialElements) return;
            report.Mismatches.Add(new ModelStatsMismatch
            {
                Item = "SpecialElements",
                Framework = Text(report.Framework.SpecialElements),
                Solver = Text(report.Solver.SpecialElements),
                Reason = $"模型含框架不建立也不索引的元素（{report.Solver.SpecialDetail}）：框架的統計、IIS 分析與取解都不涵蓋它們",
            });
        }

        private static void CheckObjective(ModelStatsReport report)
        {
            var framework = report.Framework.Objective;
            var solver = report.Solver.Objective;
            if (framework == solver) return;
            report.Mismatches.Add(new ModelStatsMismatch
            {
                Item = "Objective",
                Framework = SenseText(framework),
                Solver = SenseText(solver),
                Reason = framework == null ? $"solver 有 {solver} 目標式，但不是經 CreateMinimize / CreateMaximize 或匯入建立"
                    : solver == null ? $"框架建過 {framework} 目標式，solver 模型卻沒有：目標式被移出模型"
                    : $"方向不一致：框架記 {framework}、solver 是 {solver}",
            });
        }

        private static void LogModelStats(ModelStatsReport report)
        {
            var f = report.Framework;
            var s = report.Solver;
            Logging.Info($"[模型統計對帳] 來源={report.Source} 變數 記帳={f.Variables} 索引={report.IndexedVariables} solver={s.Variables}｜binary {f.Binary}/{s.Binary}｜integer {f.Integer}/{s.Integer}｜continuous {f.Continuous}/{s.Continuous}（記帳/solver）");
            Logging.Info($"[模型統計對帳] 限制式 記帳={f.Constraints} 索引={report.IndexedConstraints} solver={s.Constraints}｜特殊元素 {f.SpecialElements}/{s.SpecialElements}｜目標式 {SenseText(f.Objective)}/{SenseText(s.Objective)}（記帳/solver）");

            if (report.IsMatch)
            {
                Logging.Info("[MODEL_STATS_MATCH] 框架建模統計與 solver 模型一致 | items=Variables,Binary,Integer,Continuous,Constraints,SpecialElements,Objective result=verified");
                return;
            }
            foreach (var m in report.Mismatches)
                Logging.Warn($"[MODEL_STATS_MISMATCH] 框架建模統計與 solver 模型不一致 | item={m.Item} framework={m.Framework}{(m.Index == null ? "" : $" index={m.Index}")} solver={m.Solver} reason={m.Reason} result=continued");
        }

        // 未引用變數依名稱 head 分組計數（VariableB_Pick=2, Surplus_x=1），最多列 5 組
        private static string GroupSummary(IEnumerable<string> names)
        {
            var groups = names.GroupBy(VariableGroup).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
            string shown = string.Join(", ", groups.Take(5).Select(g => $"{g.Key}={g.Count()}"));
            return groups.Count > 5 ? $"{shown}, …共 {groups.Count} 組" : shown;
        }

        private static string Text(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string SenseText(ObjectiveSense? sense) => sense?.ToString() ?? "None";

        #endregion

        #region Pool — 狀態管理

        /// <summary>pool 目前是否有變數項（只看變數項，純常數不算）。用於送出前確認自己有沒有漏加項。</summary>
        public bool HasPool => _lhsTerms.Count > 0 || _rhsTerms.Count > 0;

        /// <summary>
        /// 丟棄 pool 內累積的所有項與常數。
        /// 正常流程不必自己呼叫——每個 Create* 送出後都會自動清空；只有中途要放棄一條組到一半的限制式才用。
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

        // 只吃 LHS 的出口（CreateRange / 目標式）遇到 RHS pool 有內容：照舊捨棄，但一定要留 warn，不能靜默吞掉
        private void WarnIfRhsPoolIgnored(string operation, string name, string reason)
        {
            if (_rhsTerms.Count == 0 && _rhsConst == 0) return;
            Logging.Warn($"[POOL_RHS_IGNORED] 右側 pool 不被採用 | operation={operation} name={name} rhsTerms={_rhsTerms.Count} rhsConst={_rhsConst} reason={reason} result=rhs_discarded");
        }

        private void LogDuplicateConstraint(string name)
            => Logging.Warn($"[CONSTRAINT_DUPLICATE] 略過重複限制式 | name={name} reason=duplicate_name result=kept_existing");

        // LHS − RHS 的 lazy 合併，不建立新 List，LinearExpr 單次迭代即可消費
        private IEnumerable<(double coef, TVar var)> CombinedLhsMinusRhs()
            => _lhsTerms.Concat(_rhsTerms.Select(t => (-t.coef, t.var)));

        #endregion

        #region Pool — AddLHS / AddRHS

        /// <summary>
        /// 往限制式左側累加一項 coeff·變數。連續呼叫即為求和；送出前一直留在 pool。
        /// </summary>
        /// <param name="coeff">係數。ALWAYS 先用 LINQ 取出存成區域變數再傳進來，不要內嵌整段查詢。</param>
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
        /// 有右側變數項時 ALWAYS 用這個，不要自己移項到左邊——保持與 Model.md 逐條對照的能力就是 pool 存在的理由。
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
        /// <param name="name">限制式名稱；同名只會建立第一條，之後的直接略過並寫 warn log。迴圈建立時 ALWAYS 把索引拼進名字（如 "Cap@TruckA"）。</param>
        /// <returns>true = pool 有內容（含被判定重複而略過的情況）；false = pool 是空的，什麼都沒建。</returns>
        /// <remarks>不論建立成功、重複略過或提早返回，pool 都會被清空——下一條限制式從乾淨狀態開始。</remarks>
        public bool CreateGreatEqual(string name)
            => CreateLinearConstraint(ValidateConstraintName(nameof(CreateGreatEqual), name), ConstraintSense.GreaterEqual);

        /// <summary>以 owner 類別名與維度值自動組名，送出「LHS ≥ RHS」。</summary>
        public bool CreateGreatEqual(ConstraintBase owner, params object[] dims)
            => CreateLinearConstraint(ComposeConstraintName(owner, dims), ConstraintSense.GreaterEqual);

        /// <summary>
        /// 送出「LHS ≥ rhs」，右側直接給常數。
        /// 注意這個 overload 是**覆蓋**右側常數（不是累加），先前 AddRHS(常數) 加的值會被 rhs 取代；右側變數項則仍會被算進去。
        /// </summary>
        /// <returns>false = 左側沒有任何變數項，限制式未建立。</returns>
        public bool CreateGreatEqual(double rhs, string name)
        {
            name = ValidateConstraintName(nameof(CreateGreatEqual), name);
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
        /// 送出形式與清空 pool 的行為同 <see cref="CreateGreatEqual(string)"/>，只有比較方向不同。
        /// </summary>
        /// <param name="name">限制式名稱；同名第二次起會被略過（warn log）。</param>
        /// <returns>true = pool 有內容；false = pool 是空的。</returns>
        public bool CreateLessEqual(string name)
            => CreateLinearConstraint(ValidateConstraintName(nameof(CreateLessEqual), name), ConstraintSense.LessEqual);

        /// <summary>以 owner 類別名與維度值自動組名，送出「LHS ≤ RHS」。</summary>
        public bool CreateLessEqual(ConstraintBase owner, params object[] dims)
            => CreateLinearConstraint(ComposeConstraintName(owner, dims), ConstraintSense.LessEqual);

        /// <summary>送出「LHS ≤ rhs」。rhs 覆蓋右側常數（語意同 <see cref="CreateGreatEqual(double, string)"/>）。</summary>
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
        /// 送出形式與清空 pool 的行為同 <see cref="CreateGreatEqual(string)"/>。
        /// </summary>
        /// <param name="name">限制式名稱；同名第二次起會被略過（warn log）。</param>
        /// <returns>true = pool 有內容；false = pool 是空的。</returns>
        public bool CreateEqual(string name)
            => CreateLinearConstraint(ValidateConstraintName(nameof(CreateEqual), name), ConstraintSense.Equal);

        /// <summary>以 owner 類別名與維度值自動組名，送出「LHS = RHS」。</summary>
        public bool CreateEqual(ConstraintBase owner, params object[] dims)
            => CreateLinearConstraint(ComposeConstraintName(owner, dims), ConstraintSense.Equal);

        /// <summary>送出「LHS = rhs」。rhs 覆蓋右側常數（語意同 <see cref="CreateGreatEqual(double, string)"/>）。</summary>
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
            ClearPool(); // 即使 skip，也必須清空 pool，否則下一條約束的 LHS 會累積舊項目
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
            _ledgerObjective = _objectiveSense;
            _buildSummaryDirty = true;
        }

        /// <summary>
        /// 清掉框架這一側追蹤的目標式（變數項、常數項、soft penalty 項）。
        /// solver 端已移除目標式時（如 OptEngine.ResetConstraint）MUST 一併呼叫，否則下一條軟性限制式會把舊目標項重新套回去。
        /// </summary>
        protected void ResetObjectiveTracking()
        {
            _objectiveTerms.Clear();
            _softPenaltyTerms.Clear();
            _objectiveConstant = 0;
            _ledgerObjective = null;
        }

        #endregion

        #region Pool — 軟性限制式

        /// <summary>pool 左側目前累積的項，供子類別自訂軟性限制式時取用（唯讀迭代，不要在迭代中改 pool）。</summary>
        protected IEnumerable<(double coef, TVar var)> PoolLhsTerms => _lhsTerms;

        /// <summary>pool 左側目前累積的常數，供子類別自訂軟性限制式時取用。</summary>
        protected double PoolLhsConst => _lhsConst;

        /// <summary>
        /// 是否支援軟性限制式。通用實作放在 EngineBase（pool 加彈性變數 + 目標式加 penalty），
        /// 只要 engine 提供 AddVariable / AddConstraint / SetObjective primitive 即可，預設 true。
        /// </summary>
        public virtual bool SupportsSoftConstraints => true;

        /// <summary>軟性 LHS &lt;= rhs：加 surplus 變數 dp≥0，建 lhs − dp &lt;= rhs，目標式 += penalty·dp。</summary>
        public virtual bool CreateLeSoft(double rhs, double penalty)
            => BuildSoft(rhs, penalty, ConstraintSense.LessEqual, null);

        /// <summary>具名軟性 LHS &lt;= rhs：名稱會用於限制式、彈性變數與自動 log。</summary>
        public virtual bool CreateLeSoft(double rhs, double penalty, string name)
            => BuildSoft(rhs, penalty, ConstraintSense.LessEqual,
                ValidateConstraintName(nameof(CreateLeSoft), name));

        /// <summary>以 owner 類別名與維度值自動組名，建立軟性 LHS ≤ rhs。</summary>
        public virtual bool CreateLeSoft(double rhs, double penalty, ConstraintBase owner, params object[] dims)
            => BuildSoft(rhs, penalty, ConstraintSense.LessEqual, ComposeConstraintName(owner, dims));

        /// <summary>軟性 LHS &gt;= rhs：加 deficit 變數 dn≥0，建 lhs + dn &gt;= rhs，目標式 += penalty·dn。</summary>
        public virtual bool CreateGeSoft(double rhs, double penalty)
            => BuildSoft(rhs, penalty, ConstraintSense.GreaterEqual, null);

        /// <summary>具名軟性 LHS &gt;= rhs：名稱會用於限制式、彈性變數與自動 log。</summary>
        public virtual bool CreateGeSoft(double rhs, double penalty, string name)
            => BuildSoft(rhs, penalty, ConstraintSense.GreaterEqual,
                ValidateConstraintName(nameof(CreateGeSoft), name));

        /// <summary>以 owner 類別名與維度值自動組名，建立軟性 LHS ≥ rhs。</summary>
        public virtual bool CreateGeSoft(double rhs, double penalty, ConstraintBase owner, params object[] dims)
            => BuildSoft(rhs, penalty, ConstraintSense.GreaterEqual, ComposeConstraintName(owner, dims));

        /// <summary>軟性 LHS == rhs：加 dn,dp≥0，建 lhs + dn − dp == rhs，目標式 += penalty·(dn+dp)。</summary>
        public virtual bool CreateEqSoft(double rhs, double penalty, string name)
            => BuildSoft(rhs, penalty, ConstraintSense.Equal,
                ValidateConstraintName(nameof(CreateEqSoft), name));

        /// <summary>以 owner 類別名與維度值自動組名，建立軟性 LHS = rhs。</summary>
        public virtual bool CreateEqSoft(double rhs, double penalty, ConstraintBase owner, params object[] dims)
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
                RecordVariableBuild("SoftConstraint", VarType.Continuous, expectedVariables, Variables.Count - variablesBefore);
                RecordConstraintBuild(name, true);
                MarkReferenced(terms);
                Logging.Info($"[軟性限制式建立完成] name={name} sense={sense} rhs={rhs} penalty={penalty} result=success");
            }
            catch (Exception ex)
            {
                RecordVariableBuild("SoftConstraint", VarType.Continuous, expectedVariables, Math.Max(0, Variables.Count - variablesBefore));
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
        /// 要求各 engine 的 SetObjective 為「覆寫」語意（Solver 原生即是；CPLEX 在 SetObjective 內先移除舊目標式達成）。
        /// </summary>
        protected virtual void AddObjectiveTerm(double coef, TVar variable)
        {
            _softPenaltyTerms.Add((coef, variable));
            ApplyObjective();
        }

        #endregion
    }
}
