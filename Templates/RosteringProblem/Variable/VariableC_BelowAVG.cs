using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工 Employee 休假天數低於平均目標的缺口量；對應 Model.md 的 BelowAVG_{Employee} ≥ 0。</summary>
    [OptDim<string>("Employee")]
    [OptVar]
    public sealed partial class VariableC_BelowAVG { }
}
