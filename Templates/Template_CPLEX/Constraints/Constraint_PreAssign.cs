using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.Data;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class Constraint_PreAssign : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<Parameter_PreAssign> _preAssign;

        public Constraint_PreAssign(
            List<Parameter_PreAssign> preAssign,
            OptEngine engine)
        {
            _preAssign = preAssign;
            _engine = engine;
        }

        public void Build()
        {
            _preAssign.ForEach(p =>
            {
                _engine.AddLHS(1, new VariableB_ShiftAssign { Date = p.Date, Employee = p.Employee, Group = p.Group });
                _engine.AddRHS(1);
                _engine.CreateEqual($"{ConstraintName}@{p.Date:yyyy_MM_dd}@{p.Employee}@{p.Group}");
                ConstraintCount++;
            });

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
