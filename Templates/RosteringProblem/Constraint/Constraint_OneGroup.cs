using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>∀ date ∈ DATE, employee ∈ EMPLOYEE：Σ_group ShiftAssign_{date,employee,group} = One</summary>
    public sealed class Constraint_OneGroup : ConstraintBase
    {
        private readonly Set_Date dates;
        private readonly Set_Employee employees;
        private readonly Set_Group groups;
        private readonly double one;

        public Constraint_OneGroup(
            Set_Date dates,
            Set_Employee employees,
            Set_Group groups,
            double one)
        {
            this.dates = dates;
            this.employees = employees;
            this.groups = groups;
            this.one = one;
        }

        public void Build(OptEngine engine)
        {
            foreach (var date in dates)
                foreach (var employee in employees)
                {
                    foreach (var group in groups)
                        engine.AddLHS(1.0, new VariableB_ShiftAssign { Date = date, Employee = employee, Group = group });

                    engine.AddRHS(one);
                    engine.CreateEqual($"{ConstraintName}@{date:yyyy_MM_dd}@{employee}");
                }
        }
    }
}
