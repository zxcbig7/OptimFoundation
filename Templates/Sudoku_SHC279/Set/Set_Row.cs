using OptimFoundation.Modeling;

namespace Sudoku_SHC279
{
    /// <summary>盤面的列索引；對應 Model.md 的 ROW。</summary>
    [OptSet]
    [OptDim<int>("Row")]
    public sealed partial class Set_Row { }
}
