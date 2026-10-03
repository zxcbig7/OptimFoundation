using OptimFoundation.Modeling;

namespace TSP_MultiDimSet
{
    /// <summary>
    /// 實際可用的有向弧；對應 Model.md 的 ARC。
    /// 每列以起點與終點表示一條可走的路徑；模型只為這些路徑建立變數，不建立所有 NODE × NODE 組合。
    /// </summary>
    [OptSet]
    [OptDim<string>("From")]
    [OptDim<string>("To")]
    public sealed partial class Set_Arc { }
}
