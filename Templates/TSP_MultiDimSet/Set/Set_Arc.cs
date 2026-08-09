using OptimFoundation.Modeling;

namespace TSP_MultiDimSet
{
    /// <summary>
    /// 實際可用的有向弧；對應 Model.md 的 ARC。
    /// 多維 Set：一列就是一條存在的弧，模型只在這些成員上展開，不做 NODE × NODE 全格。
    /// </summary>
    [OptSet]
    [OptDim<string>("From")]
    [OptDim<string>("To")]
    public sealed partial class Set_Arc { }
}
