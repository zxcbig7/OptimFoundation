using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>
    /// 示範無可行解時使用的完工上限：Makespan ≤ InfeasibleMakespanCap（= 理論下界 − 1）。
    /// 上限嚴格低於任何可行 makespan，保證 Infeasible，用來觸發 CPLEX conflict 分析並輸出 IIS（IISs/*.ilp）。
    /// 這條限制只供 Phase 3 實驗示範，不加入正式求解模型。
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
