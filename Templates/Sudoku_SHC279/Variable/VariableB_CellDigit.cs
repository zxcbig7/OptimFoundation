using OptimFoundation.Modeling;

namespace Sudoku_SHC279
{
    /// <summary>該格是否填入該數字；對應 Model.md 的 CellDigit_{Row,Column,Digit} ∈ {0,1}。</summary>
    [OptVar]
    [OptDim<Set_Row>("Row")]
    [OptDim<Set_Column>("Column")]
    [OptDim<Set_Digit>("Digit")]
    public sealed partial class VariableB_CellDigit { }
}
