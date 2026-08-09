using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工 Employee 在 Date 前一天是否為「做休做」單日離峰型態；對應 Model.md 的 Off1Day_{Date,Employee} ∈ {0,1}。</summary>
    [OptDim<DateTime>("Date")]
    [OptDim<string>("Employee")]
    [OptVar]
    public sealed partial class VariableB_Off1Day { }
}
