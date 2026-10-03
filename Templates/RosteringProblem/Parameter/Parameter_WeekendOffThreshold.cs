using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工週末休假的目標天數，不足時由 WeekendLT4 記錄差額；對應 Model.md 的 WeekendOffThreshold。</summary>
    [OptParam]
    public sealed partial class Parameter_WeekendOffThreshold { }
}
