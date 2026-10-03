using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工的 Backup 班別設定；對應 Model.md 的 BackupGroup_{Employee,Group}。
    /// 目前只保存資料，Constraint 與 Objective 均未使用；沿用原始行為，見 Model.md 預設假設。</summary>
    [OptParam]
    [OptDim<string>("Employee")]
    [OptDim<string>("Group")]
    public sealed partial class Parameter_BackupGroup { }
}
