using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Sudoku_SHC279
{
    /// <summary>Sudoku template 的三態入口：import、experiment 與正式求解。</summary>
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
            if (isExperiment)
                Logging.SetLogFileName($"{Dataload.PuzzleName}_exp");

            // 1. 材料
            var data = OptData.Load(() => new Dataload());
            double exactlyOne = data.parameter_ExactlyOne.Single().QTY;

            var projectConfig = new ProjectConfig
            {
                ProjectName = Dataload.PuzzleName,
                EnableSolverLog = false,
                ExportLP = true,
            };
            // Production baseline/champion：tuning promotion 只更新這一個設定來源。
            // Provenance：initial baseline；r2 評估後 retained（無 promotion）。詳見 TuningHistory.md。
            // 每次 promotion MUST 同步更新此 provenance 與 TuningHistory.md。
            // Experiment 從它 clone variants；正式求解直接使用它。
            var productionBaseline = new CplexConfig
            {
                timeLimit = 30,
                workThreads = 1,
                parallelMode = 1,
                randomSeed = 1,
            };

            // 2. 模型
            var model = new OptModel("Canonical")
                .AddVariables(engine => engine.BuildVars<VariableB_CellDigit>(
                    data.set_Row,
                    data.set_Column,
                    data.set_Digit))
                .AddObjective(engine => new ObjectiveFunction(
                    data.set_Row,
                    data.set_Column,
                    data.set_Digit,
                    data.parameter_ObjCoef).Build(engine))
                .AddConstraints(engine => new Constraint_CellValue(
                    data.set_Row,
                    data.set_Column,
                    data.set_Digit,
                    exactlyOne).Build(engine))
                .AddConstraints(engine => new Constraint_RowDigit(
                    data.set_Row,
                    data.set_Column,
                    data.set_Digit,
                    exactlyOne).Build(engine))
                .AddConstraints(engine => new Constraint_ColumnDigit(
                    data.set_Row,
                    data.set_Column,
                    data.set_Digit,
                    exactlyOne).Build(engine))
                .AddConstraints(engine => new Constraint_BlockDigit(
                    data.set_Block,
                    data.set_Digit,
                    data.parameter_BlockCell,
                    exactlyOne).Build(engine))
                .AddConstraints(engine => new Constraint_Given(
                    data.parameter_Given,
                    exactlyOne).Build(engine));

            // 3. 環境
            if (isExperiment)
            {
                // r2：先 warm-up，再以三個 seed 與輪替順序降低 cold-start / 執行順序偏差。
                var warmup = productionBaseline.Clone();

                var baselineSeed1 = productionBaseline.Clone();
                baselineSeed1.randomSeed = 1;
                var feasibilitySeed1 = baselineSeed1.Clone();
                feasibilitySeed1.Emphasis = 1;
                var probeSeed1 = baselineSeed1.Clone();
                probeSeed1.probe = 2;

                var baselineSeed2 = productionBaseline.Clone();
                baselineSeed2.randomSeed = 2;
                var feasibilitySeed2 = baselineSeed2.Clone();
                feasibilitySeed2.Emphasis = 1;
                var probeSeed2 = baselineSeed2.Clone();
                probeSeed2.probe = 2;

                var baselineSeed3 = productionBaseline.Clone();
                baselineSeed3.randomSeed = 3;
                var feasibilitySeed3 = baselineSeed3.Clone();
                feasibilitySeed3.Emphasis = 1;
                var probeSeed3 = baselineSeed3.Clone();
                probeSeed3.probe = 2;

                var result = new OptExperiment(
                        $"{Dataload.PuzzleName}-tuning-r2",
                        "warm-up + three seeds with rotated variant order")
                    .AddModel(model)
                    .AddConfig("r2-warmup-exclude", warmup)
                    .AddConfig("r2-s1-baseline", baselineSeed1)
                    .AddConfig("r2-s1-emphasis=feasibility", feasibilitySeed1)
                    .AddConfig("r2-s1-probe=aggressive", probeSeed1)
                    .AddConfig("r2-s2-probe=aggressive", probeSeed2)
                    .AddConfig("r2-s2-baseline", baselineSeed2)
                    .AddConfig("r2-s2-emphasis=feasibility", feasibilitySeed2)
                    .AddConfig("r2-s3-emphasis=feasibility", feasibilitySeed3)
                    .AddConfig("r2-s3-probe=aggressive", probeSeed3)
                    .AddConfig("r2-s3-baseline", baselineSeed3)
                    .Run();

                foreach (var trial in result.Trials)
                    Logging.Info(
                        $"[Experiment] {trial.Label} status={trial.Metrics.Status} " +
                        $"runTimeMs={trial.Metrics.RunTimeMs:F0} " +
                        $"nodes={trial.Metrics.NodeCount?.ToString() ?? "-"}");
                return 0;
            }

            // 4. 正式求解
            using var project = new OptProject(model)
                .UseConfig(() => projectConfig)
                .UseConfig(() => productionBaseline)
                .OnSolved(engine => Sudoku_SHC279Solution.ReadAndValidate(engine, data).Print());

            return project.Execute() ? 0 : 1;
        }
    }
}
