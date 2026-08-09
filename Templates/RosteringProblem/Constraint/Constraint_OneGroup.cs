using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>∀ date ∈ DATE, employee ∈ EMPLOYEE：Σ_group ShiftAssign_{date,employee,group} = One</summary>
    public sealed class Constraint_OneGroup : ConstraintBase
    {
        private readonly List<DateTime> dates;
        private readonly List<string> employees;
        private readonly List<string> groups;
        private readonly double one;

        public Constraint_OneGroup(
            List<Set_Date> dates,
            List<Set_Employee> employees,
            List<Set_Group> groups,
            double one)
        {
            this.dates = dates.Select(row => row.Date).ToList();
            this.employees = employees.Select(row => row.Employee).ToList();
            this.groups = groups.Select(row => row.Group).ToList();
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
                    engine.CreateEqual(this, date, employee);
                }
        }
    }
}
