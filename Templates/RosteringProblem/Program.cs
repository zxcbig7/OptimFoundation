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
            // Provenance：沿用原始 Template_CPLEX 的手動設定（epGap=0.03, timeLimit=100, workThreads=10），
            // 尚未經過 §8 Tuning 流程重新驗證；日後 promotion 時同步更新本註解與 TuningHistory.md。
            var productionBaseline = new CplexConfig
            {
                epGap = 0.03,
                timeLimit = 100,
                workThreads = 10,
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
                .AddVariables(engine => engine.BuildVars<VariableX_BelowAVG>(data.set_Employee))
                .AddVariables(engine => engine.BuildVars<VariableX_WeekendLT4>(data.set_Employee))
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
                var baseline = productionBaseline.Clone();
                var feasible = baseline.Clone();
                feasible.Emphasis = 1;
                var optimal = baseline.Clone();
                optimal.Emphasis = 2;

                var result = new OptExperiment("RosteringProblem-tuning-r1", "baseline vs emphasis=feasible vs emphasis=optimal")
                    .AddModel(model)
                    .AddConfig("r1-baseline", baseline)
                    .AddConfig("r1-emphasis=feasible", feasible)
                    .AddConfig("r1-emphasis=optimal", optimal)
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
