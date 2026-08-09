using OptimFoundation.Modeling;

namespace Sudoku_SHC279
{
    /// <summary>可填入的數字；對應 Model.md 的 DIGIT。</summary>
    [OptSet]
    [OptDim<int>("Digit")]
    public sealed partial class Set_Digit { }
}
