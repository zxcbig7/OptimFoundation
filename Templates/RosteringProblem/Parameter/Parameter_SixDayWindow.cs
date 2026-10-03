using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>連續工作滿幾天就要計入罰分；對應 Model.md 的 SixDayWindow，由 Constraint_SixDayWork 使用。</summary>
    [OptParam]
    public sealed partial class Parameter_SixDayWindow { }
}
