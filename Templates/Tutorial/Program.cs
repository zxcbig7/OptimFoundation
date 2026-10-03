using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace Tutorial
{
    // 命令列模式：
    //   dotnet run              -- 正式求解（預設）：讀 FolderDir.Input 的 CSV → solve → ValidateRules → 解寫到 FolderDir.Output
    //   dotnet run -- exp       -- 實驗模式：同一模型 × 三組 MIP emphasis 對照，紀錄接在 Experiment/Tutorial-trial.csv 等累積檔
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool isExperiment = args.Any(arg => string.Equals(arg, "exp", StringComparison.OrdinalIgnoreCase));
            // 由 OptProject 管理 log、資料夾與檔案保留天數；實驗和正式求解都透過它執行。
            using var project = new OptProject("Tutorial");

            // ── 1. 材料 ────────────────────────────────────────────
            var data = OptData.Load(() => new Dataload());

            var projectConfig = new ProjectConfig
            {
                EnableSolverLog = true,
                ExportLP = true,
                ExportSol = true,
            };
            // 正式求解直接使用這組設定；實驗則先複製一份，再調整要比較的參數。
            var productionBaseline = new CplexConfig
            {
                MipGap = 1e-6,
                TimeLimit = 120,
            };

            // ── 2. 模型 ────────────────────────────────────────────
            var model = new OptModel("Canonical");
            model.AddVariables(engine => engine.BuildVars<VariableC_Produce>(data.set_Product, data.set_Date, data.set_Shift));
            model.AddVariables(engine => engine.BuildVars<VariableB_Setup>(data.set_Product, data.set_Date, data.set_Shift));
            model.AddVariables(engine => engine.BuildVars<VariableI_Batch>(data.set_Product, data.set_Date));
            model.AddObjective(engine => new ObjectiveFunction(data.set_Product, data.set_Date, data.set_Shift, data.parameter_UnitProfit, data.parameter_SetupCost).Build(engine));
            model.AddConstraints(engine => new Constraint_Capacity(data.set_Product, data.set_Machine, data.set_Date, data.set_Shift, data.parameter_MachineHours, data.parameter_Capacity).Build(engine));
            model.AddConstraints(engine => new Constraint_Demand(data.set_Product, data.set_Date, data.set_Shift, data.parameter_Demand).Build(engine));
            model.AddConstraints(engine => new Constraint_BatchDef(data.set_Product, data.set_Date, data.set_Shift, data.parameter_BatchSize).Build(engine));
            model.AddConstraints(engine => new Constraint_SetupLink(data.set_Product, data.set_Date, data.set_Shift, data.BigM).Build(engine));

            var model2 = OptModel.ReadModel("Model.lp", "Model2");

            // ── 3. 環境 ────────────────────────────────────────────
            // exp：用三組 MIP emphasis 分別求解，記錄結果供比較。
            if (isExperiment)
            {
                var balanced = productionBaseline.Clone();
                var feasibleFirst = balanced.Clone();
                feasibleFirst.Emphasis = 1;
                var optimalFirst = balanced.Clone();
                optimalFirst.Emphasis = 2;

                var result = project.Experiment("tuning-r1", "同一模型 × 三組 MIP emphasis 對照")
                    .AddModel(model)
                    .AddConfig("balanced", balanced)
                    .AddConfig("feasible-first", feasibleFirst)
                    .AddConfig("optimal-first", optimalFirst)
                    .Run();

                foreach (var trial in result.Trials)
                    Logging.Info($"[Experiment] {trial.Label} status={trial.Metrics.Status} solveTimeMs={trial.Metrics.SolveTimeMs:F0}");
                return 0;
            }

            // 預設：正式求解
            project.LoadConfig(projectConfig);
            bool solved = project.Solve(model, productionBaseline,
                onSolved: engine => TutorialSolution.ReadAndValidate(engine, data).Print());
            return solved ? 0 : 1;
        }
    }
}
