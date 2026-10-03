using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>
    /// Phase 3 示範的完工時間目標：期望 Makespan ≤ Target；允許超過，但超過的時間要加罰分（min Makespan + penalty·違反量）。
    /// 必須先建立 ObjectiveFunction，才能將這條限制的罰分加到目標式。
    /// 依 §4.5，正式模型只使用必須滿足的限制式；這條允許違反的限制不加入正式模型，
    /// 只在 Program.cs 的 exp 模式中，加入名為 Canonical-SoftMakespanDemo 的實驗模型。
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
