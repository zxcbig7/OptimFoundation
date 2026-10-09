using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace TSP_MultiDimSet
{
    /// <summary>TSP 範例的程式入口，提供參數比較實驗與正式求解；資料直接維護在 CSV，不需 import 模式。</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool isExperiment = args.Any(
                arg => string.Equals(arg, "exp", StringComparison.OrdinalIgnoreCase));
            // OptProject 管理 log、資料夾與保留期。
            using var project = new OptProject(Dataload.InstanceName);

            // 1. 材料
            var data = OptData.Load(() => new Dataload());

            var projectConfig = new ProjectConfig
            {
                EnableSolverLog = false,
                ExportLP = true,
            };
            // 正式設定；實驗先 Clone 再修改。
            var productionBaseline = new CplexConfig
            {
                TimeLimit = 30,
                Threads = 1,
                ParallelMode = 1,
                Seed = 1,
            };

            // 2. 模型
            var model = new OptModel("TSP_MultiDimSet")
                .AddVariables<VariableB_UseArc>(data.set_Arc)
                .AddVariables<VariableC_VisitOrder>(data.set_Customer)
                .AddObjective<ObjectiveFunction>(
                    data.set_Arc,
                    data.parameter_ArcCost)
                .AddConstraints<Constraint_CustomerInDegree>(
                    data.set_Customer,
                    data.set_Arc)
                .AddConstraints<Constraint_CustomerOutDegree>(
                    data.set_Customer,
                    data.set_Arc)
                .AddConstraints<Constraint_DepotOutDegree>(
                    data.set_Depot,
                    data.set_Arc)
                .AddConstraints<Constraint_DepotInDegree>(
                    data.set_Depot,
                    data.set_Arc)
                .AddConstraints<Constraint_SubtourMTZ>(
                    data.set_Node,
                    data.set_Customer,
                    data.set_Arc)
                .AddConstraints<Constraint_VisitOrderRange>(
                    data.set_Node,
                    data.set_Customer);

            // 3. 環境
            if (isExperiment)
            {
                // 使用相同 seed 比較各設定。
                // seed 6、7、8 留到最後驗證已選設定，不參與調參比較。
                var warmup = productionBaseline.Clone();

                CplexConfig Seeded(int seed)
                {
                    var config = productionBaseline.Clone();
                    config.Seed = seed;
                    return config;
                }

                // 另外測試 MipGap = 0，觀察求到最佳解的結果；停止條件不同，因此不加入參數設定的排名。
                var probe = productionBaseline.Clone();
                probe.MipGap = 0.0;

                var result = project.Experiment(
                        "R0",
                        "S2 R0 calibration: baseline x 5 tuning seeds + MipGap=0 contract probe")
                    .AddModel(model)
                    .AddSolverConfig("warmup-exclude", warmup)
                    .AddSolverConfig("r0-s1", Seeded(1))
                    .AddSolverConfig("r0-s2", Seeded(2))
                    .AddSolverConfig("r0-s3", Seeded(3))
                    .AddSolverConfig("r0-s4", Seeded(4))
                    .AddSolverConfig("r0-s5", Seeded(5))
                    .AddSolverConfig("probe-mipgap0", probe)
                    .Run();

                foreach (var trial in result.Trials)
                    Logging.Info(
                        $"[Experiment] {trial.Label} status={trial.Metrics.Status} " +
                        $"obj={trial.Metrics.ObjectiveValue:F4} " +
                        $"solveTimeMs={trial.Metrics.SolveTimeMs:F0}");
                return 0;
            }

            // 4. 正式求解
            project.Production()
                .AddProjectConfig(projectConfig)
                .AddModel(model)
                .AddSolverConfig("production", productionBaseline)
                .OnSolved(engine => TSP_MultiDimSetSolution.ReadAndValidate(engine, data).Print())
                .Run();
            return project.IsSuccess ? 0 : 1;
        }
    }
}
