using System;
using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Internal;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 所有 solver 引擎的泛型基底：把「建模」與「呼叫 solver」分離。
    /// 泛型參數 TModel/TVar/TExpr/TConstr 是各 solver 的原生型別；子類別只需實作 Solver Contract 那組 abstract。
    /// 提供三大共用機制：① 變數管理（Build*Vs 批次建立 + VariableSets/Variables 索引）；
    /// ② Pool API（AddLHS/AddRHS 累積左右兩側 → Create* 送出，框架自動算 LHS−RHS，免手動移項）；
    /// ③ 軟性限制式（penalty 法通用實作）。
    /// </summary>
    public abstract class EngineBase<TModel, TVar, TExpr, TConstr> : ISolverEngine, ITrajectorySource
    {
        /// <summary>各 solver 的模型物件（CPLEX->Cplex / Gurobi->GRBModel …）；Configuration() 建立、Dispose() 釋放。</summary>
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
            /// <summary>預期建立數（變數名笛卡兒積數量 / 嘗試送出的限制式條數）。</summary>
            public int Expected;

            /// <summary>實際建立數；重複名稱被略過或建立失敗都不計入。</summary>
            public int Actual;
        }

        private readonly Dictionary<string, BuildCount> _variableBuildCounts = new Dictionary<string, BuildCount>();
        private readonly Dictionary<string, BuildCount> _constraintBuildCounts = new Dictionary<string, BuildCount>();
        private bool _buildSummaryDirty = true;

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
        // 各 engine 目標式語意不同（CPLEX 新增 / Gurobi·Solver 覆寫），統一由 AddObjectiveTerm 處理。
        private readonly List<(double coef, TVar var)> _objectiveTerms = new List<(double, TVar)>();
        private readonly List<(double coef, TVar var)> _softPenaltyTerms = new List<(double, TVar)>();
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
        //   SetObjective   — 設定目標式方向（Minimize / Maximize）
        //   SetVariableBounds — 直接修改已建立變數的 LB / UB
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
        /// 設定目標式。實作 MUST 為「覆寫」語意——重複呼叫要換掉舊目標式而非疊加，
        /// 否則軟性限制式的 penalty 累加（每加一項就重設一次目標式）會產生多個目標式。
        /// </summary>
        protected abstract void SetObjective(TExpr expr, ObjectiveSense sense);

        /// <summary>直接改已建立變數的界限；傳 null 表示該側不動。</summary>
        protected abstract void SetVariableBounds(TVar variable, double? lb, double? ub);

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

        /// <summary>求解入口：先跑 PreSolveGuard()（scale guard），再呼叫 SolveCore()（各 engine 實作）。</summary>
        public bool Solve()
        {
            try
            {
                LogBuildSummary();
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

        /// <summary>各 engine 的建模實作；由 <see cref="Build"/> 這個 template method 呼叫，消費端不直接叫。</summary>
        protected abstract void BuildCore();

        /// <summary>各 engine 的求解實作；由 <see cref="Solve"/> 呼叫。回傳 true 代表取得 Optimal 或 Feasible 解。</summary>
        protected abstract bool SolveCore();

        // RegisteredVariableCount > Config.ScaleWarnThreshold → Logging.Warn（只警告不阻擋，大但合法的模型不該被擋）。
        // Config 為 null 時防禦性跳過（不炸）。
        private void PreSolveGuard()
        {
            if (Config == null) return;
            if (RegisteredVariableCount > Config.ScaleWarnThreshold)
                Logging.Warn($"[MODEL_SCALE_WARNING] 變數規模超過警告門檻 | count={RegisteredVariableCount} threshold={Config.ScaleWarnThreshold} result=continued");
        }

        /// <summary>取目標式的解值。MUST 在 Solve() 回傳 true 之後呼叫，否則各 solver 會丟自己的例外。</summary>
        public abstract double GetObjectiveValue();

        /// <summary>依變數全名取解值（格式 TypeName@s1@s2@…）。MUST 在求解成功後呼叫。</summary>
        public abstract double GetVariableValue(string name);

        /// <summary>釋放 solver 原生資源（CPLEX / Gurobi 的 native handle）。用 using 包住 engine，或交由 OptProject / OptExperiment 管理。</summary>
        public abstract void Dispose();

        #endregion

        #region VariableManager — 批次建立變數

        /// <summary>
        /// 批次建立變數並寫入 Variables dict。
        /// Default：逐筆呼叫 AddVariable。
        /// Solver override 此方法可使用原生 array API（CPLEX NumVarArray / Gurobi AddVars）
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
        {
            string setName = typeof(TVariable).Name;
            int before = Variables.Count;
            List<string> names = null;
            string stage = "key_generation";
            try
            {
                // 1) 由 sets 笛卡兒積組出所有變數名（TypeName@s1@s2@…）
                names = VariableBuilder.GetVarNames<TVariable>(sets).ToList();

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
        /// 批次建立連續變數，界限 [0, 1E100]（1E100 = 框架的「無上限」慣用值，不是 solver 的 infinity 常數）。
        /// </summary>
        /// <typeparam name="TVariable">變數類別；其 property 宣告順序 MUST 與 sets 傳入順序一致，否則之後 AddLHS 會找不到變數。</typeparam>
        /// <param name="sets">各維度的 set；框架取笛卡兒積產生所有變數名。</param>
        public virtual void BuildCVs<TVariable>(params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Continuous, nameof(BuildCVs));
            BatchBuild<TVariable>(0, 1E100, VarType.Continuous, sets);
        }

        /// <summary>批次建立連續變數並指定界限 [lb, ub]。</summary>
        public virtual void BuildCVs<TVariable>(double lb, double ub, params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Continuous, nameof(BuildCVs));
            BatchBuild<TVariable>(lb, ub, VarType.Continuous, sets);
        }

        /// <summary>批次建立整數變數，界限 [0, 1E100]。其餘語意同 <see cref="BuildCVs{TVariable}(object[])"/>。</summary>
        public virtual void BuildIVs<TVariable>(params object[] sets)
        {
            ValidateExplicitVariableType<TVariable>(VarType.Integer, nameof(BuildIVs));
            BatchBuild<TVariable>(0, 1E100, VarType.Integer, sets);
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

        /// <summary>某變數型別的全部變數名；型別不存在回空陣列（不丟例外）。</summary>
        public string[] GetSetVarNames<TVariable>()
        {
            string setName = typeof(TVariable).Name;
            return VariableSets.TryGetValue(setName, out var set)
                ? set.Keys.ToArray()
                : Array.Empty<string>();
        }

        /// <summary>取某變數型別的全部解值，key = 完整變數名（TypeName@…）。求解後呼叫；型別不存在回空字典。</summary>
        public Dictionary<string, double> GetSetVarValues<TVariable>()
        {
            string setName = typeof(TVariable).Name;
            try
            {
                if (!VariableSets.TryGetValue(setName, out var set))
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
            _buildSummaryDirty = true;
        }

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

            _buildSummaryDirty = false;
        }

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

        /// <summary>送出範圍限制式 lb ≤ LHS ≤ ub（只用 AddLHS 累積的左側；LHS 常數移到界上抵銷）。</summary>
        public bool CreateRange(double lb, double ub, string name)
            => CreateRangeCore(lb, ub, ValidateConstraintName(nameof(CreateRange), name));

        /// <summary>以 owner 類別名與維度值自動組名，送出範圍限制式。</summary>
        public bool CreateRange(double lb, double ub, ConstraintBase owner, params object[] dims)
            => CreateRangeCore(lb, ub, ComposeConstraintName(owner, dims));

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
                return false;
            }

            bool created = false;
            try
            {
                if (!_verifyConstraints.Contains(name))
                {
                    AddRangeConstraint(name, LinearExpr(_lhsTerms), lb - _lhsConst, ub - _lhsConst);
                    _verifyConstraints.Add(name);
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

        /// <summary>以 pool 累積的 LHS 為目標式，設為最小化。</summary>
        public void CreateMinimize() => SetObjectiveFromPool(ObjectiveSense.Minimize);

        /// <summary>以 pool 累積的 LHS 為目標式，設為最大化。</summary>
        public void CreateMaximize() => SetObjectiveFromPool(ObjectiveSense.Maximize);

        private void SetObjectiveFromPool(ObjectiveSense sense)
        {
            int expectedTerms = _lhsTerms.Count + _softPenaltyTerms.Count;
            Logging.Info($"[目標式建構開始] sense={sense} terms={expectedTerms}");
            if (_lhsTerms.Count == 0 && _softPenaltyTerms.Count == 0)
            {
                Logging.Warn($"[目標式建構完成] sense={sense} terms=0 reason=no_terms result=skipped");
                return;
            }
            try
            {
                _objectiveTerms.Clear();
                _objectiveTerms.AddRange(_lhsTerms);
                _objectiveSense = sense;
                ApplyObjective();
                ClearPool();
                Logging.Info($"[目標式建構完成] sense={sense} terms={expectedTerms} result=success");
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
            => SetObjective(LinearExpr(_objectiveTerms.Concat(_softPenaltyTerms)), _objectiveSense);

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
            const double inf = double.MaxValue;
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
        /// 要求各 engine 的 SetObjective 為「覆寫」語意（Gurobi/Solver 原生即是；CPLEX 在 SetObjective 內先移除舊目標式達成）。
        /// </summary>
        protected virtual void AddObjectiveTerm(double coef, TVar variable)
        {
            _softPenaltyTerms.Add((coef, variable));
            ApplyObjective();
        }

        #endregion
    }
}
