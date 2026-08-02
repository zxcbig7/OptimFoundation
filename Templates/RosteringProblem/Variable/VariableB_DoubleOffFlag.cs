using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工 Employee 在 Date 是否出現雙人連休型態；對應 Model.md 的 DoubleOffFlag_{Date,Employee} ∈ {0,1}。</summary>
    [OptDim<Set_Date>("Date")]
    [OptDim<Set_Employee>("Employee")]
    [OptVar]
    public sealed partial class VariableB_DoubleOffFlag { }
}
