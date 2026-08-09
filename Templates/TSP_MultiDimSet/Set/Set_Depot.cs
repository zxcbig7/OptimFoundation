using OptimFoundation.Modeling;

namespace TSP_MultiDimSet
{
    /// <summary>出發／返回節點；對應 Model.md 的 DEPOT。</summary>
    [OptSet]
    [OptDim<string>("Node")]
    public sealed partial class Set_Depot { }
}
