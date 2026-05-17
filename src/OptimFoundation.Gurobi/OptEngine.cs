using System;
using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Core;

#if GUROBI_INSTALLED
using Gurobi;
#endif

namespace OptimFoundation.Gurobi
{
#if GUROBI_INSTALLED

    /// <summary>
    /// Gurobi 求解器引擎。
    /// 使用方式：繼承本類別並覆寫 Build()，在 Build() 內呼叫
    /// AddVariable / LinearExpr / AddConstraint / SetObjective 定義模型，
    /// 變數建立完畢後呼叫 Update()，然後呼叫 base.Build() 初始化求解器參數。
    /// </summary>
    public class OptEngine : EngineBase<GRBModel, GRBVar, GRBLinExpr, GRBConstr>
    {
        private GRBEnv _env;
        private bool _exportLp;
        private bool _exportMps;
        private bool _exportSol;
        private readonly string _startTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        private readonly List<GRBConstr> _constraints = new List<GRBConstr>();

        public OptEngine(GurobiConfig config) : base(config) { }

        #region EngineBase 抽象方法實作

        protected override GRBVar AddVariable(string name, double lb, double ub, VarType type)
        {
            char grbType = type switch
            {
                VarType.Integer => GRB.INTEGER,
                VarType.Binary => GRB.BINARY,
                _ => GRB.CONTINUOUS
            };
            var v = Model.AddVar(lb, ub, 0, grbType, name);
            Variables[name] = v;
            return v;
        }

        protected override GRBLinExpr LinearExpr(IEnumerable<(double coef, GRBVar var)> terms)
        {
            var expr = new GRBLinExpr();
            foreach (var (coef, v) in terms)
                expr.AddTerm(coef, v);
            return expr;
        }

        protected override GRBConstr AddConstraint(string name, GRBLinExpr lhs, ConstraintSense sense, double rhs)
        {
            char grb = sense switch
            {
                ConstraintSense.LessEqual => GRB.LESS_EQUAL,
                ConstraintSense.Equal => GRB.EQUAL,
                ConstraintSense.GreaterEqual => GRB.GREATER_EQUAL,
                _ => throw new ArgumentOutOfRangeException(nameof(sense))
            };
            var c = Model.AddConstr(lhs, grb, rhs, name);
            _constraints.Add(c);
            return c;
        }

        protected override void SetObjective(GRBLinExpr expr, ObjectiveSense sense)
        {
            int grb = sense == ObjectiveSense.Minimize ? GRB.MINIMIZE : GRB.MAXIMIZE;
            Model.SetObjective(expr, grb);
        }

        #endregion

        #region ISolverEngine 實作

        public override void Build()
        {
            var cfg = Config as GurobiConfig;
            _env = new GRBEnv();

            if (cfg != null)
            {
                if (cfg.LicenseId.HasValue)
                    _env.Set(GRB.IntParam.LicenseID, cfg.LicenseId.Value);
                if (!string.IsNullOrEmpty(cfg.WlsAccessId))
                    _env.Set(GRB.StringParam.WLSAccessID, cfg.WlsAccessId);
                if (!string.IsNullOrEmpty(cfg.WlsSecret))
                    _env.Set(GRB.StringParam.WLSSecret, cfg.WlsSecret);
            }

            if (!Config.LogToConsole)
                _env.Set(GRB.IntParam.OutputFlag, 0);

            if (!string.IsNullOrEmpty(Config.LogFilePath))
                _env.Set(GRB.StringParam.LogFile, Config.LogFilePath);

            Model = new GRBModel(_env);

            if (Config.TimeLimit.HasValue)
                Model.Set(GRB.DoubleParam.TimeLimit, Config.TimeLimit.Value);
            if (Config.MipGap.HasValue)
                Model.Set(GRB.DoubleParam.MIPGap, Config.MipGap.Value);
            if (Config.Threads.HasValue)
                Model.Set(GRB.IntParam.Threads, Config.Threads.Value);

            if (cfg != null)
            {
                if (cfg.Method.HasValue)
                    Model.Set(GRB.IntParam.Method, cfg.Method.Value);
                if (cfg.Presolve.HasValue)
                    Model.Set(GRB.IntParam.Presolve, cfg.Presolve.Value);
                if (cfg.MipFocus.HasValue)
                    Model.Set(GRB.IntParam.MIPFocus, cfg.MipFocus.Value);
                if (cfg.Seed.HasValue)
                    Model.Set(GRB.IntParam.Seed, cfg.Seed.Value);
                if (cfg.FeasibilityTol.HasValue)
                    Model.Set(GRB.DoubleParam.FeasibilityTol, cfg.FeasibilityTol.Value);
                if (cfg.OptimalityTol.HasValue)
                    Model.Set(GRB.DoubleParam.OptimalityTol, cfg.OptimalityTol.Value);
                if (cfg.Heuristics.HasValue)
                    Model.Set(GRB.DoubleParam.Heuristics, cfg.Heuristics.Value);
                if (cfg.SoftMemLimit.HasValue)
                    Model.Set(GRB.DoubleParam.SoftMemLimit, cfg.SoftMemLimit.Value);

                _exportLp = cfg.ExportLp;
                _exportMps = cfg.ExportMps;
                _exportSol = cfg.ExportSol;

                if (cfg.ExportLp || cfg.ExportSol)
                {
                    FolderDir.LP.CreateFolder();
                    FolderDir.Sol.CreateFolder();
                    FolderDir.IIS.CreateFolder();
                }
                if (cfg.ExportMps)
                    FolderDir.Model.CreateFolder();
            }
        }

