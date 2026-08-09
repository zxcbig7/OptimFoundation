using OptimFoundation.Modeling;

namespace FJSP_BASIC_BRICK
{
    /// <summary>作業完成時間（小時）；對應 Model.md 的 Complete_{Lot,Operation}。</summary>
    [OptVar]
    [OptDim<string>("Lot")]
    [OptDim<string>("Operation")]
    public sealed partial class VariableC_Complete { }
}
