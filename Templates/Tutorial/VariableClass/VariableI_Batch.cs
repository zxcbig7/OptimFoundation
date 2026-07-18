using OptimFoundation.Modeling;
using Tutorial.SetClass;

namespace Tutorial.VariableClass
{
    /// <summary>每日生產批數 ≥ 0 整數。數學：Batch_{p,d}。前綴 I_ = integer。2D 變數（Product × Date）。</summary>
    [OptVar]
    [OptDim<Set_Product>("Product")]
    [OptDim<Set_Date>("Date")]
    public partial class VariableI_Batch { }
}
