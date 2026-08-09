using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>視為違規的「前一天班別 → 當天班別」組合；對應 Model.md 的 NightToDayRule_{PreGroup,Group}。
    /// QTY 沿用原始資料生成時的權重快照，目前未被 Constraint_NightToDay 讀取（見 Model.md 預設假設）。</summary>
    [OptParam]
    [OptDim<string>("PreGroup")]
    [OptDim<string>("Group")]
    public sealed partial class Parameter_NightToDay { }
}
