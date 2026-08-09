using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>RosteringProblem 的三態入口：import、experiment 與正式求解。</summary>
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

            // exp 的 log 檔名 MUST 在第一次寫入前設定，整次執行才收在同一包
            bool isExperiment = args.Any(arg => string.Equals(arg, "exp", StringComparison.OrdinalIgnoreCase));
            if (isExperiment)
                Logging.SetLogFileName("RosteringProblem_exp");

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
                ProjectName = "RosteringProblem",
                EnableSolverLog = true,
                ExportSol = true,
                ExportLP = true,
                ExportMPS = true,
            };
            // 唯一 production baseline/champion；experiment clone 它，prod 直接使用它。
            // Provenance：沿用原始 Template_CPLEX 的手動設定（MipGap=0.03, TimeLimit=100, Threads=10），
            // 尚未經過 §8 Tuning 流程重新驗證；日後 promotion 時同步更新本註解與 TuningHistory.md。
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
            // 模式 2：exp——掃 solver 設定，不做正式求解
            if (isExperiment)
            {
                // S2 R0 校準：環境已定版（Threads=10, ParallelMode=1），baseline × 5 tuning seeds 量 θ 與剖面。
                // seeds 6/7/8 保留為 holdout，全程不參與調參。
                var warmup = productionBaseline.Clone();
                warmup.ParallelMode = 1;

                CplexConfig Seeded(int seed)
                {
                    var config = productionBaseline.Clone();
                    config.ParallelMode = 1;
                    config.Seed = seed;
                    return config;
                }

                // S3 R3：剖面 Dual-bound → 候選 Symmetry=3（同質員工的對稱性消除）。一輪一顆，seed 為共同因子。
                CplexConfig SymmetryBreaking(int seed)
                {
                    var config = Seeded(seed);
                    config.Symmetry = 3;
                    return config;
                }

                var result = new OptExperiment(
                        "RosteringProblem-tuning-r3",
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
                                 $"obj={trial.Metrics.ObjectiveValue:G6} gap={trial.Metrics.MipGap:P2} runTimeMs={trial.Metrics.RunTimeMs:F0}");
                return 0;
            }

            // 模式 3（預設）：正式求解
            using var project = new OptProject(model)
                .UseConfig(() => projectConfig)
                .UseConfig(() => productionBaseline)
                .OnSolved(engine => RosteringProblemSolution.ReadAndValidate(engine, data).Print());

            bool solved = project.Execute();
            return solved ? 0 : 1;
        }
    }
}
