using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>共用常數 1，供 OneGroup、PreAssign、SixDayWork、
    /// NightToDay、OffOneDay、DoubleOffLT2 等限制式計數，或把「多個條件同時成立」寫成線性限制；對應 Model.md 的 One。</summary>
    [OptParam]
    public sealed partial class Parameter_One { }
}
