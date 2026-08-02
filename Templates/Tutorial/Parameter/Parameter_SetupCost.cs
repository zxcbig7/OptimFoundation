using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>開線一次性成本（元）。數學：SetupCost_p，值在 QTY。1D 參數。</summary>
    [OptParam]
    [OptDim<Set_Product>("Product")]
    public sealed partial class Parameter_SetupCost { }
}
