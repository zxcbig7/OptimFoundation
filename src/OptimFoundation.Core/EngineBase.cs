using System;
using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Internal;

namespace OptimFoundation.Core
{
    #region Interfaces and Enums
    /// <summary>
    /// 求解器共用設定，由各 adapter 轉為原生參數；null 使用求解器預設值。
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
    }


    /// <summary>求解引擎的共用操作：建模、求解、讀解與取得紀錄。</summary>
    public interface ISolverEngine : IDisposable
    {
        /// <summary>本引擎使用的求解設定。</summary>
        ISolverConfig SolverConfig { get; }

        /// <summary>求解狀態；未求解為 NotSolved。</summary>
        SolveStatus Status { get; }

        /// <summary>最近一次 Solve() 記錄的求解狀態、耗時與結果；尚未求解為 null。</summary>
        SolveMetrics LastMetrics { get; }

        /// <summary>目前模型類型（LP、MILP、IP 或 BP），建模後即可讀取。</summary>
        ModelType ModelType { get; }

        /// <summary>建立求解器模型並套用設定；建立變數或限制式前必須先呼叫。</summary>
        void Build();

        /// <summary>求解。回傳 true 代表取得 Optimal 或 Feasible 解。</summary>
        bool Solve();

        /// <summary>取得目標式的解值；必須在求解成功後呼叫。</summary>
        double GetObjectiveValue();

        /// <summary>依變數全名取得解值（TypeName@s1@s2@…）；必須在求解成功後呼叫。</summary>
        double GetVariableValue(string name);

        /// <summary>取得解值；未指定型別時回傳全部變數。</summary>
        IReadOnlyDictionary<string, double> GetSolution(string varTypeName = null);

        /// <summary>設定 MIP start，可直接使用另一個 engine 的解；請在建模後、Solve 前呼叫。</summary>
        /// <returns>實際套用的變數數；未套用時為 0。</returns>
        int AddMIPStart(IReadOnlyDictionary<string, double> values, string name = null);
    }

    /// <summary>支援特殊限制式的 solver 才會實作此介面。</summary>
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

    /// <summary>求解引擎基底類別；子類別負責實際呼叫 solver。</summary>
    public abstract class EngineBase<TModel, TVar, TExpr, TConstr> : ISolverEngine, ITrajectorySource
    {
        /// <summary>各 solver 的模型物件（CPLEX->Cplex …）；LoadConfig() 建立、Dispose() 釋放。</summary>
        protected TModel Model;

        /// <summary>
        /// 以變數全名索引原生變數，包含軟性限制式的彈性變數。
        /// </summary>
        protected readonly Dictionary<string, TVar> Variables = new Dictionary<string, TVar>();

        /// <summary>Variables 字典中的變數總數，包含軟性限制式自動加入的彈性變數。</summary>
        public int VariableCount => Variables.Count;

        /// <summary>依 solver 模型重新判定問題類型，因此匯入模型也適用；加入軟性限制式後，IP/BP 會成為 MILP。</summary>
        public ModelType ModelType => ResolveModelType(ReadModelComposition());

        /// <summary>建構時傳入的求解器設定。</summary>
        public ISolverConfig SolverConfig { get; protected set; }

        /// <summary>求解狀態；未求解前為 NotSolved。</summary>
        public SolveStatus Status { get; protected set; } = SolveStatus.NotSolved;

        /// <summary>求得的最佳目標值；未求解前為 0。</summary>
        public double BestObjValue { get; protected set; }

        /// <summary>求解結束時的 MIP gap；LP 問題為 0。</summary>
        public double MIPGap { get; protected set; }

        /// <summary>最近一次求解的狀態、耗時與結果。</summary>
        public SolveMetrics LastMetrics { get; protected set; }

        /// <summary>已建立的限制式數量；預設為 0，由需要提供此數量的引擎覆寫。</summary>
        public virtual int ConstraintCount => 0;

        // Expected 是預期建立數，Actual 是成功建立數。
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

        // 追蹤引用以列出 CPLEX 未收進模型的變數名稱。
        private readonly HashSet<TVar> _referencedVariables = new HashSet<TVar>();

        /// <summary>各變數型別的預期與實際建立數；每次讀取都回傳複本。</summary>
        public IReadOnlyDictionary<string, (int Expected, int Actual)> VariableBuildCounts
            => _variableBuildCounts.ToDictionary(kv => kv.Key, kv => (kv.Value.Expected, kv.Value.Actual));

        /// <summary>各限制式群組的預期與實際建立數；每次讀取都回傳複本。</summary>
        public IReadOnlyDictionary<string, (int Expected, int Actual)> ConstraintBuildCounts
            => _constraintBuildCounts.ToDictionary(kv => kv.Key, kv => (kv.Value.Expected, kv.Value.Actual));

        /// <summary>依序嘗試統計方法；皆不可用時回傳 null。</summary>
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
                    Logging.Warn($"[求解遙測取得失敗] 位置={name} 型別={target.GetType().FullName} 原因={ex.GetBaseException().Message} 結果=回傳空值");
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

        // 用完整限制式名稱去重，避免把不同維度的限制式當成重複。
        private readonly HashSet<string> _verifyConstraints = new HashSet<string>();

        // 保留目標項，供新增軟性罰分時重設完整目標式。
        private readonly List<(double coef, TVar var)> _objectiveTerms = new List<(double, TVar)>();
        private readonly List<(double coef, TVar var)> _softPenaltyTerms = new List<(double, TVar)>();
        private double _objectiveConstant = 0;
        private ObjectiveSense _objectiveSense = ObjectiveSense.Minimize;
        private int _softCount = 0;


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
        /// <param name="config">求解器組態。</param>
        protected EngineBase(ISolverConfig config)
        {
            SolverConfig = config;
        }

        #region Solver Contract

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
        /// 設定 expr + constant；必須取代舊目標式，避免累加軟性罰分時重複建目標。
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
        /// 從求解器模型讀取組成供 ModelType 判定；不可只計 Variables，以支援匯入模型。尚未建模時回傳零值。
        /// </summary>
        /// <returns>
        /// 各型別變數數與離散結構旗標；旗標須涵蓋 semi-continuous、SOS。
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
        /// 記錄建模摘要與警告後呼叫 SolveCore；未引用變數不阻擋求解。
        /// </summary>
        public bool Solve()
        {
            try
            {
                LogBuildSummary();
                WarnUnreferencedVariables();
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
                "引擎操作失敗",
                null,
                operation,
                GetType().FullName,
                exception.GetBaseException().Message);
        }

        /// <summary>各引擎實作模型初始化的方法；使用者應呼叫 <see cref="Build"/>，由它清空統計後再呼叫此方法。</summary>
        protected abstract void BuildCore();

        /// <summary>各 engine 的求解實作；由 <see cref="Solve"/> 呼叫。回傳 true 代表取得 Optimal 或 Feasible 解。</summary>
        protected abstract bool SolveCore();

        /// <summary>取得目標式解值。必須在 Solve() 回傳 true 後呼叫，否則求解器可能拋出例外。</summary>
        public abstract double GetObjectiveValue();

        /// <summary>依變數全名取得解值（格式 TypeName@s1@s2@…），必須在求解成功後呼叫。</summary>
        public abstract double GetVariableValue(string name);

        /// <summary>釋放 solver 原生資源（CPLEX 的 native handle）。用 using 包住 engine，或交由 OptProject / OptExecution 管理。</summary>
        public abstract void Dispose();

        #endregion

        #region VariableManager — 批次建立變數

        /// <summary>
        /// 批次建立並登記變數；預設逐筆呼叫 AddVariable，可覆寫為 solver 批次 API。
        /// </summary>
        protected virtual void AddVariables(IReadOnlyList<string> names, double lb, double ub, VarType type)
        {
            foreach (var name in names)
            {
                AddVariable(name, lb, ub, type);
            }
        }

        private void BatchBuild(string setName, double lb, double ub, VarType type, object[] sets)
            => BatchBuild(setName, () => VariableManager.ComposeNames(setName, sets), lb, ub, type);

        private void BatchBuild(string setName, Func<IEnumerable<string>> nameFactory, double lb, double ub, VarType type)
        {
            int before = Variables.Count;
            List<string> names = null;
            string stage = "名稱建立階段";
            try
            {
                names = nameFactory().ToList();

                // 同名變數沿用既有項目，避免 solver 多建但字典只留下最後一個。
                var newNames = SkipDuplicateVariableNames(setName, names);

                stage = "CPLEX 變數建立階段";
                if (newNames.Count > 0)
                    AddVariables(newNames, lb, ub, type);

                int actual = Variables.Count - before;
                RecordVariableBuild(setName, names.Count, actual);
                Logging.Info($"[變數建立完成] 變數類別={setName} 數量={actual}/{names.Count}");
            }
            catch (Exception ex)
            {
                int actual = Math.Max(0, Variables.Count - before);
                string expected = names == null ? "未知" : names.Count.ToString();
                if (names != null)
                    RecordVariableBuild(setName, names.Count, actual);
                Logging.ErrorOnce(
                    ex,
                    "變數建立失敗",
                    null,
                    "BatchBuild",
                    stage,
                    ex.GetBaseException().Message,
                    $"變數類別={setName} 變數型別={type} 範圍=[{lb},{ub}] 數量={actual}/{expected}");
                throw;
            }
        }

        private List<string> SkipDuplicateVariableNames(string setName, List<string> names)
        {
            var newNames = new List<string>(names.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new List<string>();
            foreach (var name in names)
            {
                if (Variables.ContainsKey(name) || !seen.Add(name))
                    duplicates.Add(name);
                else
                    newNames.Add(name);
            }

            if (duplicates.Count > 0)
                Logging.Warn($"[變數重複] 變數類別={setName} 數量={duplicates.Count} 範例={string.Join(",", duplicates.Take(5))} 原因=名稱已存在 結果=保留原值");
            return newNames;
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
                $"變數前綴解析器回傳未知的 VarType 成員名稱：{typeName}");
            throw Logging.ErrorOnce(
                exception,
                "變數型別解析失敗",
                null,
                nameof(TryResolveVariableType),
                typeName,
                "未知的變數型別");
        }

        private static void ValidateExplicitVariableType<TVariable>(VarType requestedType, string operation)
        {
            string className = typeof(TVariable).Name;

            // 明確 builder 入口：完全沒有正式前綴的類別可由呼叫方法決定型別。
            if (!TryResolveVariableType(className, out var declaredType) || declaredType == requestedType)
                return;

            string message = $"{operation}<{className}> 要建立 {requestedType} 變數，但類別名前綴宣告為 {declaredType}；" +
                $"命名規則：{VariablePrefixNaming.NamingGuide}";
            throw Logging.ErrorOnce(
                new ArgumentException(message),
                "變數型別不一致",
                "變數名稱前綴與建立方法指定的型別不一致",
                operation,
                className,
                "宣告型別與指定型別不一致",
                $"變數類別={className} 宣告型別={declaredType} 指定型別={requestedType}");
        }


        /// <summary>
        /// 批次建立連續變數，界限 [0, <see cref="OptBounds.Infinity"/>]（1E20 = CPLEX 的無上限）。
        /// 先依位置逐維比對 sets 與 TVariable 維度 property 的數量與型別（不符拋例外），再以類別名轉呼叫 string 版。
        /// </summary>
        /// <typeparam name="TVariable">變數類別；property 宣告順序必須與 sets 順序一致，否則 AddLHS 組出的名稱會查不到變數。</typeparam>
        /// <param name="sets">各維度的集合；框架取各集合的所有組合（笛卡兒積）產生變數名稱。</param>
        public virtual void BuildCVs<TVariable>(params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Continuous, nameof(BuildCVs));
            VariableManager.ValidateVariableDimensions<TVariable>(sets);
            BuildCVs(typeof(TVariable).Name, sets);
        }

        /// <summary>批次建立連續變數並指定界限 [lb, ub]。維度檢查同 <see cref="BuildCVs{TVariable}(object[])"/>。</summary>
        public virtual void BuildCVs<TVariable>(double lb, double ub, params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Continuous, nameof(BuildCVs));
            VariableManager.ValidateVariableDimensions<TVariable>(sets);
            BuildCVs(typeof(TVariable).Name, lb, ub, sets);
        }

        /// <summary>批次建立整數變數，界限 [0, <see cref="OptBounds.Infinity"/>]。維度順序與檢查同 <see cref="BuildCVs{TVariable}(object[])"/>。</summary>
        public virtual void BuildIVs<TVariable>(params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Integer, nameof(BuildIVs));
            VariableManager.ValidateVariableDimensions<TVariable>(sets);
            BuildIVs(typeof(TVariable).Name, sets);
        }

        /// <summary>批次建立整數變數並指定界限 [lb, ub]。維度檢查同 <see cref="BuildCVs{TVariable}(object[])"/>。</summary>
        public virtual void BuildIVs<TVariable>(double lb, double ub, params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Integer, nameof(BuildIVs));
            VariableManager.ValidateVariableDimensions<TVariable>(sets);
            BuildIVs(typeof(TVariable).Name, lb, ub, sets);
        }

        /// <summary>批次建立 0/1 二元變數。維度順序與檢查同 <see cref="BuildCVs{TVariable}(object[])"/>。</summary>
        public virtual void BuildBVs<TVariable>(params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Binary, nameof(BuildBVs));
            VariableManager.ValidateVariableDimensions<TVariable>(sets);
            BuildBVs(typeof(TVariable).Name, sets);
        }



        // string overload 只驗證 setName，不檢查類別前綴與維度數。

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

        /// <summary>
        /// 依類別名前綴決定變數型別：VariableB_（Binary，界限 [0,1]）、
        /// VariableC_（Continuous）/ VariableI_（Integer）。
        /// 先依位置逐維比對 sets 與 TVariable 維度 property 的數量與型別（型別必須完全相同，int 與 long 視為不同），
        /// 再以類別名當變數名稱開頭，交給 string 版 <see cref="BuildVars(string, VarType, object[])"/> 建立。
        /// 需要自訂上下界，或類別名未使用這些前綴時，可用 BuildCVs / BuildIVs / BuildBVs 指定型別。
        /// </summary>
        /// <exception cref="ArgumentException">前綴無法判定型別，或 sets 的維度數量、型別與 TVariable 不一致。</exception>
        public virtual void BuildVars<TVariable>(params object[] sets)
        {
            string name = typeof(TVariable).Name;
            if (!TryResolveVariableType(name, out var type))
            {
                string msg = $"BuildVars<{name}> 無法從類別名前綴判定變數型別；" +
                    $"命名天條：{VariablePrefixNaming.NamingGuide}，例：VariableC_Start；" +
                    "不依天條命名請改用 BuildCVs / BuildIVs / BuildBVs";
                throw Logging.ErrorOnce(
                    new ArgumentException(msg),
                    "變數型別不合法",
                    "無法從名稱前綴判定變數型別",
                    nameof(BuildVars),
                    name,
                    "前綴不合法",
                    $"變數類別={name}");
            }

            VariableManager.ValidateVariableDimensions<TVariable>(sets);
            BuildVars(name, type, sets);
        }

        #endregion

        #region VariableManager — 查詢

        /// <summary>
        /// 以實例 ToString() 產生的全名查詢原生變數。
        /// </summary>
        /// <param name="searchData">填好各維度值的變數類別實例，例：new VariableB_Assign { EMP = "E1", DATE = "D1" }。</param>
        /// <exception cref="KeyNotFoundException">該型別未建立，或這組索引值不在建立範圍內。</exception>
        protected TVar ReadVar(object searchData)
            => ReadVar(searchData.ToString());

        /// <summary>
        /// 以變數全名查詢原生變數，不需建立變數實例。
        /// </summary>
        /// <param name="varName">變數全名，例：VariableB_Assign@E1@D1。import 進來、不符框架命名慣例的名稱同樣可查。</param>
        /// <exception cref="KeyNotFoundException">這個名稱的變數不存在。</exception>
        protected TVar ReadVar(string varName)
        {
            if (varName != null && Variables.TryGetValue(varName, out var v))
                return v;

            throw Logging.ErrorOnce(
                new KeyNotFoundException($"找不到變數：{varName}"),
                "變數找不到",
                null,
                nameof(ReadVar),
                varName,
                "變數尚未建立");
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
                Logging.ErrorOnce(ex, "取得解失敗", null, nameof(GetSetVarValues), setName,
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
                Logging.ErrorOnce(ex, "取得解失敗", null, nameof(GetSolution), varTypeName,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        #endregion

        #region MIP Start

        /// <summary>
        /// 以「變數全名 → 值」提供 MIP start，名稱同 GetSolution；可只給部分值，由 solver 依 effort 設定補齊。
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
            string label = name ?? "<自動>";
            if (values == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(values), "values 不得為 null"),
                    "起始解不合法", null, nameof(AddMIPStart), label, "值為空");

            try
            {
                if (ModelType == ModelType.LP)
                {
                    Logging.Warn($"[起始解略過] 名稱={label} 數量={values.Count} 原因=線性規劃模型 結果=略過");
                    return 0;
                }

                var entries = new List<(TVar var, double value)>(values.Count);
                var unknown = new List<string>();
                foreach (var kv in values)
                {
                    if (kv.Key != null && Variables.TryGetValue(kv.Key, out var v))
                        entries.Add((v, kv.Value));
                    else
                        unknown.Add(kv.Key ?? "<空值>");
                }

                if (unknown.Count > 0)
                    Logging.Warn($"[起始解變數找不到] 名稱={label} 數量={values.Count} 找不到變數數量={unknown.Count} 範例={string.Join(",", unknown.Take(5))} 原因=變數不在模型內 結果=略過");

                if (entries.Count == 0)
                {
                    Logging.Warn($"[起始解略過] 名稱={label} 數量={values.Count} 原因=沒有符合的變數 結果=略過");
                    return 0;
                }

                AddMIPStartCore(entries, name);
                Logging.Info($"[起始解套用完成] 名稱={label} 數量={entries.Count}/{values.Count} 模型內變數數量={VariableCount}");
                return entries.Count;
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "起始解套用失敗", null, nameof(AddMIPStart), label,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        #endregion

        #region Variable Bounds — 設定變數界限

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
        /// 匯入並建立索引後清除舊統計、同步目標方向，避免軟性罰分符號錯誤。
        /// </summary>
        /// <param name="objective">檔案裡的目標式方向；null = 沒有目標式。</param>
        protected void RecordImportedModel(ObjectiveSense? objective)
        {
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
            if (string.IsNullOrWhiteSpace(name)) return "<未命名>";
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
            if (string.IsNullOrWhiteSpace(name)) return "<未命名>";
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

            // 分別列出建立計數與目前模型數量，揭露未登記或重設造成的差異。
            int expectedVariables = _variableBuildCounts.Values.Sum(x => x.Expected);
            int actualVariables = _variableBuildCounts.Values.Sum(x => x.Actual);
            Logging.Info($"[變數建立摘要] 數量={actualVariables}/{expectedVariables} 變數類別數量={_variableBuildCounts.Count} 模型內變數數量={VariableCount}");

            foreach (var entry in _constraintBuildCounts.OrderBy(x => x.Key, StringComparer.Ordinal))
                Logging.Info($"[限制式建立完成] 限制式類別={entry.Key} 數量={entry.Value.Actual}/{entry.Value.Expected}");
            int expectedConstraints = _constraintBuildCounts.Values.Sum(x => x.Expected);
            int actualConstraints = _constraintBuildCounts.Values.Sum(x => x.Actual);
            Logging.Info($"[限制式建立摘要] 數量={actualConstraints}/{expectedConstraints} 限制式類別數量={_constraintBuildCounts.Count} 模型內限制式數量={ConstraintCount}");

            var composition = ReadModelComposition();
            Logging.Info($"[模型類型摘要] 模型類型={ResolveModelType(composition)} 連續變數數量={composition.Continuous} 整數變數數量={composition.Integer} 二元變數數量={composition.Binary}");

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

        // CPLEX 收進的變數較少時才列未引用名單；直接呼叫 AddLE 等入口不會更新 pool 引用紀錄。
        private void WarnUnreferencedVariables()
        {
            var composition = ReadModelComposition();
            if (VariableCount <= composition.Continuous + composition.Integer + composition.Binary) return;

            var unreferenced = Variables.Where(kv => !_referencedVariables.Contains(kv.Value)).Select(kv => kv.Key).ToList();
            if (unreferenced.Count == 0) return;
            Logging.Warn($"[變數未引用] 已宣告的變數沒被任何限制式或目標式引用，CPLEX 不會收進模型 | 數量={unreferenced.Count} 變數類別={GroupSummary(unreferenced)} 範例={string.Join(",", unreferenced.Take(5))} 結果=繼續");
        }

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
        /// 清空 pool 的項與常數；Create* 建立成功、同名略過或沒有變數項而略過時都會自動清空，只有建立時拋出例外後須自行呼叫。
        /// </summary>
        public void ClearPool()
        {
            _lhsTerms.Clear();
            _rhsTerms.Clear();
            _lhsConst = 0;
            _rhsConst = 0;
        }

        // 沒有可用的變數項：記警告（列出被丟掉的常數）、計入建立統計後清空 pool，避免常數殘留到下一條限制式。
        private bool SkipEmptyConstraint(string name, string reason)
        {
            Logging.Warn($"[限制式為空] 名稱={name ?? "<未命名>"} 常數={_lhsConst} 右側項數量={_rhsTerms.Count} 右側常數={_rhsConst} 原因={reason} 結果=略過");
            RecordConstraintBuild(name, false);
            ClearPool();
            return false;
        }

        // CreateRange 與建立目標式只採用左側；右側有暫存項目時記錄警告，提醒呼叫端這些項目不會被使用。
        private void WarnIfRhsPoolIgnored(string operation, string name, string reason)
        {
            if (_rhsTerms.Count == 0 && _rhsConst == 0) return;
            Logging.Warn($"[右側暫存區略過] 位置={operation} 名稱={name} 右側項數量={_rhsTerms.Count} 右側常數={_rhsConst} 原因={reason} 結果=略過");
        }

        private void LogDuplicateConstraint(string name)
            => Logging.Warn($"[限制式重複] 名稱={name} 原因=名稱重複 結果=保留原值");

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
                Logging.Warn("[變數為空] 位置=AddLHS 原因=傳入的變數為空 結果=略過");
                return false;
            }
            string key = varSpec.ToString();
            if (!Variables.TryGetValue(key, out var v))
            {
                throw Logging.ErrorOnce(
                    new KeyNotFoundException($"AddLHS 找不到變數：{key}（變數類別：{varSpec.GetType().Name}），請確認屬性宣告順序與 Build*Vs 傳入集合順序一致"),
                    "變數找不到",
                    null,
                    nameof(AddLHS),
                    key,
                    "變數尚未建立",
                    $"變數類別={varSpec.GetType().Name}");
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
        /// 累加右側 coeff·變數；建立限制式時由框架移項。
        /// </summary>
        /// <returns>true = 已加入；false = varSpec 為 null（略過該項）。</returns>
        /// <exception cref="KeyNotFoundException">變數名查不到（同 <see cref="AddLHS(double, object)"/>）。</exception>
        public bool AddRHS(double coeff, object varSpec)
        {
            if (varSpec == null)
            {
                Logging.Warn("[變數為空] 位置=AddRHS 原因=傳入的變數為空 結果=略過");
                return false;
            }
            string key = varSpec.ToString();
            if (!Variables.TryGetValue(key, out var v))
            {
                throw Logging.ErrorOnce(
                    new KeyNotFoundException($"AddRHS 找不到變數：{key}（變數類別：{varSpec.GetType().Name}），請確認屬性宣告順序與 Build*Vs 傳入集合順序一致"),
                    "變數找不到",
                    null,
                    nameof(AddRHS),
                    key,
                    "變數尚未建立",
                    $"變數類別={varSpec.GetType().Name}");
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
                    new ArgumentNullException(nameof(owner), "建立限制式名稱時 owner 不得為 null"),
                    "限制式名稱不合法",
                    null,
                    nameof(ComposeConstraintName),
                    null,
                    "所屬物件為空");
            }

            return ModelNaming.Compose(owner.GetType().Name, dims);
        }

        private static string ValidateConstraintName(string operation, string name)
            => ModelNaming.ValidateComposedName(operation, name);

        /// <summary>
        /// 建立 LHS ≥ RHS；框架自動移項為 (LHS項 − RHS項) ≥ (RHS常數 − LHS常數)。
        /// </summary>
        /// <param name="name">限制式名稱；同名只建立第一條，之後略過並記錄警告。迴圈建立時須在名稱包含維度值，例如 "Cap@TruckA"。</param>
        /// <returns>true = pool 有內容（含被判定重複而略過的情況）；false = pool 是空的，什麼都沒建。</returns>
        /// <remarks>建立成功、因同名略過，或沒有變數項而略過時都會清空 pool（含常數）；只有建立時拋出例外，pool 才會保留。</remarks>
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
                return SkipEmptyConstraint(name, "左式沒有任何項");
            _rhsConst = rhs;
            return CreateLinearConstraint(name, ConstraintSense.GreaterEqual);
        }

        /// <summary>
        /// 建立 LHS ≤ RHS；移項與清空 pool 同 <see cref="CreateGreaterEqual(string)"/>。
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
                return SkipEmptyConstraint(name, "左式沒有任何項");
            _rhsConst = rhs;
            return CreateLinearConstraint(name, ConstraintSense.LessEqual);
        }

        /// <summary>
        /// 建立 LHS = RHS；移項與清空 pool 同 <see cref="CreateGreaterEqual(string)"/>。
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
                return SkipEmptyConstraint(name, "左式沒有任何項");
            _rhsConst = rhs;
            return CreateLinearConstraint(name, ConstraintSense.Equal);
        }

        /// <summary>
        /// 送出範圍限制式 lb ≤ LHS ≤ ub（只用 AddLHS 累積的左側；LHS 常數移到界上抵銷）。
        /// RHS pool（AddRHS 的變數項與常數）不屬於範圍限制式：有內容時寫 <c>[右側暫存區略過]</c> warn 後捨棄。
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
            if (!HasPool)
                return SkipEmptyConstraint(name, "暫存區為空");

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
                    "限制式建立失敗",
                    null,
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
                return SkipEmptyConstraint(name, "左式沒有任何項");
            WarnIfRhsPoolIgnored(nameof(CreateRange), name, "範圍限制式只採用左側");

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
                    "限制式建立失敗",
                    "範圍限制式",
                    nameof(CreateRange),
                    name,
                    ex.GetBaseException().Message,
                    $"範圍=[{lb},{ub}]");
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
            Logging.Info($"[目標式建立開始] 方向={sense} 項數量={expectedTerms} 常數={_lhsConst}");
            if (_lhsTerms.Count == 0 && _softPenaltyTerms.Count == 0)
            {
                Logging.Warn($"[目標式為空] 方向={sense} 項數量=0 常數={_lhsConst} 原因=沒有任何項 結果=略過");
                ClearPool();
                return;
            }
            WarnIfRhsPoolIgnored($"Create{sense}", "<目標式>", "目標式只採用左側");
            try
            {
                _objectiveTerms.Clear();
                _objectiveTerms.AddRange(_lhsTerms);
                _objectiveConstant = _lhsConst;
                _objectiveSense = sense;
                ApplyObjective();
                ClearPool();
                Logging.Info($"[目標式建立完成] 方向={sense} 項數量={expectedTerms} 常數={_objectiveConstant}");
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(
                    ex,
                    "目標式建立失敗",
                    null,
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
        /// 清除追蹤的目標項、常數與罰分；solver 移除目標式時須同步呼叫，避免後續加回舊項。
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
        /// 是否支援軟性限制式，預設 true；引擎須實作 AddVariable、AddConstraint、SetObjective。
        /// </summary>
        public virtual bool SupportsSoftConstraints => true;

        /// <summary>允許 LHS 超過 rhs：加入超出量 dp≥0，建立 lhs − dp &lt;= rhs；罰分為 penalty·dp，最小化時加到目標式，最大化時扣除。</summary>
        public virtual bool CreateLessEqualSoft(double rhs, double penalty)
            => BuildSoft(rhs, penalty, ConstraintSense.LessEqual, null);

        /// <summary>具名軟性 LHS &lt;= rhs：名稱會用於限制式、彈性變數與自動 log；與任何限制式同名時第二次起略過（warn log）。</summary>
        public virtual bool CreateLessEqualSoft(double rhs, double penalty, string name)
            => BuildSoft(rhs, penalty, ConstraintSense.LessEqual,
                ValidateConstraintName(nameof(CreateLessEqualSoft), name));

        /// <summary>以 owner 類別名與維度值自動組名，建立軟性 LHS ≤ rhs。</summary>
        public virtual bool CreateLessEqualSoft(double rhs, double penalty, ConstraintBase owner, params object[] dims)
            => BuildSoft(rhs, penalty, ConstraintSense.LessEqual, ComposeConstraintName(owner, dims));

        /// <summary>允許 LHS 不足 rhs：加入不足量 dn≥0，建立 lhs + dn &gt;= rhs；罰分為 penalty·dn，最小化時加到目標式，最大化時扣除。</summary>
        public virtual bool CreateGreaterEqualSoft(double rhs, double penalty)
            => BuildSoft(rhs, penalty, ConstraintSense.GreaterEqual, null);

        /// <summary>具名軟性 LHS &gt;= rhs：名稱會用於限制式、彈性變數與自動 log；與任何限制式同名時第二次起略過（warn log）。</summary>
        public virtual bool CreateGreaterEqualSoft(double rhs, double penalty, string name)
            => BuildSoft(rhs, penalty, ConstraintSense.GreaterEqual,
                ValidateConstraintName(nameof(CreateGreaterEqualSoft), name));

        /// <summary>以 owner 類別名與維度值自動組名，建立軟性 LHS ≥ rhs。</summary>
        public virtual bool CreateGreaterEqualSoft(double rhs, double penalty, ConstraintBase owner, params object[] dims)
            => BuildSoft(rhs, penalty, ConstraintSense.GreaterEqual, ComposeConstraintName(owner, dims));

        /// <summary>允許 LHS 偏離 rhs：加入不足量 dn 與超出量 dp（皆≥0），建立 lhs + dn − dp == rhs；最小化加上 penalty·(dn+dp)，最大化扣除。同名第二次起略過（warn log）。</summary>
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
                string skippedName = name ?? "<自動軟性限制式>";
                Logging.Warn($"[限制式為空] 軟性限制式 | 名稱={skippedName} 方向={sense} 常數={_lhsConst} 右側常數={_rhsConst} 原因=暫存區為空 結果=略過");
                RecordConstraintBuild(skippedName, false);
                ClearPool();
                return false;
            }
            if (name == null)
                name = ModelNaming.ValidateComposedName("自動軟性限制式", $"Soft_{sense}_{++_softCount}");

            // 與一般限制式共用名稱去重，避免彈性變數撞名。
            if (_verifyConstraints.Contains(name))
            {
                LogDuplicateConstraint(name);
                RecordConstraintBuild(name, false);
                ClearPool();
                return true;
            }

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
                _verifyConstraints.Add(name);
                RecordVariableBuild("SoftConstraint", expectedVariables, Variables.Count - variablesBefore);
                RecordConstraintBuild(name, true);
                MarkReferenced(terms);
                Logging.Info($"[軟性限制式建立完成] 名稱={name} 方向={sense} 右側值={rhs} 懲罰={penalty}");
            }
            catch (Exception ex)
            {
                RecordVariableBuild("SoftConstraint", expectedVariables, Math.Max(0, Variables.Count - variablesBefore));
                RecordConstraintBuild(name, false);
                Logging.ErrorOnce(
                    ex,
                    "軟性限制式建立失敗",
                    null,
                    nameof(BuildSoft),
                    name,
                    ex.GetBaseException().Message,
                    $"限制式類別={ConstraintGroup(name)}");
                throw;
            }
            ClearPool();
            return true;
        }

        /// <summary>
        /// 累積 coef·var 罰分後以 SetObjective 取代完整目標式。
        /// </summary>
        protected virtual void AddObjectiveTerm(double coef, TVar variable)
        {
            _softPenaltyTerms.Add((coef, variable));
            ApplyObjective();
        }

        #endregion
    }
}
