using OptimFoundation.Modeling;

namespace FJSP_BASIC.VariableClass
{
    /// <summary>
    /// 同機台先後序：(LotA,OperationA) 先於 (LotB,OperationB) =1。
    /// 以全笛卡兒積建立，constraint 只引用 lotA &lt; lotB（字典序）的跨批次組合，其餘由 presolve 剪除。
    /// </summary>
    [OptVar(VarType.Binary, "LotA", "OperationA", "LotB", "OperationB")]
    public partial class VariableB_Precede { }
}
