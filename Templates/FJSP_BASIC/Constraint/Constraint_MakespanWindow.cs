using FJSP_BASIC.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC.Constraint
{
    /// <summary>[Range] 規劃窗：MakespanFloor ≤ Makespan ≤ MakespanDeadline（示範 CreateRange，demo 值非綁定）</summary>
    public class Constraint_MakespanWindow : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<string> _scope;
        private readonly double _floor;
        private readonly double _deadline;

        public Constraint_MakespanWindow(List<string> scope, double floor, double deadline, OptEngine engine)
        {
            _scope = scope;
            _floor = floor;
            _deadline = deadline;
            _engine = engine;
        }

        public void Build()
        {
            var scope = _scope[0];
            _engine.AddLHS(1.0, new VariableX_Makespan { Scope = scope });
            _engine.CreateRange(_floor, _deadline, $"{ConstraintName}@{scope}");
            ConstraintCount++;

            Logging.Info($"[{ConstraintName}] {ConstraintCount}  [{_floor}, {_deadline}]");
        }
    }
}
