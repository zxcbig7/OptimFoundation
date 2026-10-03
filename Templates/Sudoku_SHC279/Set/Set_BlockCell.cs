using OptimFoundation.Modeling;

namespace Sudoku_SHC279
{
    /// <summary>列出每個宮包含哪些格子，每列記錄（宮, 列, 欄）；模型依這份資料判斷格子屬於哪個宮。</summary>
    [OptSet]
    [OptDim<int>("Block")]
    [OptDim<int>("Row")]
    [OptDim<int>("Column")]
    public sealed partial class Set_BlockCell { }
}
