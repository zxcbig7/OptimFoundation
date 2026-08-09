using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工 Employee 是否連續工作滿視窗天數（結束於 Date）；對應 Model.md 的 SixDayWork_{Date,Employee} ∈ {0,1}。</summary>
    [OptDim<DateTime>("Date")]
    [OptDim<string>("Employee")]
    [OptVar]
    public sealed partial class VariableB_SixDayWork { }
}
