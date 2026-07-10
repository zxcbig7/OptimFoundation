using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class Constraint_WeekendLT4 : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<DateTime> _dates;
        private readonly List<string> _employees;

        public Constraint_WeekendLT4(
            List<DateTime> dates,
            List<string> employees,
            OptEngine engine)
        {
            _dates = dates;
            _employees = employees;
            _engine = engine;
        }

        public void Build()
        {
            var weekends = _dates
                .Where(d => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
                .ToList();

            _employees.ForEach(e =>
            {
                _engine.AddLHS(1, new VariableX_WeekendLT4 { Employee = e });
                _engine.AddRHS(4);
                weekends.ForEach(d =>
                    _engine.AddRHS(-1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "O" }));
                _engine.CreateGreatEqual($"{ConstraintName}@{e}");
                ConstraintCount++;
            });

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
