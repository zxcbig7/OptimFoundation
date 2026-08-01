using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class Constraint_OneGroup : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<DateTime> _dates;
        private readonly List<string> _employees;
        private readonly List<string> _groups;

        public Constraint_OneGroup(
            List<DateTime> dates,
            List<string> employees,
            List<string> groups,
            OptEngine engine)
        {
            _dates = dates;
            _employees = employees;
            _groups = groups;
            _engine = engine;
        }

        public void Build()
        {
            _dates.ForEach(d =>
            {
                _employees.ForEach(e =>
                {
                    _groups.ForEach(g =>
                        _engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = g }));

                    _engine.AddRHS(1);
                    _engine.CreateEqual($"{ConstraintName}@{d:yyyy_MM_dd}@{e}");
                });
            });

        }
    }
}
