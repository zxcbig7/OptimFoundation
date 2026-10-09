using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Sudoku_SHC279
{
    /// <summary>Sudoku 範例的程式入口，提供題盤匯入、參數比較實驗與正式求解三種模式。</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "import")
            {
                OptData.Load(() => new Dataload(args[1])).Export();
                return 0;
            }

            bool isExperiment = args.Any(
                arg => string.Equals(arg, "exp", StringComparison.OrdinalIgnoreCase));
            // OptProject 管理 log、資料夾與保留期。
            using var project = new OptProject(Dataload.PuzzleName);

            // 1. 材料
            var data = OptData.Load(() => new Dataload());
            double exactlyOne = data.parameter_ExactlyOne.Single().QTY;

            var projectConfig = new ProjectConfig
            {
                EnableSolverLog = false,
                ExportLP = true,
            };
            // 正式設定；實驗先 Clone 再修改。調參證據見 TuningHistory.md。
            var productionBaseline = new CplexConfig
            {
                TimeLimit = 30,
                Threads = 1,
                ParallelMode = 1,
                Seed = 1,
            };

            // 2. 模型
            var model = new OptModel("Sudoku_SHC279")
                .AddVariables<VariableB_CellDigit>(
                    data.set_Row,
                    data.set_Column,
                    data.set_Digit)
                .AddObjective<ObjectiveFunction>(
                    data.set_Row,
                    data.set_Column,
                    data.set_Digit,
                    data.parameter_ObjCoef)
                .AddConstraints<Constraint_CellValue>(
                    data.set_Row,
                    data.set_Column,
                    data.set_Digit,
                    exactlyOne)
                .AddConstraints<Constraint_RowDigit>(
                    data.set_Row,
                    data.set_Column,
                    data.set_Digit,
                    exactlyOne)
                .AddConstraints<Constraint_ColumnDigit>(
                    data.set_Row,
                    data.set_Column,
                    data.set_Digit,
                    exactlyOne)
                .AddConstraints<Constraint_BlockDigit>(
                    data.set_Block,
                    data.set_Digit,
                    data.set_BlockCell,
                    exactlyOne)
                .AddConstraints<Constraint_Given>(
                    data.set_Given,
                    exactlyOne);

            // 3. 環境
            if (isExperiment)
            {
                // r2：先求解一次暖機，再用三個 seed 輪替各組設定的執行順序，減少啟動耗時與先後順序對比較的影響。
                var warmup = productionBaseline.Clone();

                var baselineSeed1 = productionBaseline.Clone();
                baselineSeed1.Seed = 1;
                var feasibilitySeed1 = baselineSeed1.Clone();
                feasibilitySeed1.Emphasis = 1;
                var probeSeed1 = baselineSeed1.Clone();
                probeSeed1.Probe = 2;

                var baselineSeed2 = productionBaseline.Clone();
                baselineSeed2.Seed = 2;
                var feasibilitySeed2 = baselineSeed2.Clone();
                feasibilitySeed2.Emphasis = 1;
                var probeSeed2 = baselineSeed2.Clone();
                probeSeed2.Probe = 2;

                var baselineSeed3 = productionBaseline.Clone();
                baselineSeed3.Seed = 3;
                var feasibilitySeed3 = baselineSeed3.Clone();
                feasibilitySeed3.Emphasis = 1;
                var probeSeed3 = baselineSeed3.Clone();
                probeSeed3.Probe = 2;

                var result = project.Experiment(
                        "tuning-r2",
                        "warm-up + three seeds with rotated variant order")
                    .AddModel(model)
                    .AddSolverConfig("r2-warmup-exclude", warmup)
                    .AddSolverConfig("r2-s1-baseline", baselineSeed1)
                    .AddSolverConfig("r2-s1-emphasis=feasibility", feasibilitySeed1)
                    .AddSolverConfig("r2-s1-probe=aggressive", probeSeed1)
                    .AddSolverConfig("r2-s2-probe=aggressive", probeSeed2)
                    .AddSolverConfig("r2-s2-baseline", baselineSeed2)
                    .AddSolverConfig("r2-s2-emphasis=feasibility", feasibilitySeed2)
                    .AddSolverConfig("r2-s3-emphasis=feasibility", feasibilitySeed3)
                    .AddSolverConfig("r2-s3-probe=aggressive", probeSeed3)
                    .AddSolverConfig("r2-s3-baseline", baselineSeed3)
                    .Run();

                foreach (var trial in result.Trials)
                    Logging.Info(
                        $"[Experiment] {trial.Label} status={trial.Metrics.Status} " +
                        $"solveTimeMs={trial.Metrics.SolveTimeMs:F0} " +
                        $"nodes={trial.Metrics.NodeCount?.ToString() ?? "-"}");
                return 0;
            }

            // 4. 正式求解
            project.Production()
                .AddProjectConfig(projectConfig)
                .AddModel(model)
                .AddSolverConfig("production", productionBaseline)
                .OnSolved(engine => Sudoku_SHC279Solution.ReadAndValidate(engine, data).Print())
                .Run();
            return project.IsSuccess ? 0 : 1;
        }
    }
}
