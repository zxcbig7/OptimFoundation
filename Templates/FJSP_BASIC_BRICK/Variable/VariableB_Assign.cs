using OptimFoundation.Modeling;

namespace FJSP_BASIC_BRICK
{
    /// <summary>作業指派到機台＝1；對應 Model.md 的 Assign_{Lot,Operation,Eqp} ∈ {0,1}。</summary>
    [OptVar]
    [OptDim<string>("Lot")]
    [OptDim<string>("Operation")]
    [OptDim<string>("Eqp")]
    public sealed partial class VariableB_Assign { }
}
