using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工被視為跨組別的班別清單；對應 Model.md 的 CrossGroup_{Employee,Group}。
    /// QTY 保留產生範例資料時設定的權重，目前未被 Constraint_CrossGroup 讀取（見 Model.md 預設假設）。</summary>
    [OptParam]
    [OptDim<string>("Employee")]
    [OptDim<string>("Group")]
    public sealed partial class Parameter_CrossGroup { }
}
