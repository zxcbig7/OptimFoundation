using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace Template;

internal static class Program
{
    private static int Main(string[] args)
    {
        // 1. import-data：整理 raw 資料成 canonical CSV，完成後立即離開
        if (args.Length >= 2 && args[0] == "import-data")
        {
            var imported = OptData.Load(() => new Dataload(args[1]));
            TemplateSolution.ValidateData(imported);
            imported.Export();
            return 0;
        }

        bool isExperiment = args.Any(arg =>
            string.Equals(arg, "exp", StringComparison.OrdinalIgnoreCase));
        int readModelAt = Array.IndexOf(args, "read-model");
        string? modelFile = readModelAt >= 0 && readModelAt + 1 < args.Length ? args[readModelAt + 1] : null;
        if (readModelAt >= 0 && modelFile == null)
        {
            Console.Error.WriteLine("read-model 需要模型檔路徑，例：dotnet run -- read-model <file> [exp]");
            return 2;
        }

        using var project = new OptProject("Template");

        // 2. 設定
        var projectConfig = new ProjectConfig
        {
            EnableSolverLog = true,
            ExportLP = true,
            ExportSol = true,
        };

        var productionBaseline = new CplexConfig
        {
            MipGap = 0.01,
            TimeLimit = 300,
            Threads = 8,
            ParallelMode = 1,
            Seed = 11,
        };

        // 3. 模型來源：read-model 讀模型檔、不讀 CSV；否則資料 → 資料驗收 → canonical model
        Dataload? data = null;
        OptModel model;
        if (modelFile != null)
        {
            model = OptModel.ReadModel(modelFile);
        }
        else
        {
            data = OptData.Load(() => new Dataload());
            TemplateSolution.ValidateData(data);
            model = BuildModel(data, "Canonical");
        }

        // 4. 環境
        if (isExperiment)
        {
            // read-model 的實驗名加上模型名，紀錄檔跟 canonical 同一輪的分得開
            string experimentName = modelFile == null ? "tuning-r0" : $"tuning-r0-{model.Name}";
            var experiment = project.Experiment(experimentName, "R0 校準：每個模型 x baseline x 5 seeds")
                .CaptureTrajectory(true);
            experiment.AddModel(model);

            // 第二個 instance：同一套公式，資料由 InMemoryDataSource 直接餵入，不經 CSV
            if (data != null)
            {
                var scaled = OptData.Load(() => new Dataload(data.CreateScaledSource(2.0)));
                TemplateSolution.ValidateData(scaled);
                experiment.AddModel(BuildModel(scaled, "Scaled"));
            }

            foreach (int seed in new[] { 11, 22, 33, 44, 55 })
            {
                var config = productionBaseline.Clone();
                config.Seed = seed;
                experiment.AddSolverConfig($"r0-baseline-s{seed}", config);
            }

            var result = experiment.Run();
            foreach (var trial in result.Trials)
            {
                Logging.Info(
                    $"[試跑完成] 模型名稱={trial.Model} 名稱={trial.Label} " +
                    $"狀態={trial.Metrics.Status} " +
                    $"目標值={trial.Metrics.ObjectiveValue} " +
                    $"耗時毫秒={trial.Metrics.SolveTimeMs:F0}");
            }

            return 0;
        }

        // 正式求解；read-model 沒有資料，不跑解驗證
        project.Production()
            .AddProjectConfig(projectConfig)
            .AddModel(model)
            .AddSolverConfig("production", productionBaseline)
            .CaptureTrajectory(true)
            .OnSolved(data == null ? null : engine => TemplateSolution.ReadAndValidate(engine, data, new CsvSolutionSink()))
            .Run();
        return project.IsSuccess ? 0 : 1;
    }

    private static OptModel BuildModel(Dataload data, string name)
    {
        double penalty = data.parameter_Scalar.Single().QTY;

        return new OptModel(name)
            .AddVariables<VariableB_Binary>(data.set_StringKey)
            .AddVariables<VariableI_Integer>(data.set_SparsePair)
            .AddVariables<VariableC_Continuous>(data.set_StringKey, data.set_DateKey)
            .AddVariables<VariableC_ZeroDim>()
            .AddObjective<ObjectiveFunction>(
                data.set_StringKey,
                data.set_DateKey,
                data.parameter_OneDim,
                penalty)
            .AddConstraints<Constraint_Equal>(
                data.set_StringKey,
                data.set_DateKey,
                data.set_SparsePair,
                data.parameter_TwoDim)
            .AddConstraints<Constraint_LessEqual>(
                data.set_SparsePair,
                data.parameter_TwoDim)
            .AddConstraints<Constraint_GreaterEqual>(
                data.set_DateKey,
                data.set_SparsePair)
            .AddConstraints<Constraint_Range>(
                data.set_StringKey)
            .AddConstraints<Constraint_LessEqualSoft>(
                data.set_StringKey,
                penalty)
            .AddMIPStart(() => TemplateSolution.CreateStartValues(data), "CheapestKey");
    }
}
