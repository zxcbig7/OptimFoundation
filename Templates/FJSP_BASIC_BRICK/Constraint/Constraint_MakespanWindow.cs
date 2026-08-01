using FJSP_BASIC_BRICK.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK.Constraint
{
    /// <summary>[Range] 規劃窗：MakespanFloor ≤ Makespan ≤ MakespanDeadline（示範 CreateRange，demo 值非綁定）</summary>
    public class Constraint_MakespanWindow : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly double _floor;
        private readonly double _deadline;

        public Constraint_MakespanWindow(double floor, double deadline, OptEngine engine)
        {
            _floor = floor;
            _deadline = deadline;
            _engine = engine;
        }

        public void Build()
        {
            _engine.AddLHS(1.0, new VariableX_Makespan());
            _engine.CreateRange(_floor, _deadline, ConstraintName);
            Logging.Info($"[限制式設定] group={ConstraintName} floor={_floor} deadline={_deadline}");
        }
    }
}
