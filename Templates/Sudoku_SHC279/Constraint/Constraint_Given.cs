using OptimFoundation.Core;
using OptimFoundation.Cplex;
using Sudoku_SHC279.ParameterClass;
using Sudoku_SHC279.VariableClass;

namespace Sudoku_SHC279.Constraint;

/// <summary>∀ given(row,column,digit)：x[row,column,digit] = 1。</summary>
public sealed class Constraint_Given : ConstraintBase
{
    private readonly IReadOnlyList<Parameter_Given> _givens;

    public Constraint_Given(IReadOnlyList<Parameter_Given> givens) => _givens = givens;

    public void Build(OptEngine engine)
    {
        foreach (var given in _givens)
        {
            engine.AddLHS(1.0, new VariableB_CellDigit
            {
                Row = given.Row,
                Column = given.Column,
                Digit = given.Digit,
            });

            engine.CreateEqual(
                1.0,
                $"{ConstraintName}@{given.Row}@{given.Column}@{given.Digit}");
        }
    }
}
