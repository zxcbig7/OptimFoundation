using OptimFoundation.Modeling;

namespace FJSP_BASIC_BRICK
{
    /// <summary>作業開始時間（小時）；對應 Model.md 的 Start_{Lot,Operation}。</summary>
    [OptVar]
    [OptDim<string>("Lot")]
    [OptDim<string>("Operation")]
    public sealed partial class VariableC_Start { }
}
