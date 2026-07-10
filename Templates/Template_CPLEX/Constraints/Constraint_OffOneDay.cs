using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class Constraint_OffOneDay : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<DateTime> _dates;
        private readonly List<string> _employees;

        public Constraint_OffOneDay(
            List<DateTime> dates,
            List<string> employees,
            OptEngine engine)
        {
            _dates = dates;
            _employees = employees;
            _engine = engine;
        }

        /// <summary>做休做</summary>
        public void Build()
        {
            int duration = 3;

            _dates.ForEach(d =>
            {
                _employees.ForEach(e =>
                {
                    var dates = _dates.Where(sd => d.AddDays(-duration) < sd && sd <= d).ToList();
                    if (dates.Count < duration) return;

                    var preD = d.AddDays(-1);
                    var prepreD = d.AddDays(-2);

                    _engine.AddLHS(1, new VariableB_Off1Day { Date = d, Employee = e });

                    _engine.AddRHS(1);
                    _engine.AddRHS(-1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "O" });
                    _engine.AddRHS(1, new VariableB_ShiftAssign { Date = preD, Employee = e, Group = "O" });
                    _engine.AddRHS(1);
                    _engine.AddRHS(-1, new VariableB_ShiftAssign { Date = prepreD, Employee = e, Group = "O" });
                    _engine.AddRHS(-(duration - 1));

                    _engine.CreateGreatEqual($"{ConstraintName}@{d:yyyy_MM_dd}@{e}");
                    ConstraintCount++;
                });
            });

            Logging.Info($"{ConstraintName} ，共：{ConstraintCount}條");
        }
    }
}
