using OptimFoundation.Modeling;

namespace TSP_MultiDimSet
{
    /// <summary>行駛該弧的成本；對應 Model.md 的 ArcCost_{From,To}。</summary>
    [OptParam]
    [OptDim<string>("From")]
    [OptDim<string>("To")]
    public sealed partial class Parameter_ArcCost { }
}
