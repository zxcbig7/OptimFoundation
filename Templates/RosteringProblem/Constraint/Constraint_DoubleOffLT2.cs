using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>∀ date ∈ DATE，視窗 W = {sd ∈ DATE : date-DoubleOffWindow &lt; sd ≤ date}（|W| ≥ 2）, employee ∈ EMPLOYEE：
    /// |W|=2 → DoubleOffFlag_{date,employee} ≥ ShiftAssign_{date,employee,O} + ShiftAssign_{date-1,employee,O} - (|W|-One)
    /// |W|=3 → DoubleOffFlag_{date,employee} ≥ ShiftAssign_{date,employee,O} + ShiftAssign_{date-1,employee,O}
    ///                                          + One - ShiftAssign_{date-2,employee,O} - (|W|-One)
    /// ∀ employee ∈ EMPLOYEE：Σ_date DoubleOffFlag_{date,employee} + DoubleOffThreshold·DoubleOffLT2_{employee} ≥ DoubleOffThreshold</summary>
    public sealed class Constraint_DoubleOffLT2 : ConstraintBase
    {
        private readonly List<DateTime> dates;
        private readonly List<string> employees;
        private readonly double doubleOffWindow;
        private readonly double doubleOffThreshold;
        private readonly double one;

        public Constraint_DoubleOffLT2(
            List<Set_Date> dates,
            List<Set_Employee> employees,
            double doubleOffWindow,
            double doubleOffThreshold,
            double one)
        {
            this.dates = dates.Select(row => row.Date).ToList();
            this.employees = employees.Select(row => row.Employee).ToList();
            this.doubleOffWindow = doubleOffWindow;
            this.doubleOffThreshold = doubleOffThreshold;
            this.one = one;
        }

        public void Build(OptEngine engine)
        {
            foreach (var date in dates)
            {
                var window = dates.Where(sd => date.AddDays(-doubleOffWindow) < sd && sd <= date).ToList();
                if (window.Count < 2) continue;

                foreach (var employee in employees)
                {
                    if (window.Count == 2)
                    {
                        var preDate = date.AddDays(-1);
                        engine.AddLHS(1.0, new VariableB_DoubleOffFlag { Date = date, Employee = employee });
                        engine.AddRHS(1.0, new VariableB_ShiftAssign { Date = date, Employee = employee, Group = "O" });
                        engine.AddRHS(1.0, new VariableB_ShiftAssign { Date = preDate, Employee = employee, Group = "O" });
                        engine.AddRHS(-(window.Count - one));
                    }
                    else if (window.Count == 3)
                    {
                        var preDate = date.AddDays(-1);
                        var prePreDate = date.AddDays(-2);
                        engine.AddLHS(1.0, new VariableB_DoubleOffFlag { Date = date, Employee = employee });
                        engine.AddRHS(1.0, new VariableB_ShiftAssign { Date = date, Employee = employee, Group = "O" });
                        engine.AddRHS(1.0, new VariableB_ShiftAssign { Date = preDate, Employee = employee, Group = "O" });
                        engine.AddRHS(one);
                        engine.AddRHS(-1.0, new VariableB_ShiftAssign { Date = prePreDate, Employee = employee, Group = "O" });
                        engine.AddRHS(-(window.Count - one));
                    }

                    engine.CreateGreatEqual(this, "a", date, employee);
                }
            }

            foreach (var employee in employees)
            {
                foreach (var date in dates)
                    engine.AddLHS(1.0, new VariableB_DoubleOffFlag { Date = date, Employee = employee });

                engine.AddLHS(doubleOffThreshold, new VariableB_DoubleOffLT2 { Employee = employee });
                engine.AddRHS(doubleOffThreshold);
                engine.CreateGreatEqual(this, "b", employee);
            }
        }
    }
}
