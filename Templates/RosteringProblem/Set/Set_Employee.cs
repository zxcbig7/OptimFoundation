using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>可排班的員工；對應 Model.md 的 EMPLOYEE。</summary>
    [OptSet]
    [OptDim<string>("Employee")]
    public sealed partial class Set_Employee { }
}
