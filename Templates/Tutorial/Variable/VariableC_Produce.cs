using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>生產量（件）≥ 0。數學：Produce_{p,d,s}。前綴 C_ = Continuous。3D 變數（Product × Date × Shift）。</summary>
    [OptVar]
    [OptDim<string>("Product")]
    [OptDim<DateTime>("Date")]
    [OptDim<int>("Shift")]
    public sealed partial class VariableC_Produce { }
}
