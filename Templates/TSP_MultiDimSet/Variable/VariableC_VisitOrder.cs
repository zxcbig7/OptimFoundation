using OptimFoundation.Modeling;

namespace TSP_MultiDimSet
{
    /// <summary>拜訪次序（MTZ 用）；對應 Model.md 的 VisitOrder_{Node} ≥ 0，僅在 CUSTOMER 上建立。</summary>
    [OptVar]
    [OptDim<string>("Node")]
    public sealed partial class VariableC_VisitOrder { }
}
