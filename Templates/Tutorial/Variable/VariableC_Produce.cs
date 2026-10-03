using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>每個產品每天各班次的產量（件，可為非負小數），對應 Produce_{p,d,s}；VariableC_ 表示連續變數，依 Product × Date × Shift 建立。</summary>
    [OptVar]
    [OptDim<string>("Product")]
    [OptDim<DateTime>("Date")]
    [OptDim<int>("Shift")]
    public sealed partial class VariableC_Produce { }
}
