using OptimFoundation.Modeling;
using Tutorial.SetClass;

namespace Tutorial.VariableClass
{
    /// <summary>生產量（件）≥ 0。數學：Produce_{p,d,s}。前綴 X_ = continuous。3D 變數（Product × Date × Shift）。</summary>
    [OptVar]
    [OptDim<Set_Product>("Product")]
    [OptDim<Set_Date>("Date")]
    [OptDim<Set_Shift>("Shift")]
    public partial class VariableX_Produce { }
}