        public override bool Solve()
        {
            string proj = (Config as GurobiConfig)?.ProjectName ?? "Project";

            if (_exportLp)
                Model.Write(FolderDir.LP.GetFilePath($"{proj}_LP_{_startTime}.lp"));
            if (_exportMps)
                Model.Write(FolderDir.Model.GetFilePath($"{proj}_MPS_{_startTime}.mps"));

            Model.Optimize();

            int s = Model.Status;
            if (s == GRB.Status.OPTIMAL) Status = SolveStatus.Optimal;
            else if (s == GRB.Status.SUBOPTIMAL || s == GRB.Status.TIME_LIMIT
                                                || s == GRB.Status.SOLUTION_LIMIT) Status = SolveStatus.Feasible;
            else if (s == GRB.Status.INFEASIBLE || s == GRB.Status.INF_OR_UNBD) Status = SolveStatus.Infeasible;
            else if (s == GRB.Status.UNBOUNDED) Status = SolveStatus.Unbounded;
            else Status = SolveStatus.Error;

            bool ok = Status == SolveStatus.Optimal || Status == SolveStatus.Feasible;

            if (ok && _exportSol)
                Model.Write(FolderDir.Sol.GetFilePath($"{proj}_Solution_{_startTime}.sol"));

            if (Status == SolveStatus.Infeasible)
            {
                Model.ComputeIIS();
                string iisPath = FolderDir.IIS.GetFilePath($"{proj}_IIS_{_startTime}.ilp");
                Model.Write(iisPath);
                Logging.Info($"[OptEngine] IIS written: {iisPath}");
            }

            if (Model.IsMIP == 1 && ok)
            {
                Logging.Info($"[OptEngine] ObjVal={Model.ObjVal}  ObjBound={Model.ObjBound}  MIPGap={Model.MIPGap}");
            }

            return ok;
        }

        public override double GetObjectiveValue() => Model.ObjVal;

        public override double GetVariableValue(string name) => Variables[name].X;

        public override void Dispose()
        {
            Model?.Dispose();
            _env?.Dispose();
            Model = null;
            _env = null;
        }

        #endregion

        #region 便捷方法（公開給子類別）

        /// <summary>Gurobi lazy update — 建立完變數後必須呼叫一次。</summary>
        protected void Update() => Model.Update();

        protected GRBVar CreateVar(string name, double lb = 0, double ub = GRB.INFINITY,
            VarType type = VarType.Continuous)
            => AddVariable(name, lb, ub, type);

        protected GRBLinExpr Expr(IEnumerable<(double coef, GRBVar var)> terms)
            => LinearExpr(terms);

        protected GRBConstr AddLE(string name, GRBLinExpr lhs, double rhs)
            => AddConstraint(name, lhs, ConstraintSense.LessEqual, rhs);

        protected GRBConstr AddGE(string name, GRBLinExpr lhs, double rhs)
            => AddConstraint(name, lhs, ConstraintSense.GreaterEqual, rhs);

        protected GRBConstr AddEQ(string name, GRBLinExpr lhs, double rhs)
            => AddConstraint(name, lhs, ConstraintSense.Equal, rhs);

        protected void Minimize(GRBLinExpr expr) => SetObjective(expr, ObjectiveSense.Minimize);
        protected void Maximize(GRBLinExpr expr) => SetObjective(expr, ObjectiveSense.Maximize);

        #endregion

        #region Pool 模式（方便動態組裝表達式）

        private readonly List<GRBVar> _lhsVars = new List<GRBVar>();
        private readonly List<double> _lhsCoefs = new List<double>();
        private readonly List<GRBVar> _rhsVars = new List<GRBVar>();
        private readonly List<double> _rhsCoefs = new List<double>();
        private double _lhsConst;
        private double _rhsConst;

        protected bool HasPool => _lhsVars.Count > 0 || _rhsVars.Count > 0;

        protected GRBLinExpr LhsExpr
        {
            get
            {
                var expr = new GRBLinExpr();
                var coefs = _lhsCoefs.Count == 0
                    ? Enumerable.Repeat(1.0, _lhsVars.Count).ToArray()
                    : _lhsCoefs.ToArray();
                expr.AddTerms(coefs, _lhsVars.ToArray());
                expr += _lhsConst;
                return expr;
            }
        }

        protected GRBLinExpr RhsExpr
        {
            get
            {
                var expr = new GRBLinExpr();
                var coefs = _rhsCoefs.Count == 0
                    ? Enumerable.Repeat(1.0, _rhsVars.Count).ToArray()
                    : _rhsCoefs.ToArray();
                expr.AddTerms(coefs, _rhsVars.ToArray());
                expr += _rhsConst;
                return expr;
            }
        }

