using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>Capacity_{m,d,s}（小時，QTY）；索引為 Machine × Date × Shift。</summary>
    [OptParam]
    [OptDim<string>("Machine")]
    [OptDim<DateTime>("Date")]
    [OptDim<int>("Shift")]
    public sealed partial class Parameter_Capacity { }
}
