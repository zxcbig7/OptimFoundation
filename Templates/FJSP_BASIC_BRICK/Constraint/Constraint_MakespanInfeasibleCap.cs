using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>
    /// [UB] Phase 3 demo variant 專用：Makespan ≤ InfeasibleMakespanCap（= 理論下界 − 1）。
    /// 上限嚴格低於任何可行 makespan，保證 Infeasible，用來觸發 CPLEX conflict 分析並輸出 IIS（IISs/*.ilp）。
    /// 非業務限制，NEVER 進 canonical production 組裝。
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
