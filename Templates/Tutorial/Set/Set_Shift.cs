using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>班次；對應 Model.md 的 Shift。</summary>
    [OptSet]
    [OptDim<int>("Shift")]
    public sealed partial class Set_Shift { }
}
