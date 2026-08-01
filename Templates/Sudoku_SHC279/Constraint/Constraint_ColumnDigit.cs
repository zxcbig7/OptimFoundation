using OptimFoundation.Core;
using OptimFoundation.Cplex;
using Sudoku_SHC279.VariableClass;

namespace Sudoku_SHC279.Constraint;

/// <summary>∀ column,digit：Σ_row x[row,column,digit] = 1。</summary>
public sealed class Constraint_ColumnDigit : ConstraintBase
{
    private readonly IReadOnlyList<int> _rows;
    private readonly IReadOnlyList<int> _columns;
    private readonly IReadOnlyList<int> _digits;

    public Constraint_ColumnDigit(
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
        foreach (int column in _columns)
            foreach (int digit in _digits)
            {
                foreach (int row in _rows)
                    engine.AddLHS(1.0, new VariableB_CellDigit
                    {
                        Row = row,
                        Column = column,
                        Digit = digit,
                    });

                engine.CreateEqual(1.0, $"{ConstraintName}@{column}@{digit}");
            }
    }
}
