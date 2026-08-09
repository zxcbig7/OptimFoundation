using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>生產量（件）≥ 0。數學：Produce_{p,d,s}。前綴 X_ = continuous。3D 變數（Product × Date × Shift）。</summary>
    [OptVar]
    [OptDim<string>("Product")]
    [OptDim<DateTime>("Date")]
    [OptDim<int>("Shift")]
    public sealed partial class VariableX_Produce { }
}
