using OptimFoundation.Modeling;

namespace Sudoku_SHC279
{
    /// <summary>題盤已知數字的（列, 欄, 數字）組合；對應 Model.md 的 Given。純 key parameter，不生成 QTY。</summary>
    [OptParam(HasValue = false)]
    [OptDim<Set_Row>("Row")]
    [OptDim<Set_Column>("Column")]
    [OptDim<Set_Digit>("Digit")]
    public sealed partial class Parameter_Given { }
}
