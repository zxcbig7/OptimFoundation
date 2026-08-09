using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工 Employee 的雙人連休次數是否超過門檻；對應 Model.md 的 DoubleOffLT2_{Employee} ∈ {0,1}。</summary>
    [OptDim<string>("Employee")]
    [OptVar]
    public sealed partial class VariableB_DoubleOffLT2 { }
}
