using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>[Range] 規劃窗：MakespanFloor ≤ Makespan ≤ MakespanDeadline（示範 CreateRange，demo 值非綁定）</summary>
    public sealed class Constraint_MakespanWindow : ConstraintBase
    {
        private readonly double _floor;
        private readonly double _deadline;

        public Constraint_MakespanWindow(double floor, double deadline)
        {
            _floor = floor;
            _deadline = deadline;
        }

        public void Build(OptEngine engine)
        {
            engine.AddLHS(1.0, new VariableX_Makespan());
            engine.CreateRange(_floor, _deadline, ConstraintName);
        }
    }
}
