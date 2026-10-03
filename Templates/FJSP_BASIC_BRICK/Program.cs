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
            // 由 OptProject 管理 log、資料夾與檔案保留天數；實驗和正式求解都透過它執行。
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
            // 正式求解使用這組設定；調參結果確認較好後，統一更新在這裡。
            // 設定來源：沿用重構前範例值；TimeLimit=90 已對兩種模型組裝寫法各測 3 次，皆為 Optimal，且目標值相同。
            var productionBaseline = new CplexConfig
            {
                MipGap = 1e-4,
                TimeLimit = 90,
                Threads = 8,
            };

            // ── 2. 正式模型：全部使用必須滿足的限制式 ─────────
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
            // 模式 2：exp，比較 solver 設定，並求解兩個 Phase 3 示範模型：允許超時但加罰分，以及刻意造成無可行解。
            if (isExperiment)
            {
                // Phase 3 示範：在正式模型加上最晚完工時間目標，超過目標時加罰分（CreateLessEqualSoft，見 §4.5）。
                // 軟性限制只用於具名實驗模型，因此在此重新列出完整組裝內容，保持正式模型的限制不變。
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

                // Phase 3 示範：在正式模型加入不可能達到的完工時間上限，觸發無可行解的衝突分析（IISs/*.ilp）。
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

                var result = project.Experiment(
                        "tuning-r1",
                        "canonical vs soft-makespan-demo vs infeasible-cap-demo × 3 個 MIP emphasis")
                    .AddModel(model)
                    .AddModel(softModel)
                    .AddModel(infeasibleModel)
                    .AddConfig("r1-balanced", baseline)
                    .AddConfig("r1-emphasis=feasibility", feasibility)
                    .AddConfig("r1-emphasis=optimal", optimal)
                    .Run();

                foreach (var trial in result.Trials)
                    Logging.Info($"[Experiment] {trial.Label} status={trial.Metrics.Status} solveTimeMs={trial.Metrics.SolveTimeMs:F0}");
                return 0;
            }

            // 模式 3（預設）：正式求解
            project.LoadConfig(projectConfig);
            bool solved = project.Solve(model, productionBaseline,
                onSolved: engine => FJSP_BASIC_BRICKSolution.ReadAndValidate(engine, data).Print());
            return solved ? 0 : 1;
        }
    }
}
