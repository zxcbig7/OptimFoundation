using OptimFoundation.Modeling;

namespace RosteringProblem
{
    /// <summary>員工 Employee 在 Date 是否觸犯前一天班別轉當天班別的違規組合；對應 Model.md 的 NightToDay_{Date,Employee} ∈ {0,1}。</summary>
    [OptDim<DateTime>("Date")]
    [OptDim<string>("Employee")]
    [OptVar]
    public sealed partial class VariableB_NightToDay { }
}
