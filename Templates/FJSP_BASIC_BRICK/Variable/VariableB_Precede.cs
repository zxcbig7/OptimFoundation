using OptimFoundation.Modeling;

namespace FJSP_BASIC_BRICK
{
    /// <summary>
    /// 同機台先後序：(LotA,OperationA) 先於 (LotB,OperationB)＝1；對應 Model.md 的 Precede_{LotA,OperationA,LotB,OperationB}。
    /// LotA 與 LotB 都取自 Lot 集合；OperationA 與 OperationB 都取自 Operation 集合，用來表示要比較的兩道作業。
    /// </summary>
    [OptVar]
    [OptDim<string>("LotA")]
    [OptDim<string>("OperationA")]
    [OptDim<string>("LotB")]
    [OptDim<string>("OperationB")]
    public sealed partial class VariableB_Precede { }
}
