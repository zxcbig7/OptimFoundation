using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>
    /// [Soft] Phase 3 demo variant 專用：期望 Makespan ≤ Target；違反量以 penalty 併入目標式（min Makespan + penalty·違反量）。
    /// MUST 在 ObjectiveFunction 之後建構——penalty 是加到「已存在的」目標式上。
    /// §4.5 天條：正式 Constraint_* 組裝只用 hard constraint API；本條 NEVER 進 canonical production 組裝，
    /// 只掛在 Program.cs 的 exp 模式另建的具名 OptModel variant（Canonical-SoftMakespanDemo）。
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
            engine.AddLHS(1.0, new VariableX_Makespan());
            engine.CreateLeSoft(_target, _penalty, ConstraintName);
        }
    }
}
