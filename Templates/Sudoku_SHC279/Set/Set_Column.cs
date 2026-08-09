using OptimFoundation.Modeling;

namespace Sudoku_SHC279
{
    /// <summary>盤面的欄索引；對應 Model.md 的 COLUMN。</summary>
    [OptSet]
    [OptDim<int>("Column")]
    public sealed partial class Set_Column { }
}
