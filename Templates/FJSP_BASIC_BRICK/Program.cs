using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>FJSP_BASIC_BRICK 的程式入口，提供資料匯入、參數比較實驗與正式求解三種模式。</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // 模式 1：import，讀取 FolderDir.Input 下 raw/ 的規模與亂數設定，產生求解用的標準 CSV。
            if (args.Length >= 2 && args[0] == "import")
            {
                OptData.Load(() => new Dataload(args[1])).Export();
                return 0;
            }

            bool isExperiment = args.Any(arg => string.Equals(arg, "exp", StringComparison.OrdinalIgnoreCase));
            // OptProject 管理 log、資料夾與保留期。
            using var project = new OptProject("FJSP_BASIC_BRICK");

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
                EnableSolverLog = false,
                ExportLP = true,
            };
            // 正式求解設定；實驗先 Clone 再修改。
            var productionBaseline = new CplexConfig
            {
                MipGap = 1e-4,
                TimeLimit = 90,
                Threads = 8,
            };

            // ── 2. 正式模型：全部使用必須滿足的限制式 ─────────
            var model = new OptModel("Canonical")
                .AddVariables<VariableB_Assign>(data.set_Lot, data.set_Operation, data.set_Eqp)
                .AddVariables<VariableB_Precede>(data.set_Lot, data.set_Operation, data.set_Lot, data.set_Operation)
                .AddVariables<VariableC_Start>(data.set_Lot, data.set_Operation)
                .AddVariables<VariableC_Complete>(data.set_Lot, data.set_Operation)
                .AddVariables<VariableC_Makespan>()
                .AddObjective<ObjectiveFunction>()
                .AddConstraints<Constraint_AssignOneEqp>(data.set_Lot, data.set_Operation, data.set_Eqp, exactlyOne)
                .AddConstraints<Constraint_CompleteDef>(data.set_Lot, data.set_Operation, data.set_Eqp, data.parameter_ProcessTime)
                .AddConstraints<Constraint_RoutePrecedence>(data.set_Lot, data.set_Operation)
                .AddConstraints<Constraint_NoOverlap>(data.set_Lot, data.set_Operation, data.set_Eqp, bigM, noOverlapForwardOffset, noOverlapBackwardOffset)
                .AddConstraints<Constraint_MakespanDef>(data.set_Lot, data.set_Operation)
                .AddConstraints<Constraint_MakespanWindow>(makespanFloor, makespanDeadline);

            // ── 3. 環境 ────────────────────────────────────────────
            // 模式 2：exp，比較 solver 設定，並求解兩個 Phase 3 示範模型：允許超時但加罰分，以及刻意造成無可行解。
            if (isExperiment)
            {
                // 實驗模型加入軟性完工目標；獨立組裝以保持正式模型不變。
                var softModel = new OptModel("Canonical-SoftMakespanDemo")
                    .AddVariables<VariableB_Assign>(data.set_Lot, data.set_Operation, data.set_Eqp)
                    .AddVariables<VariableB_Precede>(data.set_Lot, data.set_Operation, data.set_Lot, data.set_Operation)
                    .AddVariables<VariableC_Start>(data.set_Lot, data.set_Operation)
                    .AddVariables<VariableC_Complete>(data.set_Lot, data.set_Operation)
                    .AddVariables<VariableC_Makespan>()
                    .AddObjective<ObjectiveFunction>()
                    .AddConstraints<Constraint_AssignOneEqp>(data.set_Lot, data.set_Operation, data.set_Eqp, exactlyOne)
                    .AddConstraints<Constraint_CompleteDef>(data.set_Lot, data.set_Operation, data.set_Eqp, data.parameter_ProcessTime)
                    .AddConstraints<Constraint_RoutePrecedence>(data.set_Lot, data.set_Operation)
                    .AddConstraints<Constraint_NoOverlap>(data.set_Lot, data.set_Operation, data.set_Eqp, bigM, noOverlapForwardOffset, noOverlapBackwardOffset)
                    .AddConstraints<Constraint_MakespanDef>(data.set_Lot, data.set_Operation)
                    .AddConstraints<Constraint_MakespanWindow>(makespanFloor, makespanDeadline)
                    .AddConstraints<Constraint_MakespanTargetSoft>(softMakespanTarget, makespanPenalty);

                // 加入不可達上限，示範無解時的 IIS 分析。
                var infeasibleModel = new OptModel("Canonical-InfeasibleCapDemo")
                    .AddVariables<VariableB_Assign>(data.set_Lot, data.set_Operation, data.set_Eqp)
                    .AddVariables<VariableB_Precede>(data.set_Lot, data.set_Operation, data.set_Lot, data.set_Operation)
                    .AddVariables<VariableC_Start>(data.set_Lot, data.set_Operation)
                    .AddVariables<VariableC_Complete>(data.set_Lot, data.set_Operation)
                    .AddVariables<VariableC_Makespan>()
                    .AddObjective<ObjectiveFunction>()
                    .AddConstraints<Constraint_AssignOneEqp>(data.set_Lot, data.set_Operation, data.set_Eqp, exactlyOne)
                    .AddConstraints<Constraint_CompleteDef>(data.set_Lot, data.set_Operation, data.set_Eqp, data.parameter_ProcessTime)
                    .AddConstraints<Constraint_RoutePrecedence>(data.set_Lot, data.set_Operation)
                    .AddConstraints<Constraint_NoOverlap>(data.set_Lot, data.set_Operation, data.set_Eqp, bigM, noOverlapForwardOffset, noOverlapBackwardOffset)
                    .AddConstraints<Constraint_MakespanDef>(data.set_Lot, data.set_Operation)
                    .AddConstraints<Constraint_MakespanWindow>(makespanFloor, makespanDeadline)
                    .AddConstraints<Constraint_MakespanInfeasibleCap>(infeasibleMakespanCap);

                var baseline = productionBaseline.Clone();
                var feasibility = baseline.Clone();
                feasibility.Emphasis = 1;
                var optimal = baseline.Clone();
                optimal.Emphasis = 2;

                var result = project.Experiment(
                        "tuning-r1",
                        "canonical vs soft-makespan-demo vs infeasible-cap-demo × 3 個 MIP emphasis")
                    .AddModel(model)
                    .AddModel(softModel)
                    .AddModel(infeasibleModel)
                    .AddSolverConfig("r1-balanced", baseline)
                    .AddSolverConfig("r1-emphasis=feasibility", feasibility)
                    .AddSolverConfig("r1-emphasis=optimal", optimal)
                    .Run();

                foreach (var trial in result.Trials)
                    Logging.Info($"[Experiment] {trial.Label} status={trial.Metrics.Status} solveTimeMs={trial.Metrics.SolveTimeMs:F0}");
                return 0;
            }

            // 模式 3（預設）：正式求解
            project.Production()
                .AddProjectConfig(projectConfig)
                .AddModel(model)
                .AddSolverConfig("production", productionBaseline)
                .OnSolved(engine => FJSP_BASIC_BRICKSolution.ReadAndValidate(engine, data).Print())
                .Run();
            return project.IsSuccess ? 0 : 1;
        }
    }
}
