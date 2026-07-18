using OptimFoundation.Modeling;
using Tutorial.SetClass;

namespace Tutorial.ParameterClass
{
    /// <summary>每日需求下限（件）。數學：Demand_{p,d}，值在 QTY。2D 參數（Product × Date，含 DateTime 維度）。</summary>
    [OptParam]
    [OptDim<Set_Product>("Product")]
    [OptDim<Set_Date>("Date")]
    public partial class Parameter_Demand { }
}
