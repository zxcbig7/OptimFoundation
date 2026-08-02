using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Sudoku_SHC279
{
    /// <summary>每列的每個數字恰好出現一次；對應 Model.md 的 RowDigit。</summary>
    public sealed class Constraint_RowDigit : ConstraintBase
    {
        private readonly Set_Row _rows;
        private readonly Set_Column _columns;
        private readonly Set_Digit _digits;
        private readonly double _exactlyOne;

        public Constraint_RowDigit(
            Set_Row rows,
            Set_Column columns,
            Set_Digit digits,
            double exactlyOne)
        {
            _rows = rows;
            _columns = columns;
            _digits = digits;
            _exactlyOne = exactlyOne;
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

                    engine.AddRHS(_exactlyOne);
                    engine.CreateEqual($"{ConstraintName}@{row}@{digit}");
                }
        }
    }
}
