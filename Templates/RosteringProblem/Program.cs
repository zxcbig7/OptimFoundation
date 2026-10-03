using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>RosteringProblem 的程式入口，提供範例資料產生、參數比較實驗與正式求解三種模式。</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // 模式 1：import——本專案沒有不規則外部來源，import 改為以固定種子重新生成範例排班資料
            // 並攤平成標準 CSV（見 Data/Dataload.cs 的 Dataload(string) 建構子）。
            if (args.Length >= 2 && args[0] == "import")
            {
                OptData.Load(() => new Dataload(args[1])).Export();
                return 0;
            }

            bool isExperiment = args.Any(arg => string.Equals(arg, "exp", StringComparison.OrdinalIgnoreCase));
            // 由 OptProject 管理 log、資料夾與檔案保留天數；實驗和正式求解都透過它執行。
            using var project = new OptProject("RosteringProblem");

            // ── 1. 材料 ────────────────────────────────────────────
            var data = OptData.Load(() => new Dataload());

            double one = data.parameter_One.Single().QTY;
            double sixDayWindow = data.parameter_SixDayWindow.Single().QTY;
            double nightToDayWindow = data.parameter_NightToDayWindow.Single().QTY;
            double offOneDayWindow = data.parameter_OffOneDayWindow.Single().QTY;
            double doubleOffWindow = data.parameter_DoubleOffWindow.Single().QTY;
            double doubleOffThreshold = data.parameter_DoubleOffThreshold.Single().QTY;
            double weekendOffThreshold = data.parameter_WeekendOffThreshold.Single().QTY;

            double offOneDayPenalty = data.parameter_OffOneDayPenalty.Single().QTY;
            double sixDayPenalty = data.parameter_SixDayPenalty.Single().QTY;
            double groupMismatchPenalty = data.parameter_GroupMismatchPenalty.Single().QTY;
            double nightToDayPenalty = data.parameter_NightToDayPenalty.Single().QTY;
            double doubleOffLT2Penalty = data.parameter_DoubleOffLT2Penalty.Single().QTY;
            double belowAvgPenalty = data.parameter_BelowAVGPenalty.Single().QTY;
            double weekend4DayPenalty = data.parameter_Weekend4DayPenalty.Single().QTY;

            var projectConfig = new ProjectConfig
            {
                EnableSolverLog = true,
                ExportSol = true,
                ExportLP = true,
                ExportMPS = true,
            };
            // 正式求解直接使用這組設定；實驗則先複製一份，再調整要比較的參數。
            // 設定來源：沿用原始 Template_CPLEX 的手動設定（MipGap=0.03, TimeLimit=100, Threads=10），
            // 尚未依 §8 調參流程重新驗證；日後採用新設定時，同步更新本註解與 TuningHistory.md。
            var productionBaseline = new CplexConfig
            {
                MipGap = 0.03,
                TimeLimit = 100,
                Threads = 10,
            };

            // ── 2. 模型 ────────────────────────────────────────────
            var model = new OptModel("Canonical")
                .AddVariables(engine => engine.BuildVars<VariableB_ShiftAssign>(data.set_Date, data.set_Employee, data.set_Group))
                .AddVariables(engine => engine.BuildVars<VariableB_GroupMismatch>(data.set_Date, data.set_Employee))
                .AddVariables(engine => engine.BuildVars<VariableB_NightToDay>(data.set_Date, data.set_Employee))
                .AddVariables(engine => engine.BuildVars<VariableB_DoubleOffFlag>(data.set_Date, data.set_Employee))
                .AddVariables(engine => engine.BuildVars<VariableB_DoubleOffLT2>(data.set_Employee))
                .AddVariables(engine => engine.BuildVars<VariableB_Off1Day>(data.set_Date, data.set_Employee))
                .AddVariables(engine => engine.BuildVars<VariableB_SixDayWork>(data.set_Date, data.set_Employee))
                .AddVariables(engine => engine.BuildVars<VariableC_BelowAVG>(data.set_Employee))
                .AddVariables(engine => engine.BuildVars<VariableC_WeekendLT4>(data.set_Employee))
                .AddObjective(engine => new ObjectiveFunction(
                    data.set_Date,
                    data.set_Employee,
                    offOneDayPenalty,
                    sixDayPenalty,
                    groupMismatchPenalty,
                    nightToDayPenalty,
                    doubleOffLT2Penalty,
                    belowAvgPenalty,
                    weekend4DayPenalty).Build(engine))
                .AddConstraints(engine => new Constraint_FullfillDemand(
                    data.set_Date, data.set_Employee, data.set_Group, data.parameter_ShiftDemand).Build(engine))
                .AddConstraints(engine => new Constraint_OneGroup(
                    data.set_Date, data.set_Employee, data.set_Group, one).Build(engine))
                .AddConstraints(engine => new Constraint_PreAssign(
                    data.parameter_PreAssign, one).Build(engine))
                .AddConstraints(engine => new Constraint_SixDayWork(
                    data.set_Date, data.set_Employee, sixDayWindow, one).Build(engine))
                .AddConstraints(engine => new Constraint_NightToDay(
                    data.set_Date, data.set_Employee, data.parameter_NightToDay, nightToDayWindow, one).Build(engine))
                .AddConstraints(engine => new Constraint_OffOneDay(
                    data.set_Date, data.set_Employee, offOneDayWindow, one).Build(engine))
                .AddConstraints(engine => new Constraint_CrossGroup(
                    data.set_Date, data.set_Employee, data.parameter_CrossGroup).Build(engine))
                .AddConstraints(engine => new Constraint_BelowAVG(
                    data.set_Date, data.set_Employee, data.parameter_ShiftDemand).Build(engine))
                .AddConstraints(engine => new Constraint_WeekendLT4(
                    data.set_Date, data.set_Employee, weekendOffThreshold).Build(engine))
                .AddConstraints(engine => new Constraint_DoubleOffLT2(
                    data.set_Date, data.set_Employee, doubleOffWindow, doubleOffThreshold, one).Build(engine));

            // ── 3. 環境 ────────────────────────────────────────────
            // 模式 2：exp，使用不同 solver 設定重複求解，記錄比較結果。
            if (isExperiment)
            {
                // S2 R0 基準量測：固定 Threads=10、ParallelMode=1，用基準設定跑 5 個 seed 當對照組，並觀察耗時原因。
                // seed 6、7、8 留到最後驗證已選設定，不參與調參比較。
                var warmup = productionBaseline.Clone();
                warmup.ParallelMode = 1;

                CplexConfig Seeded(int seed)
                {
                    var config = productionBaseline.Clone();
                    config.ParallelMode = 1;
                    config.Seed = seed;
                    return config;
                }

                // S3 R3：針對最佳界改善較慢的情況，測試 Symmetry=3 能否減少同質員工的重複排班搜尋；只改這個參數，使用相同 seed 比較。
                CplexConfig SymmetryBreaking(int seed)
                {
                    var config = Seeded(seed);
                    config.Symmetry = 3;
                    return config;
                }

                var result = project.Experiment(
                        "tuning-r3",
                        "S3 R3: baseline vs Symmetry=3 x 5 seeds, rotated order")
                    .AddModel(model)
                    .AddConfig("warmup-exclude", warmup)
                    .AddConfig("r3-s1-baseline", Seeded(1))
                    .AddConfig("r3-s1-symmetry3", SymmetryBreaking(1))
                    .AddConfig("r3-s2-symmetry3", SymmetryBreaking(2))
                    .AddConfig("r3-s2-baseline", Seeded(2))
                    .AddConfig("r3-s3-baseline", Seeded(3))
                    .AddConfig("r3-s3-symmetry3", SymmetryBreaking(3))
                    .AddConfig("r3-s4-symmetry3", SymmetryBreaking(4))
                    .AddConfig("r3-s4-baseline", Seeded(4))
                    .AddConfig("r3-s5-baseline", Seeded(5))
                    .AddConfig("r3-s5-symmetry3", SymmetryBreaking(5))
                    .Run();

                foreach (var trial in result.Trials)
                    Logging.Info($"[Experiment] {trial.Label} status={trial.Metrics.Status} " +
                                 $"obj={trial.Metrics.ObjectiveValue:G6} gap={trial.Metrics.Gap:P2} solveTimeMs={trial.Metrics.SolveTimeMs:F0}");
                return 0;
            }

            // 模式 3（預設）：正式求解
            // 模型建完後存一份 .sav 供 Templates/ModelInspector 匯入檢視。
            // 用 .sav 而非既有的 LP/MPS：後兩者是文字格式、係數經十進位截斷，讀回來無法精確重現本次求解。
            // 在限制式建立完後、求解前就匯出，讓無可行解時也有模型檔可供檢查。
            model.AddConstraints(engine =>
                engine.ExportModel($"{engine.ModelName}_SAV_{engine.StartTime}.sav"));

            project.LoadConfig(projectConfig);
            bool solved = project.Solve(model, productionBaseline,
                onSolved: engine => RosteringProblemSolution.ReadAndValidate(engine, data).Print());
            return solved ? 0 : 1;
        }
    }
}
