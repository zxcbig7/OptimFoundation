using System;
using System.Collections.Generic;

namespace OptimFoundation.Core
{
    public abstract class EngineBase<TModel, TVar, TExpr, TConstr> : ISolverEngine
    {
        protected TModel Model;
        protected readonly Dictionary<string, TVar> Variables = new Dictionary<string, TVar>();

        public ISolverConfig Config { get; protected set; }
        public SolveStatus Status { get; protected set; } = SolveStatus.NotSolved;

        protected EngineBase(ISolverConfig config)
        {
            Config = config;
        }

        protected abstract TVar AddVariable(string name, double lb, double ub, VarType type);
        protected abstract TExpr LinearExpr(IEnumerable<(double coef, TVar var)> terms);
        protected abstract TConstr AddConstraint(string name, TExpr lhs, ConstraintSense sense, double rhs);
        protected abstract void SetObjective(TExpr expr, ObjectiveSense sense);

        public abstract void Build();
        public abstract bool Solve();
        public abstract double GetObjectiveValue();
        public abstract double GetVariableValue(string name);
        public abstract void Dispose();

        public virtual IReadOnlyDictionary<string, double> GetSolution(string varTypeName = null)
        {
            var result = new Dictionary<string, double>();
            string prefix = varTypeName == null ? null : varTypeName + "@";
            foreach (var key in Variables.Keys)
            {
                if (prefix == null || key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    result[key] = GetVariableValue(key);
            }
            return result;
        }
    }
}
