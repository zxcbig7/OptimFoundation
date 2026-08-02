using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>avgOff = floor((|EMPLOYEE|·|DATE| - Σ_{shiftDemand.Group≠O} ShiftDemand) / |EMPLOYEE|) - 1
    /// ∀ employee ∈ EMPLOYEE：Σ_date ShiftAssign_{date,employee,O} + BelowAVG_{employee} ≥ avgOff</summary>
    public sealed class Constraint_BelowAVG : ConstraintBase
    {
        private readonly Set_Date dates;
        private readonly Set_Employee employees;
        private readonly List<Parameter_ShiftDemand> shiftDemand;

        public Constraint_BelowAVG(
            Set_Date dates,
            Set_Employee employees,
            List<Parameter_ShiftDemand> shiftDemand)
        {
            this.dates = dates;
            this.employees = employees;
            this.shiftDemand = shiftDemand;
        }

        public void Build(OptEngine engine)
        {
            double totalEmployees = employees.Count;
            double allShift = employees.Count * dates.Count;
            double allDemand = shiftDemand.Where(w => w.Group != "O").Sum(s => s.QTY);
            double avgOff = Math.Floor((allShift - allDemand) / totalEmployees) - 1;

            foreach (var employee in employees)
            {
                foreach (var date in dates)
                    engine.AddLHS(1.0, new VariableB_ShiftAssign { Date = date, Employee = employee, Group = "O" });

                engine.AddLHS(1.0, new VariableX_BelowAVG { Employee = employee });
                engine.AddRHS(avgOff);
                engine.CreateGreatEqual($"{ConstraintName}@{employee}");
            }
        }
    }
}
