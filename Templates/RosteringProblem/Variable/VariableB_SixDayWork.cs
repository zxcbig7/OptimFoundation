using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工 Employee 截至 Date 是否已連續工作滿 SixDayWindow 天；對應 Model.md 的 SixDayWork_{Date,Employee} ∈ {0,1}。</summary>
    [OptDim<DateTime>("Date")]
    [OptDim<string>("Employee")]
    [OptVar]
    public sealed partial class VariableB_SixDayWork { }
}
