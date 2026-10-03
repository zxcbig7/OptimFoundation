using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工的 Backup 班別設定；對應 Model.md 的 BackupGroup_{Employee,Group}。
    /// 僅保存資料，Constraint 與 Objective 未使用；見 Model.md。</summary>
    [OptParam]
    [OptDim<string>("Employee")]
    [OptDim<string>("Group")]
    public sealed partial class Parameter_BackupGroup { }
}
