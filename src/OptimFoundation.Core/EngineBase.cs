using System;
using System.Collections.Generic;
using System.Linq;

namespace OptimFoundation.Core
{
    public abstract class EngineBase<TModel, TVar, TExpr, TConstr> : ISolverEngine
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

        private readonly List<(double coef, TVar var)> _lhsTerms = new List<(double, TVar)>();
        private readonly List<(double coef, TVar var)> _rhsTerms = new List<(double, TVar)>();
        private double _lhsConst = 0;
        private double _rhsConst = 0;

        // 以 constraint name 為 key，而非 variable set：名稱含迴圈索引（如 "Cap@TruckA"），不同條件不會誤判重複
        private readonly HashSet<string> _verifyConstraints = new HashSet<string>();

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

        public void CreateMinimize()
        {
            if (_lhsTerms.Count == 0) return;
            SetObjective(LinearExpr(_lhsTerms), ObjectiveSense.Minimize);
            ClearPool();
        }

        public void CreateMaximize()
        {
            if (_lhsTerms.Count == 0) return;
            SetObjective(LinearExpr(_lhsTerms), ObjectiveSense.Maximize);
            ClearPool();
        }

        #endregion

        #region Pool — 軟性限制式

        protected IEnumerable<(double coef, TVar var)> PoolLhsTerms => _lhsTerms;
        protected double PoolLhsConst => _lhsConst;

        /// <summary>
        /// 此 solver 是否支援軟性限制式。呼叫 CreateLeSoft/GeSoft/EqSoft 前先判斷，
        /// 避免到 runtime 才拿到 NotImplementedException。
        /// </summary>
        public virtual bool SupportsSoftConstraints => false;

        public virtual bool CreateLeSoft(double rhs, double penalty)
            => throw new NotImplementedException($"{GetType().Name} 不支援軟性限制式，請確認 SupportsSoftConstraints。");

        public virtual bool CreateGeSoft(double rhs, double penalty)
            => throw new NotImplementedException($"{GetType().Name} 不支援軟性限制式，請確認 SupportsSoftConstraints。");

        public virtual bool CreateEqSoft(double rhs, double penalty, string name)
            => throw new NotImplementedException($"{GetType().Name} 不支援軟性限制式，請確認 SupportsSoftConstraints。");

        #endregion
    }
}
