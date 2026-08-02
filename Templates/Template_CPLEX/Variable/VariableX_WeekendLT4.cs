using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工 Employee 週末休假天數低於門檻的缺口量；對應 Model.md 的 WeekendLT4_{Employee} ≥ 0。</summary>
    [OptDim<Set_Employee>("Employee")]
    [OptVar]
    public sealed partial class VariableX_WeekendLT4 { }
}
