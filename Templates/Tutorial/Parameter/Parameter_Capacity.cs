using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>每機每日每班工時上限（小時）。數學：Capacity_{m,d,s}，值在 QTY。3D 參數（Machine × Date × Shift，含 DateTime + int 維度）。</summary>
    [OptParam]
    [OptDim<string>("Machine")]
    [OptDim<DateTime>("Date")]
    [OptDim<int>("Shift")]
    public sealed partial class Parameter_Capacity { }
}
