using OptimFoundation.Modeling;

namespace TSP_MultiDimSet
{
    /// <summary>是否使用該弧；對應 Model.md 的 UseArc_{From,To} ∈ {0,1}。</summary>
    [OptVar]
    [OptDim<string>("From")]
    [OptDim<string>("To")]
    public sealed partial class VariableB_UseArc { }
}
