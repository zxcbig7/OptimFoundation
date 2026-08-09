using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace TSP_MultiDimSet
{
    /// <summary>TSP template 的入口：experiment 與正式求解兩態（資料為手維護 CSV，不需 import 模式）。</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool isExperiment = args.Any(
                arg => string.Equals(arg, "exp", StringComparison.OrdinalIgnoreCase));
            if (isExperiment)
                Logging.SetLogFileName($"{Dataload.InstanceName}_exp");

            // 1. 材料
            var data = OptData.Load(() => new Dataload());

            var projectConfig = new ProjectConfig
            {
                ProjectName = Dataload.InstanceName,
                EnableSolverLog = false,
                ExportLP = true,
            };
            // Production baseline/champion：tuning promotion 只更新這一個設定來源。
            // Provenance：initial baseline，尚未執行 Phase 3 tuning。
            // Experiment 從它 clone variants；正式求解直接使用它。
            var productionBaseline = new CplexConfig
            {
                TimeLimit = 30,
                Threads = 1,
                ParallelMode = 1,
                Seed = 1,
            };

            // 2. 模型
            var model = new OptModel("TSP_MultiDimSet")
                .AddVariables(engine => engine.BuildVars<VariableB_UseArc>(data.set_Arc))
                .AddVariables(engine => engine.BuildVars<VariableC_VisitOrder>(data.set_Customer))
                .AddObjective(engine => new ObjectiveFunction(
                    data.set_Arc,
                    data.parameter_ArcCost).Build(engine))
                .AddConstraints(engine => new Constraint_CustomerInDegree(
                    data.set_Customer,
                    data.set_Arc).Build(engine))
                .AddConstraints(engine => new Constraint_CustomerOutDegree(
                    data.set_Customer,
                    data.set_Arc).Build(engine))
                .AddConstraints(engine => new Constraint_DepotOutDegree(
                    data.set_Depot,
                    data.set_Arc).Build(engine))
                .AddConstraints(engine => new Constraint_DepotInDegree(
                    data.set_Depot,
                    data.set_Arc).Build(engine))
                .AddConstraints(engine => new Constraint_SubtourMTZ(
                    data.set_Node,
                    data.set_Customer,
                    data.set_Arc).Build(engine))
                .AddConstraints(engine => new Constraint_VisitOrderRange(
                    data.set_Node,
                    data.set_Customer).Build(engine));

            // 3. 環境
            if (isExperiment)
            {
                // S2 R0 校準：環境已定版（Threads=1），只跑 baseline × 5 tuning seeds 量 θ 與剖面。
                // seeds 6/7/8 保留為 holdout，全程不參與調參。
                var warmup = productionBaseline.Clone();

                CplexConfig Seeded(int seed)
                {
                    var config = productionBaseline.Clone();
                    config.Seed = seed;
                    return config;
                }

                // 契約健檢探針：MipGap = 0，只當 finding，不進排名。
                var probe = productionBaseline.Clone();
                probe.MipGap = 0.0;

                var result = new OptExperiment(
                        $"{Dataload.InstanceName}-R0",
                        "S2 R0 calibration: baseline x 5 tuning seeds + MipGap=0 contract probe")
                    .AddModel(model)
                    .AddConfig("warmup-exclude", warmup)
                    .AddConfig("r0-s1", Seeded(1))
                    .AddConfig("r0-s2", Seeded(2))
                    .AddConfig("r0-s3", Seeded(3))
                    .AddConfig("r0-s4", Seeded(4))
                    .AddConfig("r0-s5", Seeded(5))
                    .AddConfig("probe-mipgap0", probe)
                    .Run();

                foreach (var trial in result.Trials)
                    Logging.Info(
                        $"[Experiment] {trial.Label} status={trial.Metrics.Status} " +
                        $"obj={trial.Metrics.ObjectiveValue:F4} " +
                        $"runTimeMs={trial.Metrics.RunTimeMs:F0}");
                return 0;
            }

            // 4. 正式求解
            using var project = new OptProject(model)
                .UseConfig(() => projectConfig)
                .UseConfig(() => productionBaseline)
                .OnSolved(engine => TSP_MultiDimSetSolution.ReadAndValidate(engine, data).Print());

            return project.Execute() ? 0 : 1;
        }
    }
}
