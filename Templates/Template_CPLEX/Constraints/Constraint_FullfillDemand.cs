using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.Data;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class Constraint_FullfillDemand : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<DateTime> _dates;
        private readonly List<string> _employees;
        private readonly List<string> _groups;
        private readonly List<Parameter_ShiftDemand> _shiftDemand;

        public Constraint_FullfillDemand(
            List<DateTime> dates,
            List<string> employees,
            List<string> groups,
            List<Parameter_ShiftDemand> shiftDemand,
            OptEngine engine)
        {
            _dates = dates;
            _employees = employees;
            _groups = groups;
            _shiftDemand = shiftDemand;
            _engine = engine;
        }

        public void Build()
        {
            _dates.ForEach(d =>
            {
                _groups.Where(g => g != "O").ToList().ForEach(g =>
                {
                    _employees.ForEach(e =>
                        _engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = g }));

                    double demand = _shiftDemand.FirstOrDefault(x => x.Date == d && x.Group == g)?.QTY ?? 0;
                    _engine.AddRHS(demand);
                    _engine.CreateEqual($"{ConstraintName}@{d:yyyy_MM_dd}@{g}");
                });
            });

        }
    }
}
