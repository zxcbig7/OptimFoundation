using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>標記員工 Employee 在 Date 前一天只休一天，前後兩天都有上班的「做休做」情況；對應 Model.md 的 Off1Day_{Date,Employee} ∈ {0,1}。</summary>
    [OptDim<DateTime>("Date")]
    [OptDim<string>("Employee")]
    [OptVar]
    public sealed partial class VariableB_Off1Day { }
}
