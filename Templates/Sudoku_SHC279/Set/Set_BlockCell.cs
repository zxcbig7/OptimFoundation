using OptimFoundation.Modeling;

namespace Sudoku_SHC279
{
    /// <summary>每個宮涵蓋的（宮, 列, 欄）組合；宮的劃分規則資料化。</summary>
    [OptSet]
    [OptDim<int>("Block")]
    [OptDim<int>("Row")]
    [OptDim<int>("Column")]
    public sealed partial class Set_BlockCell { }
}
