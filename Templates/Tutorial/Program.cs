using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace Tutorial
{
    // 三態 CLI：
    //   dotnet run              -- 正式求解（預設）：讀 Data/*.csv → solve → ValidateRules → 解寫回 Solution/*.csv
    //   dotnet run -- exp       -- 實驗模式：同一模型 × 三組 MIP emphasis 對照，Experiments/*.csv/json
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool isExperiment = args.Any(arg => string.Equals(arg, "exp", StringComparison.OrdinalIgnoreCase));
            if (isExperiment)
                Logging.SetLogFileName("Tutorial_exp");

            // ── 1. 材料 ────────────────────────────────────────────
            var data = OptData.Load(() => new Dataload());

            var projectConfig = new ProjectConfig
            {
                ProjectName = "Tutorial",
                EnableSolverLog = true,
                ExportLP = true,
                ExportSol = true,
                DataId = "Tutorial",
                UserId = "SYSTEM",
            };
            // 唯一 production baseline/champion；experiment clone 它，prod 直接使用它。
            var productionBaseline = new CplexConfig
            {
                MipGap = 1e-6,
                TimeLimit = 120,
            };

            // ── 2. 模型 ────────────────────────────────────────────
            var model = new OptModel("Canonical")
                .AddVariables(engine => engine.BuildVars<VariableC_Produce>(data.set_Product, data.set_Date, data.set_Shift))
                .AddVariables(engine => engine.BuildVars<VariableB_Setup>(data.set_Product, data.set_Date, data.set_Shift))
                .AddVariables(engine => engine.BuildVars<VariableI_Batch>(data.set_Product, data.set_Date))
                .AddObjective(engine => new ObjectiveFunction(data.set_Product, data.set_Date, data.set_Shift, data.parameter_UnitProfit, data.parameter_SetupCost).Build(engine))
                .AddConstraints(engine => new Constraint_Capacity(data.set_Product, data.set_Machine, data.set_Date, data.set_Shift, data.parameter_MachineHours, data.parameter_Capacity).Build(engine))
                .AddConstraints(engine => new Constraint_Demand(data.set_Product, data.set_Date, data.set_Shift, data.parameter_Demand).Build(engine))
                .AddConstraints(engine => new Constraint_BatchDef(data.set_Product, data.set_Date, data.set_Shift, data.parameter_BatchSize).Build(engine))
                .AddConstraints(engine => new Constraint_SetupLink(data.set_Product, data.set_Date, data.set_Shift, data.BigM).Build(engine));

            // ── 3. 環境 ────────────────────────────────────────────
            // exp：三組 MIP emphasis 對照，不做正式求解
            if (isExperiment)
            {
                var balanced = productionBaseline.Clone();
                var feasibleFirst = balanced.Clone();
                feasibleFirst.Emphasis = 1;
                var optimalFirst = balanced.Clone();
                optimalFirst.Emphasis = 2;

                var result = new OptExperiment("Tutorial-tuning-r1", "同一模型 × 三組 MIP emphasis 對照")
                    .AddModel(model)
                    .AddConfig("balanced", balanced)
                    .AddConfig("feasible-first", feasibleFirst)
                    .AddConfig("optimal-first", optimalFirst)
                    .Run();

                foreach (var trial in result.Trials)
                    Logging.Info($"[Experiment] {trial.Label} status={trial.Metrics.Status} runTimeMs={trial.Metrics.RunTimeMs:F0}");
                return 0;
            }

            // 預設：正式求解
            using var project = new OptProject(model)
                .UseConfig(() => projectConfig)
                .UseConfig(() => productionBaseline)
                .OnSolved(engine => TutorialSolution.ReadAndValidate(engine, data).Print());

            bool solved = project.Execute();
            return solved ? 0 : 1;
        }
    }
}