        protected void AddLhs(GRBVar v, double coef = 1.0) { _lhsVars.Add(v); _lhsCoefs.Add(coef); }
        protected void AddLhs(double constant) => _lhsConst += constant;
        protected void AddRhs(GRBVar v, double coef = 1.0) { _rhsVars.Add(v); _rhsCoefs.Add(coef); }
        protected void AddRhs(double constant) => _rhsConst += constant;

        protected void ClearPool()
        {
            _lhsVars.Clear(); _lhsCoefs.Clear();
            _rhsVars.Clear(); _rhsCoefs.Clear();
            _lhsConst = 0; _rhsConst = 0;
        }

        protected GRBConstr CommitLE(string name)
        {
            var c = Model.AddConstr(LhsExpr, GRB.LESS_EQUAL, RhsExpr, name);
            _constraints.Add(c); ClearPool(); return c;
        }

        protected GRBConstr CommitGE(string name)
        {
            var c = Model.AddConstr(LhsExpr, GRB.GREATER_EQUAL, RhsExpr, name);
            _constraints.Add(c); ClearPool(); return c;
        }

        protected GRBConstr CommitEQ(string name)
        {
            var c = Model.AddConstr(LhsExpr, GRB.EQUAL, RhsExpr, name);
            _constraints.Add(c); ClearPool(); return c;
        }

        protected GRBConstr CommitRange(double lb, double ub, string name)
        {
            var c = Model.AddRange(LhsExpr, lb, ub, name);
            _constraints.Add(c); ClearPool(); return c;
        }

        #endregion

        #region 軟性限制式

        /// <summary>將 LHS &lt;= RHS 轉為 penalty 項加入目標式。</summary>
        protected bool CreateLeSoft(double rhs, double penalty)
        {
            if (!HasPool) return false;
            int sense = Model.ModelSense;
            if (sense == GRB.MAXIMIZE) penalty *= -1;
            GRBLinExpr obj = Model.GetObjective() as GRBLinExpr ?? new GRBLinExpr();
            Model.SetObjective(obj + (LhsExpr - rhs) * penalty, sense);
            ClearPool();
            return true;
        }

        /// <summary>將 LHS &gt;= RHS 轉為 penalty 項加入目標式。</summary>
        protected bool CreateGeSoft(double rhs, double penalty)
        {
            if (!HasPool) return false;
            int sense = Model.ModelSense;
            if (sense == GRB.MINIMIZE) penalty *= -1;
            GRBLinExpr obj = Model.GetObjective() as GRBLinExpr ?? new GRBLinExpr();
            Model.SetObjective(obj + (rhs - LhsExpr) * penalty, sense);
            ClearPool();
            return true;
        }

        /// <summary>將 LHS == rhs 以雙向 delta 鬆弛加入目標式。</summary>
        protected bool CreateEqSoft(double rhs, double penalty, string name)
        {
            if (!HasPool) return false;
            int sense = Model.ModelSense;
            if (sense == GRB.MAXIMIZE) penalty *= -1;
            var dn = Model.AddVar(0, GRB.INFINITY, penalty, GRB.CONTINUOUS, $"Delta_Neg_{name}");
            var dp = Model.AddVar(0, GRB.INFINITY, penalty, GRB.CONTINUOUS, $"Delta_Pos_{name}");
            Model.Update();
            AddLhs(dn, 1.0);
            AddLhs(dp, -1.0);
            AddRhs(rhs);
            CommitEQ(name);
            return true;
        }

        #endregion
    }

#else

    // DLL 未安裝時的 stub，維持 solution 可編譯。
    // 安裝 Gurobi 後：在 .csproj 取消註解 Reference 並加入 GUROBI_INSTALLED define。
    public sealed class OptEngine : EngineBase<object, object, object, object>
    {
        public OptEngine(GurobiConfig config) : base(config) { }

        protected override object AddVariable(string name, double lb, double ub, VarType type)
            => throw new NotSupportedException("Gurobi DLL 未安裝");

        protected override object LinearExpr(IEnumerable<(double coef, object var)> terms)
            => throw new NotSupportedException("Gurobi DLL 未安裝");

        protected override object AddConstraint(string name, object lhs, ConstraintSense sense, double rhs)
            => throw new NotSupportedException("Gurobi DLL 未安裝");

        protected override void SetObjective(object expr, ObjectiveSense sense)
            => throw new NotSupportedException("Gurobi DLL 未安裝");

        public override void Build()           => throw new NotSupportedException("Gurobi DLL 未安裝");
        public override bool Solve()           => throw new NotSupportedException("Gurobi DLL 未安裝");
        public override double GetObjectiveValue()       => throw new NotSupportedException("Gurobi DLL 未安裝");
        public override double GetVariableValue(string name) => throw new NotSupportedException("Gurobi DLL 未安裝");
        public override void Dispose() { }
    }

#endif
}
