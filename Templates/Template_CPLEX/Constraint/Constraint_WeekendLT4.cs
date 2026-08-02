using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>weekends = {date ∈ DATE : date.DayOfWeek ∈ {Sat,Sun}}
    /// ∀ employee ∈ EMPLOYEE：WeekendLT4_{employee} + Σ_{date∈weekends} ShiftAssign_{date,employee,O} ≥ WeekendOffThreshold</summary>
    public sealed class Constraint_WeekendLT4 : ConstraintBase
    {
        private readonly Set_Date dates;
        private readonly Set_Employee employees;
        private readonly double weekendOffThreshold;

        public Constraint_WeekendLT4(
            Set_Date dates,
            Set_Employee employees,
            double weekendOffThreshold)
        {
            this.dates = dates;
            this.employees = employees;
            this.weekendOffThreshold = weekendOffThreshold;
        }

        public void Build(OptEngine engine)
        {
            var weekends = dates
                .Where(date => date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                .ToList();

            foreach (var employee in employees)
            {
                engine.AddLHS(1.0, new VariableX_WeekendLT4 { Employee = employee });
                engine.AddRHS(weekendOffThreshold);
                foreach (var date in weekends)
                    engine.AddRHS(-1.0, new VariableB_ShiftAssign { Date = date, Employee = employee, Group = "O" });
                engine.CreateGreatEqual($"{ConstraintName}@{employee}");
            }
        }
    }
}
