using OptimFoundation.Core;
using OptimFoundation.Cplex;
using Sudoku_SHC279.VariableClass;

namespace Sudoku_SHC279.Constraint;

/// <summary>∀ row,digit：Σ_column x[row,column,digit] = 1。</summary>
public sealed class Constraint_RowDigit : ConstraintBase
{
    private readonly IReadOnlyList<int> _rows;
    private readonly IReadOnlyList<int> _columns;
    private readonly IReadOnlyList<int> _digits;

    public Constraint_RowDigit(
        IReadOnlyList<int> rows,
        IReadOnlyList<int> columns,
        IReadOnlyList<int> digits)
    {
        _rows = rows;
        _columns = columns;
        _digits = digits;
    }

    public void Build(OptEngine engine)
    {
        foreach (int row in _rows)
            foreach (int digit in _digits)
            {
                foreach (int column in _columns)
                    engine.AddLHS(1.0, new VariableB_CellDigit
                    {
                        Row = row,
                        Column = column,
                        Digit = digit,
                    });

                engine.CreateEqual(1.0, $"{ConstraintName}@{row}@{digit}");
            }
    }
}
