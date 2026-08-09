using OptimFoundation.Modeling;

namespace FJSP_BASIC_BRICK
{
    /// <summary>加工時間（小時）；對應 Model.md 的 ProcessTime_{Lot,Operation,Eqp}。</summary>
    [OptParam]
    [OptDim<string>("Lot")]
    [OptDim<string>("Operation")]
    [OptDim<string>("Eqp")]
    public sealed partial class Parameter_ProcessTime { }
}
