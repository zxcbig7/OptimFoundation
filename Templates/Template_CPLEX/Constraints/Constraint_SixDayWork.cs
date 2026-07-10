using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class Constraint_SixDayWork : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<DateTime> _dates;
        private readonly List<string> _employees;

        public Constraint_SixDayWork(
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
            int duration = 6;

            _dates.ForEach(d =>
            {
                _employees.ForEach(e =>
                {
                    var dates = _dates.Where(sd => d.AddDays(-duration) < sd && sd <= d).ToList();
                    if (dates.Count < duration) return;

                    dates.ForEach(sd =>
                    {
                        _engine.AddLHS(1, new VariableB_SixDayWork { Date = d, Employee = e });
                        _engine.AddRHS(1);
                        _engine.AddRHS(-1, new VariableB_ShiftAssign { Date = sd, Employee = e, Group = "O" });
                        _engine.CreateLessEqual($"{ConstraintName}@{d:yyyy_MM_dd}@{e}");
                        ConstraintCount++;
                    });

                    _engine.AddLHS(1, new VariableB_SixDayWork { Date = d, Employee = e });
                    _engine.AddRHS(1);
                    dates.ForEach(sd =>
                        _engine.AddRHS(-1, new VariableB_ShiftAssign { Date = sd, Employee = e, Group = "O" }));
                    _engine.CreateGreatEqual($"{ConstraintName}@{d:yyyy_MM_dd}@{e}");
                    ConstraintCount++;
                });
            });

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
