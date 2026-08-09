using OptimFoundation.Modeling;

namespace Sudoku_SHC279
{
    /// <summary>該格是否填入該數字；對應 Model.md 的 CellDigit_{Row,Column,Digit} ∈ {0,1}。</summary>
    [OptVar]
    [OptDim<int>("Row")]
    [OptDim<int>("Column")]
    [OptDim<int>("Digit")]
    public sealed partial class VariableB_CellDigit { }
}
