using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Sudoku_SHC279
{
    /// <summary>每列的每個數字恰好出現一次；對應 Model.md 的 RowDigit。</summary>
    public sealed class Constraint_RowDigit : ConstraintBase
    {
        private readonly List<Set_Row> _rows;
        private readonly List<Set_Column> _columns;
        private readonly List<Set_Digit> _digits;
        private readonly double _exactlyOne;

        public Constraint_RowDigit(
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
