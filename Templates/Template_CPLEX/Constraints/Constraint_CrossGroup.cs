using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.Data;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class Constraint_CrossGroup : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<DateTime> _dates;
        private readonly List<string> _employees;
        private readonly List<Parameter_CrossGroup> _crossGroup;

        public Constraint_CrossGroup(
            List<DateTime> dates,
            List<string> employees,
            List<Parameter_CrossGroup> crossGroup,
            OptEngine engine)
        {
            _dates = dates;
            _employees = employees;
            _crossGroup = crossGroup;
            _engine = engine;
        }

        public void Build()
        {
            _dates.ForEach(d =>
            {
                _employees.ForEach(e =>
                {
                    var rules = _crossGroup.Where(p => p.Employee == e).ToList();

                    rules.ForEach(g =>
                    {
                        _engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = g.Group });
                        _engine.AddRHS(1, new VariableB_GroupMismatch { Date = d, Employee = e });
                        _engine.CreateLessEqual($"{ConstraintName}@{d:yyyy_MM_dd}@{e}s");
                        ConstraintCount++;
                    });
                });
            });

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
