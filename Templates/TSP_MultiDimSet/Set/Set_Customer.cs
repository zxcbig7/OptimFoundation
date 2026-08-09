using OptimFoundation.Modeling;

namespace TSP_MultiDimSet
{
    /// <summary>必須拜訪的節點；對應 Model.md 的 CUSTOMER。</summary>
    [OptSet]
    [OptDim<string>("Node")]
    public sealed partial class Set_Customer { }
}
