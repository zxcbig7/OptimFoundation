using FJSP_BASIC_BRICK.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK.Constraint
{
    /// <summary>
    /// [Soft] 期望 Makespan ≤ Target；違反量以 penalty 併入目標式（min Makespan + penalty·違反量）。
    /// MUST 在 ObjectiveFunction 之後建構——penalty 是加到「已存在的」目標式上。
    /// </summary>
    public class Constraint_MakespanTargetSoft : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly double _target;
        private readonly double _penalty;

        public Constraint_MakespanTargetSoft(double target, double penalty, OptEngine engine)
        {
            _target = target;
            _penalty = penalty;
            _engine = engine;
        }

        public void Build()
        {
            _engine.AddLHS(1.0, new VariableX_Makespan());
            _engine.CreateLeSoft(_target, _penalty);
            ConstraintCount++;

            Logging.Info($"[{ConstraintName}] target={_target} penalty={_penalty}");
        }
    }
}
