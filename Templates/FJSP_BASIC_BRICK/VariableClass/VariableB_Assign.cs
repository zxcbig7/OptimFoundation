using OptimFoundation.Modeling;
using FJSP_BASIC_BRICK.SetClass;

namespace FJSP_BASIC_BRICK.VariableClass
{
    /// <summary>作業指派到機台 =1。數學：Assign_{l,o,e}，l∈Lot o∈Op e∈Eqp。泛型式，維度名自動 = Lot/Operation/Eqp。</summary>
    [OptDim<Set_Lot>("Lot")]
    [OptDim<Set_Operation>("Operation")]
    [OptDim<Set_Eqp>("Eqp")]
    [OptVar]
    public partial class VariableB_Assign { }
}
