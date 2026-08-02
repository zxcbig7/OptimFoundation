using OptimFoundation.Modeling;

namespace FJSP_BASIC_BRICK
{
    /// <summary>作業指派到機台＝1；對應 Model.md 的 Assign_{Lot,Operation,Eqp} ∈ {0,1}。</summary>
    [OptVar]
    [OptDim<Set_Lot>("Lot")]
    [OptDim<Set_Operation>("Operation")]
    [OptDim<Set_Eqp>("Eqp")]
    public sealed partial class VariableB_Assign { }
}
