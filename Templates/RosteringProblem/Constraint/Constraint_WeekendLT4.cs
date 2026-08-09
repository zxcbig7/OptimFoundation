using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>weekends = {date ∈ DATE : date.DayOfWeek ∈ {Sat,Sun}}
    /// ∀ employee ∈ EMPLOYEE：WeekendLT4_{employee} + Σ_{date∈weekends} ShiftAssign_{date,employee,O} ≥ WeekendOffThreshold</summary>
    public sealed class Constraint_WeekendLT4 : ConstraintBase
    {
        private readonly List<DateTime> dates;
        private readonly List<string> employees;
        private readonly double weekendOffThreshold;

        public Constraint_WeekendLT4(
            List<Set_Date> dates,
            List<Set_Employee> employees,
            double weekendOffThreshold)
        {
            this.dates = dates.Select(row => row.Date).ToList();
            this.employees = employees.Select(row => row.Employee).ToList();
            this.weekendOffThreshold = weekendOffThreshold;
        }

        public void Build(OptEngine engine)
        {
            var weekends = dates
                .Where(date => date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                .ToList();

            foreach (var employee in employees)
            {
                engine.AddLHS(1.0, new VariableC_WeekendLT4 { Employee = employee });
                engine.AddRHS(weekendOffThreshold);
                foreach (var date in weekends)
                    engine.AddRHS(-1.0, new VariableB_ShiftAssign { Date = date, Employee = employee, Group = "O" });
                engine.CreateGreatEqual(this, employee);
            }
        }
    }
}
