using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>每日各班別需求人數；對應 Model.md 的 ShiftDemand_{Date,Group}。</summary>
    [OptParam]
    [OptDim<DateTime>("Date")]
    [OptDim<string>("Group")]
    public sealed partial class Parameter_ShiftDemand { }
}
