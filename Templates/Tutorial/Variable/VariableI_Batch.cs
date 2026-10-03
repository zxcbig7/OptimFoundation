using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>每個產品每天的生產批數（非負整數），對應 Batch_{p,d}；VariableI_ 表示整數，依 Product × Date 建立。</summary>
    [OptVar]
    [OptDim<string>("Product")]
    [OptDim<DateTime>("Date")]
    public sealed partial class VariableI_Batch { }
}
