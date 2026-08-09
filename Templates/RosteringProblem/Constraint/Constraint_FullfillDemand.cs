using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>∀ date ∈ DATE, group ∈ GROUP \ {O}：Σ_employee ShiftAssign_{date,employee,group} = ShiftDemand_{date,group}</summary>
    public sealed class Constraint_FullfillDemand : ConstraintBase
    {
        private readonly List<DateTime> dates;
        private readonly List<string> employees;
        private readonly List<string> groups;
        private readonly List<Parameter_ShiftDemand> shiftDemand;

        public Constraint_FullfillDemand(
            List<Set_Date> dates,
            List<Set_Employee> employees,
            List<Set_Group> groups,
            List<Parameter_ShiftDemand> shiftDemand)
        {
            this.dates = dates.Select(row => row.Date).ToList();
            this.employees = employees.Select(row => row.Employee).ToList();
            this.groups = groups.Select(row => row.Group).ToList();
            this.shiftDemand = shiftDemand;
        }

        public void Build(OptEngine engine)
        {
            foreach (var date in dates)
                foreach (var group in groups)
                {
                    if (group == "O") continue;

                    foreach (var employee in employees)
                        engine.AddLHS(1.0, new VariableB_ShiftAssign { Date = date, Employee = employee, Group = group });

                    var demand = shiftDemand.FindParameterOrLog(
                        x => x.Date == date && x.Group == group,
                        date, group)?.QTY ?? 0.0;
                    engine.AddRHS(demand);
                    engine.CreateEqual(this, date, group);
                }
        }
    }
}
