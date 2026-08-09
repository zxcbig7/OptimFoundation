using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>規劃期間的每一生產日；對應 Model.md 的 Date。</summary>
    [OptSet]
    [OptDim<DateTime>("Date")]
    public sealed partial class Set_Date { }
}
