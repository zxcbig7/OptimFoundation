using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.Data;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class Constraint_BelowAVG : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<DateTime> _dates;
        private readonly List<string> _employees;
        private readonly List<Parameter_ShiftDemand> _shiftDemand;

        public Constraint_BelowAVG(
            List<DateTime> dates,
            List<string> employees,
            List<Parameter_ShiftDemand> shiftDemand,
            OptEngine engine)
        {
            _dates = dates;
            _employees = employees;
            _shiftDemand = shiftDemand;
            _engine = engine;
        }

        public void Build()
        {
            double totalEMP = _employees.Count;
            double allShift = _employees.Count * _dates.Count;
            double allDemand = _shiftDemand.Where(w => w.Group != "O").Sum(s => s.QTY);
            double avgOff = Math.Floor((allShift - allDemand) / totalEMP) - 1;

            _employees.ForEach(e =>
            {
                _dates.ForEach(d =>
                    _engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "O" }));

                _engine.AddLHS(1, new VariableX_BelowAVG { Employee = e });
                _engine.AddRHS(avgOff);
                _engine.CreateGreatEqual($"{ConstraintName}@{e}");
                ConstraintCount++;
            });

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
