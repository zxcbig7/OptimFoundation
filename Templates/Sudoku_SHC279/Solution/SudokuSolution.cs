using OptimFoundation.Core;
using OptimFoundation.Cplex;
using Sudoku_SHC279.Data;
using Sudoku_SHC279.VariableClass;

namespace Sudoku_SHC279.Solution;

public sealed class SudokuSolution
{
    public int[,] Grid { get; }

    private SudokuSolution(int[,] grid) => Grid = grid;

    public static SudokuSolution ReadAndValidate(OptEngine engine, Dataload data)
    {
        var grid = new int[9, 9];

        for (int row = 1; row <= 9; row++)
            for (int column = 1; column <= 9; column++)
            {
                int selected = 0;
                for (int digit = 1; digit <= 9; digit++)
                {
                    var variable = new VariableB_CellDigit { Row = row, Column = column, Digit = digit };
                    if (engine.GetVariableValue(variable.ToString()) <= 0.5) continue;
                    if (selected != 0)
                        throw new InvalidOperationException($"格 ({row},{column}) 選到多個數字。 ");
                    selected = digit;
                }

                if (selected == 0)
                    throw new InvalidOperationException($"格 ({row},{column}) 沒有選到數字。 ");
                grid[row - 1, column - 1] = selected;
            }

        ValidateRules(grid, data);
        Logging.Info($"[Sudoku求解完成] puzzle={Dataload.PuzzleName} status={engine.Status} validated=true");
        return new SudokuSolution(grid);
    }

    private static void ValidateRules(int[,] grid, Dataload data)
    {
        foreach (var given in data.parameter_Given)
            if (grid[given.Row - 1, given.Column - 1] != given.Digit)
                throw new InvalidOperationException($"解答違反 given ({given.Row},{given.Column})={given.Digit}。 ");

        for (int index = 0; index < 9; index++)
        {
            RequireOneToNine(Enumerable.Range(0, 9).Select(column => grid[index, column]), $"row {index + 1}");
            RequireOneToNine(Enumerable.Range(0, 9).Select(row => grid[row, index]), $"column {index + 1}");
        }

        for (int blockRow = 0; blockRow < 3; blockRow++)
            for (int blockColumn = 0; blockColumn < 3; blockColumn++)
                RequireOneToNine(
                    from rowOffset in Enumerable.Range(0, 3)
                    from columnOffset in Enumerable.Range(0, 3)
                    select grid[blockRow * 3 + rowOffset, blockColumn * 3 + columnOffset],
                    $"block {blockRow + 1},{blockColumn + 1}");
    }

    private static void RequireOneToNine(IEnumerable<int> values, string group)
    {
        if (!values.OrderBy(value => value).SequenceEqual(Enumerable.Range(1, 9)))
            throw new InvalidOperationException($"{group} 不是 1..9 的排列。 ");
    }

    public void Print()
    {
        Console.WriteLine("+-------+-------+-------+");
        for (int row = 0; row < 9; row++)
        {
            Console.Write("| ");
            for (int column = 0; column < 9; column++)
            {
                Console.Write($"{Grid[row, column]} ");
                if ((column + 1) % 3 == 0) Console.Write("| ");
            }
            Console.WriteLine();
            if ((row + 1) % 3 == 0) Console.WriteLine("+-------+-------+-------+");
        }
    }
}
