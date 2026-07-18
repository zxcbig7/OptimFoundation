using OptimFoundation.Modeling;
using FJSP_BASIC_BRICK.SetClass;

namespace FJSP_BASIC_BRICK.VariableClass
{
    /// <summary>
    /// 同機台先後序：(LotA,OperationA) 先於 (LotB,OperationB) =1。數學：Precede_{lA,oA,lB,oB}。
    /// ★ 同 set 多維度示範：LotA/LotB 都 ∈ Lot、OperationA/OperationB 都 ∈ Operation。
    /// 用具名維度 [OptDim<TSet>("name")]：泛型 = 來源 set（直接綁、不 alias），字串 = 維度角色名。
    /// </summary>
    [OptVar]
    [OptDim<Set_Lot>("LotA")]
    [OptDim<Set_Operation>("OperationA")]
    [OptDim<Set_Lot>("LotB")]
    [OptDim<Set_Operation>("OperationB")]
    public partial class VariableB_Precede;
}
