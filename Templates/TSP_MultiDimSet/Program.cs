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
            // 由 OptProject 管理 log、資料夾與檔案保留天數；實驗和正式求解都透過它執行。
            using var project = new OptProject(Dataload.InstanceName);

            // 1. 材料
            var data = OptData.Load(() => new Dataload());

            var projectConfig = new ProjectConfig
            {
                EnableSolverLog = false,
                ExportLP = true,
            };
            // 正式求解使用這組設定；調參結果確認較好後，統一更新在這裡。
            // 設定來源：最初的基準設定，尚未執行 Phase 3 調參流程。
            // 實驗先複製這組設定，再調整要比較的參數；正式求解直接使用這組設定。
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
                // S2 R0 基準量測：固定 Threads=1，用基準設定跑 5 個 seed 當對照組，並觀察耗時原因。
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
                        $"solveTimeMs={trial.Metrics.SolveTimeMs:F0}");
                return 0;
            }

            // 4. 正式求解
            project.LoadConfig(projectConfig);
            bool solved = project.Solve(model, productionBaseline,
                onSolved: engine => TSP_MultiDimSetSolution.ReadAndValidate(engine, data).Print());
            return solved ? 0 : 1;
        }
    }
}
