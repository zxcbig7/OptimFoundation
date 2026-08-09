using OptimFoundation.Modeling;

namespace FJSP_BASIC_BRICK
{
    /// <summary>加工批次；對應 Model.md 的 Lot。</summary>
    [OptSet]
    [OptDim<string>("Lot")]
    public sealed partial class Set_Lot { }
}
