using System;
using System.Collections.Generic;
using ILOG.Concert;
using ILOG.CPLEX;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// CPLEX 求解器引擎。
    /// 使用方式：繼承本類別並覆寫 Build()，在 Build() 內呼叫
    /// AddVariable / LinearExpr / AddConstraint / SetObjective 定義模型，
    /// 然後呼叫 base.Build() 初始化求解器後繼續定義。
    /// </summary>
    public class OptEngine : EngineBase<ILOG.CPLEX.Cplex, INumVar, ILinearNumExpr, IRange>
    {
        public OptEngine(CplexConfig config) : base(config) { }

        #region EngineBase 抽象方法實作

        protected override INumVar AddVariable(string name, double lb, double ub, VarType type)
        {
            NumVarType cplexType = type switch
            {
                VarType.Integer => NumVarType.Int,
                VarType.Binary  => NumVarType.Bool,
                _               => NumVarType.Float
            };
            var v = Model.NumVar(lb, ub, cplexType, name);
            Variables[name] = v;
            return v;
        }

        protected override ILinearNumExpr LinearExpr(IEnumerable<(double coef, INumVar var)> terms)
        {
            var expr = Model.LinearNumExpr();
            foreach (var (coef, v) in terms)
                expr.AddTerm(coef, v);
            return expr;
        }

        protected override IRange AddConstraint(string name, ILinearNumExpr lhs, ConstraintSense sense, double rhs)
        {
            IRange r = sense switch
            {
                ConstraintSense.LessEqual    => Model.AddLe(lhs, rhs),
                ConstraintSense.Equal        => Model.AddEq(lhs, rhs),
                ConstraintSense.GreaterEqual => Model.AddGe(lhs, rhs),
                _ => throw new ArgumentOutOfRangeException(nameof(sense))
            };
            r.Name = name;
            return r;
        }

        protected override void SetObjective(ILinearNumExpr expr, Core.ObjectiveSense sense)
        {
            if (sense == Core.ObjectiveSense.Minimize)
                Model.AddMinimize(expr);
            else
                Model.AddMaximize(expr);
        }

        #endregion

        #region ISolverEngine 實作

        /// <summary>
        /// 初始化 CPLEX 模型並套用 Config 參數。
        /// 子類別覆寫時必須先呼叫 base.Build()。
        /// </summary>
        public override void Build()
        {
            Model = new ILOG.CPLEX.Cplex();

            var cfg = Config as CplexConfig;

            if (!Config.LogToConsole)
                Model.SetOut(null);

            if (Config.TimeLimit.HasValue)
                Model.SetParam(ILOG.CPLEX.Cplex.Param.TimeLimit, Config.TimeLimit.Value);

            if (Config.MipGap.HasValue)
                Model.SetParam(ILOG.CPLEX.Cplex.Param.MIP.Tolerances.MIPGap, Config.MipGap.Value);

            if (Config.Threads.HasValue)
                Model.SetParam(ILOG.CPLEX.Cplex.Param.Threads, Config.Threads.Value);

            if (cfg != null)
            {
                if (cfg.RootAlgorithm.HasValue)
                    Model.SetParam(ILOG.CPLEX.Cplex.Param.RootAlgorithm, cfg.RootAlgorithm.Value);

                if (cfg.NodeAlgorithm.HasValue)
                    Model.SetParam(ILOG.CPLEX.Cplex.Param.NodeAlgorithm, cfg.NodeAlgorithm.Value);
            }
        }

        public override bool Solve()
        {
            bool solved = Model.Solve();

            var s = Model.GetStatus();
            if      (s == ILOG.CPLEX.Cplex.Status.Optimal)               Status = SolveStatus.Optimal;
            else if (s == ILOG.CPLEX.Cplex.Status.Feasible)              Status = SolveStatus.Feasible;
            else if (s == ILOG.CPLEX.Cplex.Status.Infeasible)            Status = SolveStatus.Infeasible;
            else if (s == ILOG.CPLEX.Cplex.Status.InfeasibleOrUnbounded) Status = SolveStatus.Infeasible;
            else if (s == ILOG.CPLEX.Cplex.Status.Unbounded)             Status = SolveStatus.Unbounded;
            else                                                         Status = SolveStatus.Error;

            return solved;
        }

        public override double GetObjectiveValue() => Model.GetObjValue();

        public override double GetVariableValue(string name) => Model.GetValue(Variables[name]);

        public override void Dispose()
        {
            Model?.End();
            Model = null;
        }

        #endregion

        #region 便捷方法（公開給子類別）

        protected INumVar CreateVar(string name, double lb = 0, double ub = double.MaxValue,
            VarType type = VarType.Continuous)
            => AddVariable(name, lb, ub, type);

        protected ILinearNumExpr Expr(IEnumerable<(double coef, INumVar var)> terms)
            => LinearExpr(terms);

        protected IRange AddLE(string name, ILinearNumExpr lhs, double rhs)
            => AddConstraint(name, lhs, ConstraintSense.LessEqual, rhs);

        protected IRange AddGE(string name, ILinearNumExpr lhs, double rhs)
            => AddConstraint(name, lhs, ConstraintSense.GreaterEqual, rhs);

        protected IRange AddEQ(string name, ILinearNumExpr lhs, double rhs)
            => AddConstraint(name, lhs, ConstraintSense.Equal, rhs);

        protected void Minimize(ILinearNumExpr expr) => SetObjective(expr, Core.ObjectiveSense.Minimize);
        protected void Maximize(ILinearNumExpr expr) => SetObjective(expr, Core.ObjectiveSense.Maximize);

        #endregion
    }
}
