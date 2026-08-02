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
        private readonly Set_Date dates;
        private readonly Set_Employee employees;
        private readonly double sixDayWindow;
        private readonly double one;

        public Constraint_SixDayWork(
            Set_Date dates,
            Set_Employee employees,
            double sixDayWindow,
            double one)
        {
            this.dates = dates;
            this.employees = employees;
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
                        engine.CreateLessEqual($"{ConstraintName}@{date:yyyy_MM_dd}@{employee}");
                    }

                    engine.AddLHS(1.0, new VariableB_SixDayWork { Date = date, Employee = employee });
                    engine.AddRHS(one);
                    foreach (var sd in window)
                        engine.AddRHS(-1.0, new VariableB_ShiftAssign { Date = sd, Employee = employee, Group = "O" });
                    engine.CreateGreatEqual($"{ConstraintName}@{date:yyyy_MM_dd}@{employee}");
                }
        }
    }
}
