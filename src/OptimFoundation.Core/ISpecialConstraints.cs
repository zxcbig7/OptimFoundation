using System.Collections.Generic;

namespace OptimFoundation.Core
{
    public interface ISpecialConstraints<TVar, TExpr>
    {
        void AddSOS1(IEnumerable<TVar> vars);
        void AddSOS2(IEnumerable<TVar> vars);
        void AddIndicator(TVar binary, TExpr expr, ConstraintSense sense, double rhs);
        void AddLazyConstraint(TExpr expr, ConstraintSense sense, double rhs);
    }
}
