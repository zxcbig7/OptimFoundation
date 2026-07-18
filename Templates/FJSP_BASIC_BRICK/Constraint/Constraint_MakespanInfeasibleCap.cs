using FJSP_BASIC_BRICK.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK.Constraint
{
    /// <summary>
    /// [LE] 保證 infeasible 的 Makespan 硬上限：Makespan ≤ InfeasibleMakespanCap（= 理論下界 - 1）。
    /// 只給模型 C 用，目的是觸發 CPLEX conflict 分析、示範 IIS 輸出（IISs/*.ilp），不是真實業務限制。
    /// </summary>
    public class Constraint_MakespanInfeasibleCap : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly double _cap;

        public Constraint_MakespanInfeasibleCap(double cap, OptEngine engine)
        {
            _cap = cap;
            _engine = engine;
        }

        public void Build()
        {
            _engine.AddLHS(1.0, new VariableX_Makespan());
            _engine.AddRHS(_cap);
            _engine.CreateLessEqual(ConstraintName);
            ConstraintCount++;

            Logging.Info($"[{ConstraintName}] {ConstraintCount}  Makespan ≤ {_cap}（保證 infeasible）");
        }
    }
}
