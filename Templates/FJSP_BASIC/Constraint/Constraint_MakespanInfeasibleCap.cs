using FJSP_BASIC.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC.Constraint
{
    /// <summary>
    /// [LE] 保證 infeasible 的 Makespan 硬上限：Makespan ≤ InfeasibleMakespanCap（= 理論下界 - 1）。
    /// 只給模型 C 用，目的是觸發 CPLEX conflict 分析、示範 IIS 輸出（IISs/*.ilp），不是真實業務限制。
    /// </summary>
    public class Constraint_MakespanInfeasibleCap : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<string> _scope;
        private readonly double _cap;

        public Constraint_MakespanInfeasibleCap(List<string> scope, double cap, OptEngine engine)
        {
            _scope = scope;
            _cap = cap;
            _engine = engine;
        }

        public void Build()
        {
            var scope = _scope[0];
            _engine.AddLHS(1.0, new VariableX_Makespan { Scope = scope });
            _engine.AddRHS(_cap);
            _engine.CreateLessEqual($"{ConstraintName}@{scope}");
            ConstraintCount++;

            Logging.Info($"[{ConstraintName}] {ConstraintCount}  Makespan ≤ {_cap}（保證 infeasible）");
        }
    }
}
