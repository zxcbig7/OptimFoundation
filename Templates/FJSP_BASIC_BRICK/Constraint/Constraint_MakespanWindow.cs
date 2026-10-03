using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>限制完工時間範圍：MakespanFloor ≤ Makespan ≤ MakespanDeadline；使用寬鬆界限示範 CreateRange，不額外排除可行排程。</summary>
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
            engine.AddLHS(1.0, new VariableC_Makespan());
            engine.CreateRange(_floor, _deadline, this);
        }
    }
}
