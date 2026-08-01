using OptimFoundation.Core;
using OptimFoundation.Cplex;
using Sudoku_SHC279.VariableClass;

namespace Sudoku_SHC279.Constraint;

/// <summary>∀ 3×3 block,digit：該宮九格的 x 合計 = 1。</summary>
public sealed class Constraint_BlockDigit : ConstraintBase
{
    private readonly IReadOnlyList<int> _digits;

    public Constraint_BlockDigit(IReadOnlyList<int> digits) => _digits = digits;

    public void Build(OptEngine engine)
    {
        for (int blockRow = 0; blockRow < 3; blockRow++)
            for (int blockColumn = 0; blockColumn < 3; blockColumn++)
                foreach (int digit in _digits)
                {
                    for (int rowOffset = 1; rowOffset <= 3; rowOffset++)
                        for (int columnOffset = 1; columnOffset <= 3; columnOffset++)
                            engine.AddLHS(1.0, new VariableB_CellDigit
                            {
                                Row = blockRow * 3 + rowOffset,
                                Column = blockColumn * 3 + columnOffset,
                                Digit = digit,
                            });

                    engine.CreateEqual(
                        1.0,
                        $"{ConstraintName}@{blockRow + 1}_{blockColumn + 1}@{digit}");
                }
    }
}
