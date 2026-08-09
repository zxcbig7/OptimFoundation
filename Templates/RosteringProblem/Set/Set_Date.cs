using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>排班期間內的每一天；對應 Model.md 的 DATE。</summary>
    [OptSet]
    [OptDim<DateTime>("Date")]
    public sealed partial class Set_Date { }
}
