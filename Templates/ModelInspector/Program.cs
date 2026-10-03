using ModelInspector.Reporting;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace ModelInspector
{
    /// <summary>
    /// 讀取既有模型檔（.lp / .mps / .sav）並求解，輸出模型統計、求解結果與相關檔案清單。
    /// 可讀取任何 OptimFoundation 專案匯出的模型檔，不必引用原專案的模型類別。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // Logging 的靜態建構子也會設定 UTF-8，但命令列參數錯誤可能在 Logging 初始化前就輸出，
            // 因此在此先設定 Console 編碼，避免使用 Big5 的終端機顯示錯誤訊息時出現亂碼。
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            if (!InspectionOptions.TryParse(args, out var options, out string error))
            {
                Console.Error.WriteLine($"參數錯誤：{error}");
                InspectionOptions.PrintUsage();
                return 2;
            }

            using var project = new OptProject(options.Label);

            var projectConfig = new ProjectConfig
            {
                EnableSolverLog = options.SolverLog,
                // 同時匯出 LP、MPS 與解檔；可將重新匯出的模型與來源檔比較，檢查讀入再寫出後是否改變。
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

            // 透過 ReadModel 匯入模型；是否記錄求解軌跡則在 BeforeSolve 中設定，於模型載入後、求解前啟用。
            var model = OptModel.ReadModel(options.ModelFile, options.Label);

            Console.WriteLine($"[ModelInspector] 匯入 {options.ModelFile}");

            project.LoadConfig(projectConfig);

            string? failure = null;
            try
            {
                project.Solve(model, solverConfig,
                    beforeSolve: options.CaptureTrajectory ? engine => engine.EnableTrajectory() : null);
            }
            catch (Exception ex)
            {
                // 記下例外後仍繼續產生報告，保留已建立的 Engine 所能提供的模型與錯誤資訊。
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
