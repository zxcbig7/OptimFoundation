using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Sudoku_SHC279
{
    /// <summary>每宮的每個數字恰好出現一次；宮的格子由 BlockCell 資料定義。</summary>
    public sealed class Constraint_BlockDigit : ConstraintBase
    {
        private readonly Set_Block _blocks;
        private readonly Set_Digit _digits;
        private readonly List<Parameter_BlockCell> _blockCells;
        private readonly double _exactlyOne;

        public Constraint_BlockDigit(
            Set_Block blocks,
            Set_Digit digits,
            List<Parameter_BlockCell> blockCells,
            double exactlyOne)
        {
            _blocks = blocks;
            _digits = digits;
            _blockCells = blockCells;
            _exactlyOne = exactlyOne;
        }

        public void Build(OptEngine engine)
        {
            foreach (int block in _blocks)
                foreach (int digit in _digits)
                {
                    foreach (var cell in _blockCells.Where(cell => cell.Block == block))
                        engine.AddLHS(1.0, new VariableB_CellDigit
                        {
                            Row = cell.Row,
                            Column = cell.Column,
                            Digit = digit,
                        });

                    engine.AddRHS(_exactlyOne);
                    engine.CreateEqual($"{ConstraintName}@{block}@{digit}");
                }
        }
    }
}
