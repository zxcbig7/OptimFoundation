using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Sudoku_SHC279
{
    /// <summary>每欄的每個數字恰好出現一次；對應 Model.md 的 ColumnDigit。</summary>
    public sealed class Constraint_ColumnDigit : ConstraintBase
    {
        private readonly List<Set_Row> _rows;
        private readonly List<Set_Column> _columns;
        private readonly List<Set_Digit> _digits;
        private readonly double _exactlyOne;

        public Constraint_ColumnDigit(
            List<Set_Row> rows,
            List<Set_Column> columns,
            List<Set_Digit> digits,
            double exactlyOne)
        {
            _rows = rows;
            _columns = columns;
            _digits = digits;
            _exactlyOne = exactlyOne;
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

                    engine.AddRHS(_exactlyOne);
                    engine.CreateEqual(this, column, digit);
                }
        }
    }
}
