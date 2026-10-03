using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>視為違規的「前一天班別 → 當天班別」組合；對應 Model.md 的 NightToDayRule_{PreGroup,Group}。
    /// QTY 保留產生範例資料時設定的權重，目前未被 Constraint_NightToDay 讀取（見 Model.md 預設假設）。</summary>
    [OptParam]
    [OptDim<string>("PreGroup")]
    [OptDim<string>("Group")]
    public sealed partial class Parameter_NightToDay { }
}
