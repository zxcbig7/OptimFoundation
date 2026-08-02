using OptimFoundation.Modeling;

namespace FJSP_BASIC_BRICK
{
    /// <summary>
    /// 同機台先後序：(LotA,OperationA) 先於 (LotB,OperationB)＝1；對應 Model.md 的 Precede_{LotA,OperationA,LotB,OperationB}。
    /// 同 set 多維度：LotA/LotB 皆 ∈ Lot、OperationA/OperationB 皆 ∈ Operation。
    /// </summary>
    [OptVar]
    [OptDim<Set_Lot>("LotA")]
    [OptDim<Set_Operation>("OperationA")]
    [OptDim<Set_Lot>("LotB")]
    [OptDim<Set_Operation>("OperationB")]
    public sealed partial class VariableB_Precede { }
}
