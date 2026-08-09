using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工的 Backup 班別設定；對應 Model.md 的 BackupGroup_{Employee,Group}。
    /// 目前僅作資料紀錄，未被任何 Constraint/Objective 讀取（與原始程式碼行為一致，見 Model.md 預設假設）。</summary>
    [OptParam]
    [OptDim<string>("Employee")]
    [OptDim<string>("Group")]
    public sealed partial class Parameter_BackupGroup { }
}
