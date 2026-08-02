using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>∀ date ∈ DATE, group ∈ GROUP \ {O}：Σ_employee ShiftAssign_{date,employee,group} = ShiftDemand_{date,group}</summary>
    public sealed class Constraint_FullfillDemand : ConstraintBase
    {
        private readonly Set_Date dates;
        private readonly Set_Employee employees;
        private readonly Set_Group groups;
        private readonly List<Parameter_ShiftDemand> shiftDemand;

        public Constraint_FullfillDemand(
            Set_Date dates,
            Set_Employee employees,
            Set_Group groups,
            List<Parameter_ShiftDemand> shiftDemand)
        {
            this.dates = dates;
            this.employees = employees;
            this.groups = groups;
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

                    var demand = shiftDemand.FirstOrDefault(x => x.Date == date && x.Group == group)?.QTY ?? 0.0;
                    engine.AddRHS(demand);
                    engine.CreateEqual($"{ConstraintName}@{date:yyyy_MM_dd}@{group}");
                }
        }
    }
}
