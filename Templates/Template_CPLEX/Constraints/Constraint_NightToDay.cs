using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.Data;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class Constraint_NightToDay : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<DateTime> _dates;
        private readonly List<string> _employees;
        private readonly List<Parameter_NightToDay> _nightToDay;

        public Constraint_NightToDay(
            List<DateTime> dates,
            List<string> employees,
            List<Parameter_NightToDay> nightToDay,
            OptEngine engine)
        {
            _dates = dates;
            _employees = employees;
            _nightToDay = nightToDay;
            _engine = engine;
        }

        public void Build()
        {
            int duration = 2;

            _dates.ForEach(d =>
            {
                _employees.ForEach(e =>
                {
                    var dates = _dates.Where(sd => d.AddDays(-duration) < sd && sd <= d).ToList();
                    if (dates.Count < duration) return;

                    var preD = d.AddDays(-1);

                    _nightToDay.ForEach(rule =>
                    {
                        _engine.AddLHS(1, new VariableB_NightToDay { Date = d, Employee = e });
                        _engine.AddRHS(1, new VariableB_ShiftAssign { Date = preD, Employee = e, Group = rule.PreGroup });
                        _engine.AddRHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = rule.Group });
                        _engine.AddRHS(-1);
                        _engine.CreateGreatEqual($"{ConstraintName}@{d:yyyy_MM_dd}@{e}");
                        ConstraintCount++;
                    });
                });
            });

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
