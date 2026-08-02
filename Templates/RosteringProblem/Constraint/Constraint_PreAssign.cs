using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>∀ (date, employee, group) ∈ PreAssign：ShiftAssign_{date,employee,group} = One</summary>
    public sealed class Constraint_PreAssign : ConstraintBase
    {
        private readonly List<Parameter_PreAssign> preAssign;
        private readonly double one;

        public Constraint_PreAssign(
            List<Parameter_PreAssign> preAssign,
            double one)
        {
            this.preAssign = preAssign;
            this.one = one;
        }

        public void Build(OptEngine engine)
        {
            foreach (var p in preAssign)
            {
                engine.AddLHS(1.0, new VariableB_ShiftAssign { Date = p.Date, Employee = p.Employee, Group = p.Group });
                engine.AddRHS(one);
                engine.CreateEqual($"{ConstraintName}@{p.Date:yyyy_MM_dd}@{p.Employee}@{p.Group}");
            }
        }
    }
}
