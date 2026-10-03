using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工連休兩天的目標次數；對應 Model.md 的 DoubleOffThreshold，由 Constraint_DoubleOffLT2 使用。</summary>
    [OptParam]
    public sealed partial class Parameter_DoubleOffThreshold { }
}
