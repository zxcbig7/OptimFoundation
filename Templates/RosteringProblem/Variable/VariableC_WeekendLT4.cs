using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工 Employee 週末休假天數低於門檻的缺口量；對應 Model.md 的 WeekendLT4_{Employee} ≥ 0。</summary>
    [OptDim<string>("Employee")]
    [OptVar]
    public sealed partial class VariableC_WeekendLT4 { }
}
