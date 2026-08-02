using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>結構常數「恰好一次／固定為 1」的共用值，供 OneGroup、PreAssign、SixDayWork、
    /// NightToDay、OffOneDay、DoubleOffLT2 等限制式的 AND-linearization 使用；對應 Model.md 的 One。</summary>
    [OptParam]
    public sealed partial class Parameter_One { }
}
