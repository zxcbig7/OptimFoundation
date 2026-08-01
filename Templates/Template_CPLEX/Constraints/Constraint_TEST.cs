using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.Data;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class Constraint_TEST : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<DateTime> _dates;
        private readonly List<string> _employees;
        private readonly List<Parameter_ShiftDemand> _shiftDemand;

        public Constraint_TEST(
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
            _employees.ForEach(e =>
            {
                _dates.ForEach(d =>
                    _engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "O" }));
                _engine.AddRHS(-1);
                _engine.CreateLessEqual($"{ConstraintName}@{e}");
            });

        }
    }
}
