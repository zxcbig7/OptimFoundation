using OptimFoundation.Cplex;
using Sudoku_SHC279.VariableClass;

namespace Sudoku_SHC279.Constraint;

/// <summary>Sudoku 是可行性問題；使用零係數目標式讓建模生命週期仍有統一的開始／完成 Log。</summary>
public sealed class ObjectiveFunction
{
    public void Build(OptEngine engine)
    {
        engine.AddLHS(0.0, new VariableB_CellDigit { Row = 1, Column = 1, Digit = 1 });
        engine.CreateMinimize();
    }
}
