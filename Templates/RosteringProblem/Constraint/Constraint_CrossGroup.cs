using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>∀ date ∈ DATE, employee ∈ EMPLOYEE, group ∈ CrossGroup(employee)：
    /// ShiftAssign_{date,employee,group} ≤ GroupMismatch_{date,employee}</summary>
    public sealed class Constraint_CrossGroup : ConstraintBase
    {
        private readonly List<DateTime> dates;
        private readonly List<string> employees;
        private readonly List<Parameter_CrossGroup> crossGroup;

        public Constraint_CrossGroup(
            List<Set_Date> dates,
            List<Set_Employee> employees,
            List<Parameter_CrossGroup> crossGroup)
        {
            this.dates = dates.Select(row => row.Date).ToList();
            this.employees = employees.Select(row => row.Employee).ToList();
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
                        engine.CreateLessEqual(this, date, employee, rule.Group);
                    }
                }
        }
    }
}
