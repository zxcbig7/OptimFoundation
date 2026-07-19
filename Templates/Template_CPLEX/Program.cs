
using OptimFoundation.Core;
using OptimFoundation.Cplex;

using SandBox;
using SandBox.Data;
using SandBox.Constraints;
using SandBox.VariableClass;
using SandBox.VariablesClass;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MyApp
{
    internal class Program
    {

        static void Main(string[] args)
        {
            // Model1 vs Model2 交叉實驗：dotnet run -- cross
                CrossExperiment.Run();
                return;
            if (args.Length > 0 && args[0] == "cross")
            {
            }

            // tuning 實驗環境示範：dotnet run -- experiment
            if (args.Length > 0 && args[0] == "experiment")
            {
                ExperimentDemo.Run();
                return;
            }


            // OptModel（Fluent 管線）打包版：dotnet run -- optmodel
            if (args.Length > 0 && args[0] == "optmodel")
            {
                var dataload = OptData.Load(() => new Dataload());

                var Model1 = new OptModel("RosteringProblem")
                    .UseConfig(() => new CplexConfig
                    {
                        epGap = 0.03,
                        timeLimit = 100,
                        workThreads = 10,
                        enableLog = true,
                        exportSol = true,
                        exportLP = true,
                        exportMPS = true
                    })
                    .AddVariables(e => new VariableCreate(dataload, e).Build())
                    .AddModel(e => new BuildModel(dataload, e).Build())
                    .OnSolved(e => dataload.WriteToCSV(e));


                var Model2 = new OptModel("RosteringProblem")
                .UseConfig(() => new CplexConfig
                {
                    epGap = 0.03,
                    timeLimit = 100,
                    workThreads = 10,
                    enableLog = true,
                    exportSol = true,
                    exportLP = true,
                    exportMPS = true
                })
                .AddVariables(e => new VariableCreate(dataload, e).Build())
                .AddModel(e => new BuildModel(dataload, e).Build())

                .AddModel(e => new Constraint_TEST(dataload.Date, dataload.Employee, dataload.parameter_ShiftDemand, e).Build())

                .OnSolved(e => dataload.WriteToCSV(e));

                Model1.Execute();
                Logging.Info($"整體運作時間:", Model1.totalTimer);

                return;
            }

            // OptModel 拆開版（不經 VariableCreate / BuildModel，逐一註冊變數與約束）：dotnet run -- optmodel-expanded
            if (args.Length > 0 && args[0] == "optmodel-expanded")
            {
                var dataload = OptData.Load(() => new Dataload());
                using (var m = new OptModel("RosteringProblem")
                    .UseConfig(() => new CplexConfig
                    {
                        epGap = 0.03,
                        timeLimit = 100,
                        workThreads = 10,
                        enableLog = true,
                        exportSol = true,
                        exportLP = true,
                        exportMPS = true
                    })
                    // 變數（原 VariableCreate 內容）
                    .AddVariables(e => e.BuildBVs<VariableB_ShiftAssign>(dataload.Date, dataload.Employee, dataload.Group))
                    .AddVariables(e => e.BuildBVs<VariableB_GroupMismatch>(dataload.Date, dataload.Employee))
                    .AddVariables(e => e.BuildBVs<VariableB_NightToDay>(dataload.Date, dataload.Employee))
                    .AddVariables(e => e.BuildBVs<VariableB_DoubleOffFlag>(dataload.Date, dataload.Employee))
                    .AddVariables(e => e.BuildBVs<VariableB_DoubleOffLT2>(dataload.Employee))
                    .AddVariables(e => e.BuildBVs<VariableB_Off1Day>(dataload.Date, dataload.Employee))
                    .AddVariables(e => e.BuildBVs<VariableB_SixDayWork>(dataload.Date, dataload.Employee))
                    .AddVariables(e => e.BuildCVs<VariableX_BelowAVG>(dataload.Employee))
                    .AddVariables(e => e.BuildCVs<VariableX_WeekendLT4>(dataload.Employee))
                    // 目標式（原 BuildModel 內容）
                    .AddModel(e => new ObjectiveFunction(
                        dataload.Date, dataload.Employee,
                        dataload.Penalty_OffOneDay,
                        dataload.Penalty_SixDay,
                        dataload.Penalty_GroupMismatch,
                        dataload.Penalty_NightToDay,
                        dataload.Penalty_DoubleOffLT2,
                        dataload.Penalty_BelowAVG,
                        dataload.Penalty_Weekend4Day,
                        e).Build())
                    // 限制式
                    .AddModel(e => new Constraint_FullfillDemand(dataload.Date, dataload.Employee, dataload.Group, dataload.parameter_ShiftDemand, e).Build())
                    .AddModel(e => new Constraint_OneGroup(dataload.Date, dataload.Employee, dataload.Group, e).Build())
                    .AddModel(e => new Constraint_PreAssign(dataload.parameter_PreAssign, e).Build())
                    .AddModel(e => new Constraint_SixDayWork(dataload.Date, dataload.Employee, e).Build())
                    .AddModel(e => new Constraint_NightToDay(dataload.Date, dataload.Employee, dataload.parameter_NightToDay, e).Build())
                    .AddModel(e => new Constraint_OffOneDay(dataload.Date, dataload.Employee, e).Build())
                    .AddModel(e => new Constraint_CrossGroup(dataload.Date, dataload.Employee, dataload.parameter_CrossGroup, e).Build())
                    .AddModel(e => new Constraint_BelowAVG(dataload.Date, dataload.Employee, dataload.parameter_ShiftDemand, e).Build())
                    .AddModel(e => new Constraint_WeekendLT4(dataload.Date, dataload.Employee, e).Build())
                    .AddModel(e => new Constraint_DoubleOffLT2(dataload.Date, dataload.Employee, e).Build())
                    .OnSolved(e => dataload.WriteToCSV(e)))
                {
                    m.Execute();
                    Logging.Info($"整體運作時間:", m.totalTimer);
                }
                return;
            }

            using (RosteringProblem project = new RosteringProblem())
            {
                project.Execute();
                Logging.Info($"整體運作時間:", project.totalTimer);
            }
        }
    }
}