using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>∀ date ∈ DATE, employee ∈ EMPLOYEE，視窗 W = {sd ∈ DATE : date-SixDayWindow &lt; sd ≤ date}（|W| = SixDayWindow）：
    /// ∀ sd ∈ W：SixDayWork_{date,employee} ≤ One - ShiftAssign_{sd,employee,O}
    /// SixDayWork_{date,employee} ≥ One - Σ_{sd∈W} ShiftAssign_{sd,employee,O}
    /// （SixDayWork = 1 若且唯若視窗內連續 SixDayWindow 天皆未排 Off 班）</summary>
    public sealed class Constraint_SixDayWork : ConstraintBase
    {
        private readonly List<DateTime> dates;
        private readonly List<string> employees;
        private readonly double sixDayWindow;
        private readonly double one;

        public Constraint_SixDayWork(
            List<Set_Date> dates,
            List<Set_Employee> employees,
            double sixDayWindow,
            double one)
        {
            this.dates = dates.Select(row => row.Date).ToList();
            this.employees = employees.Select(row => row.Employee).ToList();
            this.sixDayWindow = sixDayWindow;
            this.one = one;
        }

        public void Build(OptEngine engine)
        {
            foreach (var date in dates)
                foreach (var employee in employees)
                {
                    var window = dates.Where(sd => date.AddDays(-sixDayWindow) < sd && sd <= date).ToList();
                    if (window.Count < sixDayWindow) continue;

                    foreach (var sd in window)
                    {
                        engine.AddLHS(1.0, new VariableB_SixDayWork { Date = date, Employee = employee });
                        engine.AddRHS(one);
                        engine.AddRHS(-1.0, new VariableB_ShiftAssign { Date = sd, Employee = employee, Group = "O" });
                        engine.CreateLessEqual(this, date, employee, sd);
                    }

                    engine.AddLHS(1.0, new VariableB_SixDayWork { Date = date, Employee = employee });
                    engine.AddRHS(one);
                    foreach (var sd in window)
                        engine.AddRHS(-1.0, new VariableB_ShiftAssign { Date = sd, Employee = employee, Group = "O" });
                    engine.CreateGreatEqual(this, date, employee);
                }
        }
    }
}
