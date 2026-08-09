using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>FJSP_BASIC_BRICK 的三態入口：import、experiment 與正式求解。</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // 模式 1：import —— 攤平 Data/raw/ 的實例生成規格，產出標準 CSV（只有不規則來源才需要）
            if (args.Length >= 2 && args[0] == "import")
            {
                OptData.Load(() => new Dataload(args[1])).Export();
                return 0;
            }

            bool isExperiment = args.Any(arg => string.Equals(arg, "exp", StringComparison.OrdinalIgnoreCase));
            if (isExperiment)
                Logging.SetLogFileName("FJSP_BASIC_BRICK_exp");

            // ── 1. 材料 ────────────────────────────────────────────
            var data = OptData.Load(() => new Dataload());
            double exactlyOne = data.parameter_ExactlyOne.Single().QTY;
            double makespanFloor = data.parameter_MakespanFloor.Single().QTY;
            double softMakespanTarget = data.parameter_SoftMakespanTarget.Single().QTY;
            double makespanPenalty = data.parameter_MakespanPenalty.Single().QTY;
            double noOverlapForwardOffset = data.parameter_NoOverlapForwardOffset.Single().QTY;
            double noOverlapBackwardOffset = data.parameter_NoOverlapBackwardOffset.Single().QTY;
            double bigM = data.BigM;
            double makespanDeadline = data.MakespanDeadline;
            double infeasibleMakespanCap = data.InfeasibleMakespanCap;

            var projectConfig = new ProjectConfig
            {
                ProjectName = "FJSP_BASIC_BRICK",
                EnableSolverLog = false,
                ExportLP = true,
                DataId = "FJSP_BASIC_BRICK",
                UserId = "SYSTEM",
            };
            // Production baseline/champion：tuning promotion 只更新這一個設定來源。
            // Provenance：沿用重構前 demo 值——TimeLimit=90 已實測兩種組裝寫法各 3 次，全數 Status=Optimal 且 ObjVal 一致。
            var productionBaseline = new CplexConfig
            {
                MipGap = 1e-4,
                TimeLimit = 90,
                Threads = 8,
            };

            // ── 2. 模型（canonical：只用 hard constraint API）─────────
            var model = new OptModel("Canonical")
                .AddVariables(engine => engine.BuildVars<VariableB_Assign>(data.set_Lot, data.set_Operation, data.set_Eqp))
                .AddVariables(engine => engine.BuildVars<VariableB_Precede>(data.set_Lot, data.set_Operation, data.set_Lot, data.set_Operation))
                .AddVariables(engine => engine.BuildVars<VariableC_Start>(data.set_Lot, data.set_Operation))
                .AddVariables(engine => engine.BuildVars<VariableC_Complete>(data.set_Lot, data.set_Operation))
                .AddVariables(engine => engine.BuildVars<VariableC_Makespan>())
                .AddObjective(engine => new ObjectiveFunction().Build(engine))
                .AddConstraints(engine => new Constraint_AssignOneEqp(data.set_Lot, data.set_Operation, data.set_Eqp, exactlyOne).Build(engine))
                .AddConstraints(engine => new Constraint_CompleteDef(data.set_Lot, data.set_Operation, data.set_Eqp, data.parameter_ProcessTime).Build(engine))
                .AddConstraints(engine => new Constraint_RoutePrecedence(data.set_Lot, data.set_Operation).Build(engine))
                .AddConstraints(engine => new Constraint_NoOverlap(data.set_Lot, data.set_Operation, data.set_Eqp, bigM, noOverlapForwardOffset, noOverlapBackwardOffset).Build(engine))
                .AddConstraints(engine => new Constraint_MakespanDef(data.set_Lot, data.set_Operation).Build(engine))
                .AddConstraints(engine => new Constraint_MakespanWindow(makespanFloor, makespanDeadline).Build(engine));

            // ── 3. 環境 ────────────────────────────────────────────
            // 模式 2：exp —— 掃 solver 設定 + 展示兩個具名 Phase 3 模型結構 variant，不做正式求解
            if (isExperiment)
            {
                // Phase 3 demo variant：canonical + soft makespan target（示範 CreateLeSoft）。§4.5 天條：
                // soft constraint NEVER 進 canonical production 組裝，只能是具名 experiment variant，故獨立整份重列（非包裝呼叫）。
                var softModel = new OptModel("Canonical-SoftMakespanDemo")
                    .AddVariables(engine => engine.BuildVars<VariableB_Assign>(data.set_Lot, data.set_Operation, data.set_Eqp))
                    .AddVariables(engine => engine.BuildVars<VariableB_Precede>(data.set_Lot, data.set_Operation, data.set_Lot, data.set_Operation))
                    .AddVariables(engine => engine.BuildVars<VariableC_Start>(data.set_Lot, data.set_Operation))
                    .AddVariables(engine => engine.BuildVars<VariableC_Complete>(data.set_Lot, data.set_Operation))
                    .AddVariables(engine => engine.BuildVars<VariableC_Makespan>())
                    .AddObjective(engine => new ObjectiveFunction().Build(engine))
                    .AddConstraints(engine => new Constraint_AssignOneEqp(data.set_Lot, data.set_Operation, data.set_Eqp, exactlyOne).Build(engine))
                    .AddConstraints(engine => new Constraint_CompleteDef(data.set_Lot, data.set_Operation, data.set_Eqp, data.parameter_ProcessTime).Build(engine))
                    .AddConstraints(engine => new Constraint_RoutePrecedence(data.set_Lot, data.set_Operation).Build(engine))
                    .AddConstraints(engine => new Constraint_NoOverlap(data.set_Lot, data.set_Operation, data.set_Eqp, bigM, noOverlapForwardOffset, noOverlapBackwardOffset).Build(engine))
                    .AddConstraints(engine => new Constraint_MakespanDef(data.set_Lot, data.set_Operation).Build(engine))
                    .AddConstraints(engine => new Constraint_MakespanWindow(makespanFloor, makespanDeadline).Build(engine))
                    .AddConstraints(engine => new Constraint_MakespanTargetSoft(softMakespanTarget, makespanPenalty).Build(engine));

                // Phase 3 demo variant：canonical + 保證 infeasible 的 makespan 上限（示範 IIS 輸出，見 IISs/*.ilp）。
                var infeasibleModel = new OptModel("Canonical-InfeasibleCapDemo")
                    .AddVariables(engine => engine.BuildVars<VariableB_Assign>(data.set_Lot, data.set_Operation, data.set_Eqp))
                    .AddVariables(engine => engine.BuildVars<VariableB_Precede>(data.set_Lot, data.set_Operation, data.set_Lot, data.set_Operation))
                    .AddVariables(engine => engine.BuildVars<VariableC_Start>(data.set_Lot, data.set_Operation))
                    .AddVariables(engine => engine.BuildVars<VariableC_Complete>(data.set_Lot, data.set_Operation))
                    .AddVariables(engine => engine.BuildVars<VariableC_Makespan>())
                    .AddObjective(engine => new ObjectiveFunction().Build(engine))
                    .AddConstraints(engine => new Constraint_AssignOneEqp(data.set_Lot, data.set_Operation, data.set_Eqp, exactlyOne).Build(engine))
                    .AddConstraints(engine => new Constraint_CompleteDef(data.set_Lot, data.set_Operation, data.set_Eqp, data.parameter_ProcessTime).Build(engine))
                    .AddConstraints(engine => new Constraint_RoutePrecedence(data.set_Lot, data.set_Operation).Build(engine))
                    .AddConstraints(engine => new Constraint_NoOverlap(data.set_Lot, data.set_Operation, data.set_Eqp, bigM, noOverlapForwardOffset, noOverlapBackwardOffset).Build(engine))
                    .AddConstraints(engine => new Constraint_MakespanDef(data.set_Lot, data.set_Operation).Build(engine))
                    .AddConstraints(engine => new Constraint_MakespanWindow(makespanFloor, makespanDeadline).Build(engine))
                    .AddConstraints(engine => new Constraint_MakespanInfeasibleCap(infeasibleMakespanCap).Build(engine));

                var baseline = productionBaseline.Clone();
                var feasibility = baseline.Clone();
                feasibility.Emphasis = 1;
                var optimal = baseline.Clone();
                optimal.Emphasis = 2;

                var result = new OptExperiment(
                        "FJSP_BASIC_BRICK-tuning-r1",
                        "canonical vs soft-makespan-demo vs infeasible-cap-demo × 3 個 MIP emphasis")
                    .AddModel(model)
                    .AddModel(softModel)
                    .AddModel(infeasibleModel)
                    .AddConfig("r1-balanced", baseline)
                    .AddConfig("r1-emphasis=feasibility", feasibility)
                    .AddConfig("r1-emphasis=optimal", optimal)
                    .Run();

                foreach (var trial in result.Trials)
                    Logging.Info($"[Experiment] {trial.Label} status={trial.Metrics.Status} runTimeMs={trial.Metrics.RunTimeMs:F0}");
                return 0;
            }

            // 模式 3（預設）：正式求解
            using var project = new OptProject(model)
                .UseConfig(() => projectConfig)
                .UseConfig(() => productionBaseline)
                .OnSolved(engine => FJSP_BASIC_BRICKSolution.ReadAndValidate(engine, data).Print());

            return project.Execute() ? 0 : 1;
        }
    }
}
