using OptimFoundation.Modeling;

namespace TSP_MultiDimSet
{
    /// <summary>
    /// 實際可用的有向弧；對應 Model.md 的 ARC。
    /// 僅為資料列中的 (From, To) 建變數，不展開 NODE × NODE。
    /// </summary>
    [OptSet]
    [OptDim<string>("From")]
    [OptDim<string>("To")]
    public sealed partial class Set_Arc { }
}
