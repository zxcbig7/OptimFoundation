using OptimFoundation.Core;

namespace Sudoku_SHC279.VariableClass;

/// <summary>x[row,column,digit] = 1 表示該格填入該數字。</summary>
public sealed class VariableB_CellDigit : VariableBase
{
    public int Row { get; set; }
    public int Column { get; set; }
    public int Digit { get; set; }
}
