using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>每日生產批數 ≥ 0 整數。數學：Batch_{p,d}。前綴 I_ = integer。2D 變數（Product × Date）。</summary>
    [OptVar]
    [OptDim<string>("Product")]
    [OptDim<DateTime>("Date")]
    public sealed partial class VariableI_Batch { }
}
