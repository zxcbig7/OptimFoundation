using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>當 Employee 的 DoubleOffFlag 總數未達門檻時，此值必須為 1 以補足限制式，並計入罰分；對應 Model.md 的 DoubleOffLT2_{Employee} ∈ {0,1}。</summary>
    [OptDim<string>("Employee")]
    [OptVar]
    public sealed partial class VariableB_DoubleOffLT2 { }
}
