using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Sudoku_SHC279
{
    /// <summary>讀取、驗證並顯示 Sudoku 解；所有盤面結構均取自 Dataload。</summary>
    public sealed class Sudoku_SHC279Solution
    {
        private readonly Dataload _data;

        public IReadOnlyDictionary<(int Row, int Column), int> Grid { get; }

        private Sudoku_SHC279Solution(
            Dictionary<(int Row, int Column), int> grid,
            Dataload data)
        {
            Grid = grid;
            _data = data;
        }

        public static Sudoku_SHC279Solution ReadAndValidate(OptEngine engine, Dataload data)
        {
            var grid = new Dictionary<(int Row, int Column), int>();

            foreach (int row in data.set_Row)
                foreach (int column in data.set_Column)
                {
                    var selectedDigits = data.set_Digit
                        .Where(digit => engine.GetVariableValue(
                            new VariableB_CellDigit
                            {
                                Row = row,
                                Column = column,
                                Digit = digit,
                            }.ToString()) > 0.5)
                        .ToList();

                    if (selectedDigits.Count != 1)
                        throw new InvalidOperationException(
                            $"格子 ({row},{column}) 選中了 {selectedDigits.Count} 個數字，預期恰好一個。");

                    grid[(row, column)] = selectedDigits.Single();
                }

            ValidateRules(grid, data);
            Logging.Info(
                $"[Sudoku solution] puzzle={Dataload.PuzzleName} status={engine.Status} validated=true");
            return new Sudoku_SHC279Solution(grid, data);
        }

        private static void ValidateRules(
            IReadOnlyDictionary<(int Row, int Column), int> grid,
            Dataload data)
        {
            foreach (var given in data.set_Given)
                if (grid[(given.Row, given.Column)] != given.Digit)
                    throw new InvalidOperationException(
                        $"解違反 given ({given.Row},{given.Column})={given.Digit}。");

            foreach (int row in data.set_Row)
                RequireAllDigits(
                    data.set_Column.Select(column => grid[(row, column)]),
                    data.set_Digit,
                    $"row {row}");

            foreach (int column in data.set_Column)
                RequireAllDigits(
                    data.set_Row.Select(row => grid[(row, column)]),
                    data.set_Digit,
                    $"column {column}");

            foreach (int block in data.set_Block)
                RequireAllDigits(
                    data.set_BlockCell
                        .Where(cell => cell.Block == block)
                        .Select(cell => grid[(cell.Row, cell.Column)]),
                    data.set_Digit,
                    $"block {block}");
        }

        private static void RequireAllDigits(
            IEnumerable<int> values,
            List<Set_Digit> digits,
            string group)
        {
            if (!values.OrderBy(value => value).SequenceEqual(digits.Select(digit => digit.Digit).OrderBy(digit => digit)))
                throw new InvalidOperationException($"{group} 未恰好包含完整 DIGIT 集合。");
        }

        private int BlockOf(int row, int column) =>
            _data.set_BlockCell
                .Single(cell => cell.Row == row && cell.Column == column)
                .Block;

        public void Print()
        {
            string divider = new('-', _data.set_Column.Count * 2 + _data.set_Block.Count + 1);
            Console.WriteLine(divider);

            for (int rowIndex = 0; rowIndex < _data.set_Row.Count; rowIndex++)
            {
                int row = _data.set_Row[rowIndex];
                Console.Write("| ");

                for (int columnIndex = 0; columnIndex < _data.set_Column.Count; columnIndex++)
                {
                    int column = _data.set_Column[columnIndex];
                    Console.Write(Grid[(row, column)]);

                    bool lastColumn = columnIndex == _data.set_Column.Count - 1;
                    bool blockEnds = lastColumn ||
                        BlockOf(row, column) != BlockOf(row, _data.set_Column[columnIndex + 1]);
                    Console.Write(blockEnds ? " | " : " ");
                }

                Console.WriteLine();

                bool lastRow = rowIndex == _data.set_Row.Count - 1;
                bool blockBandEnds = lastRow ||
                    BlockOf(row, _data.set_Column[0]) !=
                    BlockOf(_data.set_Row[rowIndex + 1], _data.set_Column[0]);
                if (blockBandEnds)
                    Console.WriteLine(divider);
            }
        }
    }
}
