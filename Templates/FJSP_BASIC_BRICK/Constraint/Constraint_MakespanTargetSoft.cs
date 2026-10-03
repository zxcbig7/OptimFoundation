using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>
    /// 軟性目標 Makespan ≤ Target，違反量乘 penalty 加入目標式；須先建立 ObjectiveFunction。
    /// 僅加入 Canonical-SoftMakespanDemo 實驗，不用於正式模型。
    /// </summary>
    public sealed class Constraint_MakespanTargetSoft : ConstraintBase
    {
        private readonly double _target;
        private readonly double _penalty;

        public Constraint_MakespanTargetSoft(double target, double penalty)
        {
            _target = target;
            _penalty = penalty;
        }

        public void Build(OptEngine engine)
        {
            engine.AddLHS(1.0, new VariableC_Makespan());
            engine.CreateLessEqualSoft(_target, _penalty, this);
        }
    }
}
