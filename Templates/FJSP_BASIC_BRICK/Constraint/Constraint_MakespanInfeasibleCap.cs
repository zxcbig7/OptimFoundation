using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>
    /// Makespan ≤ 理論下界 − 1，保證無解以示範 IIS 分析；僅供實驗。
    /// </summary>
    public sealed class Constraint_MakespanInfeasibleCap : ConstraintBase
    {
        private readonly double _cap;

        public Constraint_MakespanInfeasibleCap(double cap)
        {
            _cap = cap;
        }

        public void Build(OptEngine engine)
        {
            engine.AddLHS(1.0, new VariableC_Makespan());
            engine.AddRHS(_cap);
            engine.CreateLessEqual(this);
        }
    }
}
