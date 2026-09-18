using ModelInspector.Reporting;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace ModelInspector
{
    /// <summary>
    /// 吃一份既有模型檔（.lp / .mps / .sav），求解它，然後把框架在匯入模式下能提供的資訊全部輸出。
    /// 不綁任何題目專案——任何走 OptimFoundation 的專案匯出的模型檔都能丟進來。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // 框架的 Logging static ctor 也會設這個，但參數錯誤是在碰到 Logging 之前就輸出，
            // 那條路徑會在 Big5 console 上變亂碼。這裡先設，讓所有輸出路徑一致。
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            if (!InspectionOptions.TryParse(args, out var options, out string error))
            {
                Console.Error.WriteLine($"參數錯誤：{error}");
                InspectionOptions.PrintUsage();
                return 2;
            }

            Logging.SetLogFileName(options.Label);

            var projectConfig = new ProjectConfig
            {
                ProjectName = options.Label,
                EnableSolverLog = options.SolverLog,
                // 三個匯出全開：LP/MPS 是 import 後的模型，拿來跟來源檔對照可驗證 round-trip 有沒有失真。
                ExportLP = true,
                ExportMPS = true,
                ExportSol = true,
            };

            var solverConfig = new CplexConfig
            {
                TimeLimit = options.TimeLimit,
                MipGap = options.MipGap,
            };
            if (options.Threads.HasValue) solverConfig.Threads = options.Threads;
            if (options.Seed.HasValue) solverConfig.Seed = options.Seed;

            // FromFile 讓 ApplyTo 走 ImportModel；之後串的 step 會在匯入完成後才執行，
            // 所以這裡是唯一能在 Solve() 之前拿到 engine 開軌跡的時機（OptProject 沒有 pre-solve hook）。
            var model = OptModel.FromFile(options.ModelFile, options.Label);
            if (options.CaptureTrajectory)
                model.AddVariables(engine => engine.EnableTrajectory());

            Console.WriteLine($"[ModelInspector] 匯入 {options.ModelFile}");

            using var project = new OptProject(model)
                .UseConfig(() => projectConfig)
                .UseConfig(() => solverConfig);

            string? failure = null;
            try
            {
                project.Execute();
            }
            catch (Exception ex)
            {
                // 例外照樣往下走：Engine 在 Build() 之後就存在，能撈到的資訊還是要撈出來給人看。
                failure = $"{ex.GetType().Name}: {ex.GetBaseException().Message}";
                Console.Error.WriteLine($"[ModelInspector] 執行中斷：{failure}");
            }

            var inspection = ModelInspection.Capture(project, projectConfig, options, failure);
            var writer = new ReportWriter(inspection, options);
            var reportFiles = writer.WriteFiles();
            writer.WriteConsole(reportFiles);

            if (failure != null) return 3;
            return project.IsSuccess ? 0 : 1;
        }
    }
}
