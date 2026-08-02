using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>做休做：∀ date ∈ DATE, employee ∈ EMPLOYEE（需視窗 OffOneDayWindow 天歷史存在）：
    /// Off1Day_{date,employee} ≥ One - ShiftAssign_{date,employee,O} + ShiftAssign_{date-1,employee,O}
    ///                            + One - ShiftAssign_{date-2,employee,O} - (OffOneDayWindow - One)</summary>
    public sealed class Constraint_OffOneDay : ConstraintBase
    {
        private readonly Set_Date dates;
        private readonly Set_Employee employees;
        private readonly double offOneDayWindow;
        private readonly double one;

        public Constraint_OffOneDay(
            Set_Date dates,
            Set_Employee employees,
            double offOneDayWindow,
            double one)
        {
            this.dates = dates;
            this.employees = employees;
            this.offOneDayWindow = offOneDayWindow;
            this.one = one;
        }

        public void Build(OptEngine engine)
        {
            foreach (var date in dates)
                foreach (var employee in employees)
                {
                    var window = dates.Where(sd => date.AddDays(-offOneDayWindow) < sd && sd <= date).ToList();
                    if (window.Count < offOneDayWindow) continue;

                    var preDate = date.AddDays(-1);
                    var prePreDate = date.AddDays(-2);

                    engine.AddLHS(1.0, new VariableB_Off1Day { Date = date, Employee = employee });

                    engine.AddRHS(one);
                    engine.AddRHS(-1.0, new VariableB_ShiftAssign { Date = date, Employee = employee, Group = "O" });
                    engine.AddRHS(1.0, new VariableB_ShiftAssign { Date = preDate, Employee = employee, Group = "O" });
                    engine.AddRHS(one);
                    engine.AddRHS(-1.0, new VariableB_ShiftAssign { Date = prePreDate, Employee = employee, Group = "O" });
                    engine.AddRHS(-(offOneDayWindow - one));

                    engine.CreateGreatEqual($"{ConstraintName}@{date:yyyy_MM_dd}@{employee}");
                }
        }
    }
}
