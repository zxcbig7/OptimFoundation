using OptimFoundation.Core;
using OptimFoundation.Cplex;
using Sudoku_SHC279.Constraint;
using Sudoku_SHC279.Data;
using Sudoku_SHC279.Solution;
using Sudoku_SHC279.VariableClass;

namespace Sudoku_SHC279;

internal static class Program
{
    private static int Main(string[] args)
    {
        // 第一階段示範：把不規則來源（9x9 題盤矩陣）攤平成標準 CSV 寫回 Data/，之後就走下面的標準路徑
        // dotnet run -- import raw/Puzzle_SHC279
        if (args.Length >= 2 && args[0] == "import")
        {
            OptData.Load(() => new Dataload(args[1])).Export();
            return 0;
        }

        var data = OptData.Load(() => new Dataload());
        var config = new CplexConfig
        {
            timeLimit = 30,
            workThreads = 1,
            enableLog = false,
            exportLP = true,
        };

        using var model = new OptModel(Dataload.PuzzleName)
            .UseConfig(() => config)
            .AddModel(engine =>
            {
                engine.BuildBVs<VariableB_CellDigit>(data.ROW, data.COLUMN, data.DIGIT);

                new ObjectiveFunction().Build(engine);
                new Constraint_CellValue(data.ROW, data.COLUMN, data.DIGIT).Build(engine);
                new Constraint_RowDigit(data.ROW, data.COLUMN, data.DIGIT).Build(engine);
                new Constraint_ColumnDigit(data.ROW, data.COLUMN, data.DIGIT).Build(engine);
                new Constraint_BlockDigit(data.DIGIT).Build(engine);
                new Constraint_Given(data.parameter_Given).Build(engine);
            })
            .OnSolved(engine => SudokuSolution.ReadAndValidate(engine, data).Print());

        bool solved = model.Execute();

        Logging.Info($"[Sudoku執行結果] puzzle={Dataload.PuzzleName} success={solved} status={model.optEngine.Status}");
        return solved ? 0 : 1;
    }
}
