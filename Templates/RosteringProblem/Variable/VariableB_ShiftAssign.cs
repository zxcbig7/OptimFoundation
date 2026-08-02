using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工 Employee 在 Date 是否排入班別 Group；對應 Model.md 的 ShiftAssign_{Date,Employee,Group} ∈ {0,1}。</summary>
    [OptDim<Set_Date>("Date")]
    [OptDim<Set_Employee>("Employee")]
    [OptDim<Set_Group>("Group")]
    [OptVar]
    public sealed partial class VariableB_ShiftAssign { }
}
