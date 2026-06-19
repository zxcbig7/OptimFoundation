using System;
using System.Collections.Generic;
using System.Linq;

namespace OptimFoundation.Core
{
    public abstract class EngineBase<TModel, TVar, TExpr, TConstr> : ISolverEngine, ITrajectorySource
    {
        protected TModel Model;
        protected readonly Dictionary<string, TVar> Variables = new Dictionary<string, TVar>();
        protected readonly Dictionary<string, Dictionary<string, TVar>> VariableSets = new Dictionary<string, Dictionary<string, TVar>>();
        public int varCount => Variables.Count;
        public int TotalVarCount => VariableSets.Values.Sum(s => s.Count);
        public ISolverConfig Config { get; protected set; }
        public SolveStatus Status { get; protected set; } = SolveStatus.NotSolved;
        public double BestObjValue { get; protected set; }
        public double MIPGap { get; protected set; }

        /// <summary>最近一次 Solve() 的統一 telemetry；由各 engine 的 Solve() 回填。</summary>
        public SolveMetrics LastMetrics { get; protected set; }

        /// <summary>已建立的限制式數量；預設 0，需要的 engine override。</summary>
        public virtual int ConstraintCount => 0;

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
                catch { return null; }
            }
            return null;
        }

        // ── ITrajectorySource 預設：不支援（CPLEX override）。未支援者呼叫端不報錯 ──
        public virtual bool SupportsTrajectory => false;
        public virtual void EnableTrajectory() { /* no-op；支援的 engine override */ }
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
        //   Build          — 入口：呼叫 Configuration(Config) 完成初始化
        //   Solve          — 求解，回傳 bool（true = Optimal or Feasible）
        //   GetObjectiveValue / GetVariableValue / Dispose
        //
        // 可選 override 的 virtual 方法（EngineBase 有 default 實作）：
        //   AddVariables   — 批次建立變數（solver override 用原生 batch API 提升效能）
        //   BuildCVs / BuildIVs / BuildBVs — 批次建立變數，寫入 VariableSets
        //   CreateLeSoft / CreateGeSoft / CreateEqSoft — 軟性限制式（penalty 法）
        // ════════════════════════════════════════════════════════════════

        public abstract void Configuration(ISolverConfig config);

        protected abstract TVar AddVariable(string name, double lb, double ub, VarType type);
        protected abstract TExpr LinearExpr(IEnumerable<(double coef, TVar var)> terms);
        protected abstract TConstr AddConstraint(string name, TExpr lhs, ConstraintSense sense, double rhs);
        protected abstract TConstr AddRangeConstraint(string name, TExpr expr, double lb, double ub);
        protected abstract void SetObjective(TExpr expr, ObjectiveSense sense);
        protected abstract void SetVariableBounds(TVar variable, double? lb, double? ub);

        /// <summary>
        /// 建立模型的入口，由外部呼叫。建議實作流程：
        /// </summary>>
        public abstract void Build();
        public abstract bool Solve();
        public abstract double GetObjectiveValue();
        public abstract double GetVariableValue(string name);
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
                AddVariable(name, lb, ub, type);
        }

        private void BatchBuild<ElementClass>(double lb, double ub, VarType type, object[] sets)
        {
            string setName = typeof(ElementClass).Name;
            if (!VariableSets.ContainsKey(setName))
                VariableSets[setName] = new Dictionary<string, TVar>();

            var names = VariableBuilder.GetVarNames<ElementClass>(sets).ToList();
            AddVariables(names, lb, ub, type);

            var varSet = VariableSets[setName];
            foreach (var name in names)
                varSet[name] = Variables[name];
        }


        public virtual void BuildCVs<ElementClass>(params object[] sets)
            => BatchBuild<ElementClass>(0, 1E100, VarType.Continuous, sets);

        public virtual void BuildCVs<ElementClass>(double lb, double ub, params object[] sets)
            => BatchBuild<ElementClass>(lb, ub, VarType.Continuous, sets);

        public virtual void BuildIVs<ElementClass>(params object[] sets)
            => BatchBuild<ElementClass>(0, 1E100, VarType.Integer, sets);

        public virtual void BuildIVs<ElementClass>(double lb, double ub, params object[] sets)
            => BatchBuild<ElementClass>(lb, ub, VarType.Integer, sets);

        public virtual void BuildBVs<ElementClass>(params object[] sets)
            => BatchBuild<ElementClass>(0, 1, VarType.Binary, sets);

        #endregion

        #region VariableManager — 查詢

        protected TVar ReadVar(object searchData)
        {
            string setName = searchData.GetType().Name;
            string varName = searchData.ToString();

            if (VariableSets.TryGetValue(setName, out var set) && set.TryGetValue(varName, out var v))
                return v;

            throw new KeyNotFoundException($"找不到變數 '{varName}' in VariableSet '{setName}'");
        }

        protected Dictionary<string, TVar> GetVariableSet(string setName)
        {
            if (VariableSets.TryGetValue(setName, out var set))
                return set;
            throw new KeyNotFoundException($"找不到 VariableSet '{setName}'");
        }

        public string[] GetAllVarNames()
            => VariableSets.Values.SelectMany(s => s.Keys).ToArray();

        public string[] GetSetVarNames<ElementClass>()
        {
            string setName = typeof(ElementClass).Name;
            return VariableSets.TryGetValue(setName, out var set)
                ? set.Keys.ToArray()
                : Array.Empty<string>();
        }

        public Dictionary<string, double> GetSetVarValues<ElementClass>()
        {
            string setName = typeof(ElementClass).Name;
            if (!VariableSets.TryGetValue(setName, out var set))
                return new Dictionary<string, double>();
            return set.ToDictionary(kvp => kvp.Key, kvp => GetVariableValue(kvp.Key));
        }

        public virtual IReadOnlyDictionary<string, double> GetSolution(string varTypeName = null)
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

        #endregion

        #region VariableManager — 設定變數界限

        protected void SetVarLB(object searchData, double lb)
            => SetVariableBounds(ReadVar(searchData), lb, null);

        protected void SetVarUB(object searchData, double ub)
            => SetVariableBounds(ReadVar(searchData), null, ub);

        protected void SetVarRange(object searchData, double lb, double ub)
            => SetVariableBounds(ReadVar(searchData), lb, ub);

        #endregion

        #region VariableManager — 重設

        public void VarSetsReset()
        {
            VariableSets.Clear();
            Variables.Clear();
        }

        protected void ResetVerifyConstraints() => _verifyConstraints.Clear();

        #endregion

        #region Pool — 狀態管理

        public bool HasPool => _lhsTerms.Count > 0 || _rhsTerms.Count > 0;

        public void ClearPool()
        {
            _lhsTerms.Clear();
            _rhsTerms.Clear();
            _lhsConst = 0;
            _rhsConst = 0;
        }

        private bool CheckHasPool()
        {
            if (_lhsTerms.Count == 0 && _rhsTerms.Count == 0) return false;
            return true;
        }

        // LHS − RHS 的 lazy 合併，不建立新 List，LinearExpr 單次迭代即可消費
        private IEnumerable<(double coef, TVar var)> CombinedLhsMinusRhs()
            => _lhsTerms.Concat(_rhsTerms.Select(t => (-t.coef, t.var)));

        #endregion

        #region Pool — AddLHS / AddRHS

        public bool AddLHS(double coeff, object varSpec)
        {
            if (varSpec == null) return false;
            string key = varSpec.ToString();
            if (!Variables.TryGetValue(key, out var v))
                throw new KeyNotFoundException($"AddLHS: 找不到變數 '{key}'（type: {varSpec.GetType().Name}）。請確認 property 宣告順序與 Build*Vs 傳入 set 順序一致。");
            _lhsTerms.Add((coeff, v));
            return true;
        }

        public bool AddLHS(double constant)
        {
            _lhsConst += constant;
            return true;
        }

        public bool AddRHS(double coeff, object varSpec)
        {
            if (varSpec == null) return false;
            string key = varSpec.ToString();
            if (!Variables.TryGetValue(key, out var v))
                throw new KeyNotFoundException($"AddRHS: 找不到變數 '{key}'（type: {varSpec.GetType().Name}）。請確認 property 宣告順序與 Build*Vs 傳入 set 順序一致。");
            _rhsTerms.Add((coeff, v));
            return true;
        }

        public bool AddRHS(double constant)
        {
            _rhsConst += constant;
            return true;
        }

        #endregion

        #region Pool — 建立限制式

        public bool CreateGreatEqual(string name)
        {
            if (!CheckHasPool()) return false;
            if (!_verifyConstraints.Contains(name))
            {
                AddConstraint(name, LinearExpr(CombinedLhsMinusRhs()), ConstraintSense.GreaterEqual, _rhsConst - _lhsConst);
                _verifyConstraints.Add(name);
            }
            ClearPool(); // 即使 skip，也必須清空 pool，否則下一條約束的 LHS 會累積舊項目
            return true;
        }

        public bool CreateGreatEqual(double rhs, string name)
        {
            if (_lhsTerms.Count == 0) return false;
            _rhsConst = rhs;
            return CreateGreatEqual(name);
        }

        public bool CreateLessEqual(string name)
        {
            if (!CheckHasPool()) return false;
            if (!_verifyConstraints.Contains(name))
            {
                AddConstraint(name, LinearExpr(CombinedLhsMinusRhs()), ConstraintSense.LessEqual, _rhsConst - _lhsConst);
                _verifyConstraints.Add(name);
            }
            ClearPool();
            return true;
        }

        public bool CreateLessEqual(double rhs, string name)
        {
            if (_lhsTerms.Count == 0) return false;
            _rhsConst = rhs;
            return CreateLessEqual(name);
        }

        public bool CreateEqual(string name)
        {
            if (!CheckHasPool()) return false;
            if (!_verifyConstraints.Contains(name))
            {
                AddConstraint(name, LinearExpr(CombinedLhsMinusRhs()), ConstraintSense.Equal, _rhsConst - _lhsConst);
                _verifyConstraints.Add(name);
            }
            ClearPool();
            return true;
        }

        public bool CreateEqual(double rhs, string name)
        {
            if (_lhsTerms.Count == 0) return false;
            _rhsConst = rhs;
            return CreateEqual(name);
        }

        public bool CreateRange(double lb, double ub, string name)
        {
            if (_lhsTerms.Count == 0) return false;
            AddRangeConstraint(name, LinearExpr(_lhsTerms), lb - _lhsConst, ub - _lhsConst);
            ClearPool();
            return true;
        }

        #endregion

        #region Pool — 建立目標式

        public void CreateMinimize() => SetObjectiveFromPool(ObjectiveSense.Minimize);

        public void CreateMaximize() => SetObjectiveFromPool(ObjectiveSense.Maximize);

        private void SetObjectiveFromPool(ObjectiveSense sense)
        {
            if (_lhsTerms.Count == 0 && _softPenaltyTerms.Count == 0) return;
            _objectiveTerms.Clear();
            _objectiveTerms.AddRange(_lhsTerms);
            _objectiveSense = sense;
            ApplyObjective();
            ClearPool();
        }

        // 以追蹤的目標式項 + 已累積的軟性 penalty 項重設目標式。
        private void ApplyObjective()
            => SetObjective(LinearExpr(_objectiveTerms.Concat(_softPenaltyTerms)), _objectiveSense);

        #endregion

        #region Pool — 軟性限制式

        protected IEnumerable<(double coef, TVar var)> PoolLhsTerms => _lhsTerms;
        protected double PoolLhsConst => _lhsConst;

        /// <summary>
        /// 是否支援軟性限制式。通用實作放在 EngineBase（pool 加彈性變數 + 目標式加 penalty），
        /// 只要 engine 提供 AddVariable / AddConstraint / SetObjective primitive 即可，預設 true。
        /// </summary>
        public virtual bool SupportsSoftConstraints => true;

        /// <summary>軟性 LHS &lt;= rhs：加 surplus 變數 dp≥0，建 lhs − dp &lt;= rhs，目標式 += penalty·dp。</summary>
        public virtual bool CreateLeSoft(double rhs, double penalty)
            => BuildSoft(rhs, penalty, ConstraintSense.LessEqual, null);

        /// <summary>軟性 LHS &gt;= rhs：加 deficit 變數 dn≥0，建 lhs + dn &gt;= rhs，目標式 += penalty·dn。</summary>
        public virtual bool CreateGeSoft(double rhs, double penalty)
            => BuildSoft(rhs, penalty, ConstraintSense.GreaterEqual, null);

        /// <summary>軟性 LHS == rhs：加 dn,dp≥0，建 lhs + dn − dp == rhs，目標式 += penalty·(dn+dp)。</summary>
        public virtual bool CreateEqSoft(double rhs, double penalty, string name)
            => BuildSoft(rhs, penalty, ConstraintSense.Equal, name);

        // 軟性限制式通用建構：彈性變數放進變數池，penalty 放進目標式。
        // penalty 方向依目標式 sense：最小化 +penalty（懲罰違反量）、最大化 -penalty。
        private bool BuildSoft(double rhs, double penalty, ConstraintSense sense, string name)
        {
            if (!HasPool) return false;
            if (string.IsNullOrEmpty(name)) name = $"Soft_{sense}_{++_softCount}";

            double adjustedRhs = rhs + _rhsConst - _lhsConst;
            double p = _objectiveSense == ObjectiveSense.Maximize ? -penalty : penalty;
            var terms = new List<(double coef, TVar var)>(CombinedLhsMinusRhs());
            const double inf = double.MaxValue;

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
