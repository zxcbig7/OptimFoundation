using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>已預先指派的（日期, 員工, 班別）組合；對應 Model.md 的 PreAssign_{Date,Employee,Group}。
    /// QTY 沿用原始資料的預設值 0，Constraint_PreAssign 只讀 key 不讀 QTY。</summary>
    [OptParam]
    [OptDim<DateTime>("Date")]
    [OptDim<string>("Employee")]
    [OptDim<string>("Group")]
    public sealed partial class Parameter_PreAssign { }
}
