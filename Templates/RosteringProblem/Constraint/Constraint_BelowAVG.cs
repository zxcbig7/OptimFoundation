using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>avgOff = floor((|EMPLOYEE|·|DATE| - Σ_{shiftDemand.Group≠O} ShiftDemand) / |EMPLOYEE|) - 1
    /// ∀ employee ∈ EMPLOYEE：Σ_date ShiftAssign_{date,employee,O} + BelowAVG_{employee} ≥ avgOff</summary>
    public sealed class Constraint_BelowAVG : ConstraintBase
    {
        private readonly List<DateTime> dates;
        private readonly List<string> employees;
        private readonly List<Parameter_ShiftDemand> shiftDemand;

        public Constraint_BelowAVG(
            List<Set_Date> dates,
            List<Set_Employee> employees,
            List<Parameter_ShiftDemand> shiftDemand)
        {
            this.dates = dates.Select(row => row.Date).ToList();
            this.employees = employees.Select(row => row.Employee).ToList();
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

                engine.AddLHS(1.0, new VariableC_BelowAVG { Employee = employee });
                engine.AddRHS(avgOff);
                engine.CreateGreatEqual(this, employee);
            }
        }
    }
}
