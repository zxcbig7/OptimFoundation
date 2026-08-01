using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class Constraint_DoubleOffLT2 : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<DateTime> _dates;
        private readonly List<string> _employees;

        public Constraint_DoubleOffLT2(
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
            int duration = 3;

            _dates.ForEach(d =>
            {
                var dates = _dates.Where(sd => d.AddDays(-duration) < sd && sd <= d).ToList();
                if (dates.Count < 2) return;

                _employees.ForEach(e =>
                {
                    if (dates.Count == 2)
                    {
                        var preD = d.AddDays(-1);
                        _engine.AddLHS(1, new VariableB_DoubleOffFlag { Date = d, Employee = e });
                        _engine.AddRHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "O" });
                        _engine.AddRHS(1, new VariableB_ShiftAssign { Date = preD, Employee = e, Group = "O" });
                        _engine.AddRHS(-(2 - 1));
                    }
                    else if (dates.Count == 3)
                    {
                        var preD = d.AddDays(-1);
                        var prepreD = d.AddDays(-2);
                        _engine.AddLHS(1, new VariableB_DoubleOffFlag { Date = d, Employee = e });
                        _engine.AddRHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "O" });
                        _engine.AddRHS(1, new VariableB_ShiftAssign { Date = preD, Employee = e, Group = "O" });
                        _engine.AddRHS(1);
                        _engine.AddRHS(-1, new VariableB_ShiftAssign { Date = prepreD, Employee = e, Group = "O" });
                        _engine.AddRHS(-(3 - 1));
                    }

                    _engine.CreateGreatEqual($"{ConstraintName}_a@{d:yyyy_MM_dd}@{e}");
                });
            });

            _employees.ForEach(e =>
            {
                _dates.ForEach(d =>
                    _engine.AddLHS(1, new VariableB_DoubleOffFlag { Date = d, Employee = e }));
                _engine.AddLHS(2, new VariableB_DoubleOffLT2 { Employee = e });
                _engine.AddRHS(2);
                _engine.CreateGreatEqual($"{ConstraintName}_b@{e}");
            });

        }
    }
}
