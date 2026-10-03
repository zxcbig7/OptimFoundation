using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>每個產品每天各班次是否開線（1 是、0 否），對應 Setup_{p,d,s}；VariableB_ 表示二元變數，依 Product × Date × Shift 建立。</summary>
    [OptVar]
    [OptDim<string>("Product")]
    [OptDim<DateTime>("Date")]
    [OptDim<int>("Shift")]
    public sealed partial class VariableB_Setup { }
}
