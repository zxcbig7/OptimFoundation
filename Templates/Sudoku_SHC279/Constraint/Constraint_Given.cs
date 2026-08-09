using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Sudoku_SHC279
{
    /// <summary>把題盤的每個已知格固定為指定數字；對應 Model.md 的 Given。</summary>
    public sealed class Constraint_Given : ConstraintBase
    {
        private readonly List<Set_Given> _givens;
        private readonly double _exactlyOne;

        public Constraint_Given(List<Set_Given> givens, double exactlyOne)
        {
            _givens = givens;
            _exactlyOne = exactlyOne;
        }

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

                engine.AddRHS(_exactlyOne);
                engine.CreateEqual($"{ConstraintName}@{given.Row}@{given.Column}@{given.Digit}");
            }
        }
    }
}
