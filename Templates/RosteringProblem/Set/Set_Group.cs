using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>班別群組（含 Off 班 "O"）；對應 Model.md 的 GROUP。</summary>
    [OptSet]
    [OptDim<string>("Group")]
    public sealed partial class Set_Group { }
}
