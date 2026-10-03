using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>檢查相鄰兩天班別是否違規時，要查看幾天的排班；對應 Model.md 的 NightToDayWindow，由 Constraint_NightToDay 使用。</summary>
    [OptParam]
    public sealed partial class Parameter_NightToDayWindow { }
}
