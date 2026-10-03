using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>檢查「上班、休一天、再上班」時，要查看幾天的排班；對應 Model.md 的 OffOneDayWindow，由 Constraint_OffOneDay 使用。</summary>
    [OptParam]
    public sealed partial class Parameter_OffOneDayWindow { }
}
