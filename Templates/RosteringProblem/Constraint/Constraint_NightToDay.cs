using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>∀ date ∈ DATE, employee ∈ EMPLOYEE（需視窗 NightToDayWindow 天歷史存在）, rule ∈ NightToDayRule：
    /// NightToDay_{date,employee} ≥ ShiftAssign_{date-1,employee,rule.PreGroup} + ShiftAssign_{date,employee,rule.Group} - One</summary>
    public sealed class Constraint_NightToDay : ConstraintBase
    {
        private readonly List<DateTime> dates;
        private readonly List<string> employees;
        private readonly List<Parameter_NightToDay> nightToDayRules;
        private readonly double nightToDayWindow;
        private readonly double one;

        public Constraint_NightToDay(
            List<Set_Date> dates,
            List<Set_Employee> employees,
            List<Parameter_NightToDay> nightToDayRules,
            double nightToDayWindow,
            double one)
        {
            this.dates = dates.Select(row => row.Date).ToList();
            this.employees = employees.Select(row => row.Employee).ToList();
            this.nightToDayRules = nightToDayRules;
            this.nightToDayWindow = nightToDayWindow;
            this.one = one;
        }

        public void Build(OptEngine engine)
        {
            foreach (var date in dates)
                foreach (var employee in employees)
                {
                    var window = dates.Where(sd => date.AddDays(-nightToDayWindow) < sd && sd <= date).ToList();
                    if (window.Count < nightToDayWindow) continue;

                    var preDate = date.AddDays(-1);

                    foreach (var rule in nightToDayRules)
                    {
                        engine.AddLHS(1.0, new VariableB_NightToDay { Date = date, Employee = employee });
                        engine.AddRHS(1.0, new VariableB_ShiftAssign { Date = preDate, Employee = employee, Group = rule.PreGroup });
                        engine.AddRHS(1.0, new VariableB_ShiftAssign { Date = date, Employee = employee, Group = rule.Group });
                        engine.AddRHS(-one);
                        engine.CreateGreatEqual(this, date, employee, rule.PreGroup, rule.Group);
                    }
                }
        }
    }
}
