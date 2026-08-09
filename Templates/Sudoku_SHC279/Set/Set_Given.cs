using OptimFoundation.Modeling;

namespace Sudoku_SHC279
{
    /// <summary>題盤已知數字的（列, 欄, 數字）組合；對應 Model.md 的 Given。</summary>
    [OptSet]
    [OptDim<int>("Row")]
    [OptDim<int>("Column")]
    [OptDim<int>("Digit")]
    public sealed partial class Set_Given { }
}
