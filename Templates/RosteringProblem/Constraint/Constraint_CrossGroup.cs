using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>∀ date ∈ DATE, employee ∈ EMPLOYEE, group ∈ CrossGroup(employee)：
    /// ShiftAssign_{date,employee,group} ≤ GroupMismatch_{date,employee}</summary>
    public sealed class Constraint_CrossGroup : ConstraintBase
    {
        private readonly Set_Date dates;
        private readonly Set_Employee employees;
        private readonly List<Parameter_CrossGroup> crossGroup;

        public Constraint_CrossGroup(
            Set_Date dates,
            Set_Employee employees,
            List<Parameter_CrossGroup> crossGroup)
        {
            this.dates = dates;
            this.employees = employees;
            this.crossGroup = crossGroup;
        }

        public void Build(OptEngine engine)
        {
            foreach (var date in dates)
                foreach (var employee in employees)
                {
                    var rules = crossGroup.Where(p => p.Employee == employee).ToList();

                    foreach (var rule in rules)
                    {
                        engine.AddLHS(1.0, new VariableB_ShiftAssign { Date = date, Employee = employee, Group = rule.Group });
                        engine.AddRHS(1.0, new VariableB_GroupMismatch { Date = date, Employee = employee });
                        engine.CreateLessEqual($"{ConstraintName}@{date:yyyy_MM_dd}@{employee}@{rule.Group}");
                    }
                }
        }
    }
}
