using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>可生產的產品；對應 Model.md 的 Product。</summary>
    [OptSet]
    [OptDim<string>("Product")]
    public sealed partial class Set_Product { }
}
