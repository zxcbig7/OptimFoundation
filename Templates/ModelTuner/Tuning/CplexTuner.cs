using System.Globalization;
using OptimFoundation.Core;
using OptimFoundation.Cplex;
using CplexParam = ILOG.CPLEX.Cplex.Param;

namespace ModelTuner
{
    /// <summary>
    /// 在 S2.5 階段使用 CPLEX 內建調參工具，讀取 Instances/tune 的模型檔（solver-tuning-guide §3.6）。
    /// 框架尚未提供 <c>TuneParam</c> 入口（guide 附錄 B），所以繼承 OptEngine 來存取 protected <c>Model</c>，
    /// 讓 productionBaseline 的全部設定仍透過 <c>LoadConfig()</c> 套用到 CPLEX；框架日後提供 AutoTune 時再改用它。
    /// 工具建議的每個參數都要分開做比較實驗，通過 §4 驗證後才能採用為正式設定。
    /// </summary>
    internal sealed class CplexTuner : OptEngine
    {
        private static readonly string[] TuningControlParams =
        {
            "CPXPARAM_TimeLimit", "CPXPARAM_DetTimeLimit", "CPXPARAM_Tune_",
        };

        private CplexTuner(CplexConfig config, ProjectConfig output) : base(config, output) { }

        public static int Run(TunerWorkspace workspace, CplexConfig productionBaseline, double budgetSeconds, int repeat)
        {
            if (workspace.Tune.Count == 0)
            {
                Logging.Error($"[INSTANCE_SET_EMPTY] Instances/tune 沒有模型檔 | context={nameof(CplexTuner)} reason=no_model_file result=aborted");
                return 2;
            }

            // TuneParam 把 TiLim / DetTiLim 當成「整個 tune 的總時限」，每次試跑的時限改由 Tune.TimeLimit / Tune.DetTimeLimit 管。
            // 把原本每次求解的時限移到 Tune.*，其餘基準設定列入 fixed set，不准調參工具更動。
            var fixedConfig = productionBaseline.Clone();
            double? perTrialSeconds = fixedConfig.TimeLimit;
            double? perTrialTicks = fixedConfig.DeterministicTimeLimit;
            fixedConfig.TimeLimit = null;
            fixedConfig.DeterministicTimeLimit = null;
            fixedConfig.TuningTimeLimit = null;
            fixedConfig.TuningDetTimeLimit = null;
            fixedConfig.TuningRepeat = null;
            fixedConfig.TuningMeasure = null;
            fixedConfig.TuningDisplay = null;

            // TuneParam 不經過 Solve 或 Experiment，因此要在這裡設定本次調參使用的 log 檔名。
            Logging.SetLogFileName($"{workspace.ProjectName}-cplex-tune");

            using var tuner = new CplexTuner(fixedConfig, new ProjectConfig { EnableSolverLog = true });
            tuner.SetModelName($"{workspace.ProjectName}-cplex-tune");
            tuner.Build();
            return tuner.Tune(workspace, fixedConfig, budgetSeconds, perTrialSeconds, perTrialTicks, repeat);
        }

        private int Tune(TunerWorkspace workspace, CplexConfig fixedConfig, double budgetSeconds, double? perTrialSeconds, double? perTrialTicks, int repeat)
        {
            var fixedSet = Model.GetParameterSet();
            // GetParameterSet() 不包含等於 CPLEX 預設值的設定；明確加入已指定的 gap，避免調參工具改變求解停止條件。
            if (fixedConfig.MipGap.HasValue) fixedSet.SetParam(CplexParam.MIP.Tolerances.MIPGap, fixedConfig.MipGap.Value);
            if (fixedConfig.AbsoluteMipGap.HasValue) fixedSet.SetParam(CplexParam.MIP.Tolerances.AbsMIPGap, fixedConfig.AbsoluteMipGap.Value);

            Model.SetParam(CplexParam.TimeLimit, budgetSeconds);
            if (perTrialSeconds.HasValue) Model.SetParam(CplexParam.Tune.TimeLimit, perTrialSeconds.Value);
            if (perTrialTicks.HasValue) Model.SetParam(CplexParam.Tune.DetTimeLimit, perTrialTicks.Value);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            FolderDir.Experiment.CreateFolder();
            string fixedPath = FolderDir.Experiment.GetPathFile($"{workspace.ProjectName}-cplex-tune-{stamp}-fixed.prm");
            string tunedPath = FolderDir.Experiment.GetPathFile($"{workspace.ProjectName}-cplex-tune-{stamp}.prm");
            Model.WriteParameterSet(fixedSet, fixedPath);

            var files = workspace.Tune.Select(i => i.FullPath).ToArray();
            Logging.Info($"[CplexTune] files={files.Length} budget={budgetSeconds}s perTrial={perTrialSeconds?.ToString(CultureInfo.InvariantCulture) ?? "未設"}s repeat={(files.Length == 1 ? repeat : 1)}");

            int status;
            if (files.Length == 1)
            {
                // 單一模型才使用 TuningRepeat：CPLEX 會改變模型元素的排列，多次量測同一模型，減少單次結果的偶然性。
                Model.SetParam(CplexParam.Tune.Repeat, repeat);
                ReadModel(files[0]);
                status = Model.TuneParam(fixedSet);
            }
            else
            {
                status = Model.TuneParam(files, fixedSet);
            }

            Model.WriteParam(tunedPath);
            var suggestions = DiffPrm(fixedPath, tunedPath);

            Logging.Info($"[CplexTune] status={DescribeStatus(status)}");
            if (suggestions.Count == 0)
                Logging.Info("[CplexTune] 無建議：在 fixed set 之外 CPLEX 沒找到比預設更好的參數");
            foreach (var s in suggestions)
                Logging.Info($"[CplexTune] 建議 {s}（拆成獨立 variant，走 §4 驗證）");

            Directory.CreateDirectory(workspace.ArchiveDir);
            string archived = Path.Combine(workspace.ArchiveDir, Path.GetFileName(tunedPath));
            File.Copy(tunedPath, archived, overwrite: false);
            Logging.Info($"[Archive] {archived}");
            return 0;
        }

        // 找出調參結果相較固定設定新增或改值的參數；排除總時限和 Tune.*，因為它們只控制調參過程。
        private static List<string> DiffPrm(string fixedPath, string tunedPath)
        {
            var before = ReadPrm(fixedPath);
            return ReadPrm(tunedPath)
                .Where(p => !TuningControlParams.Any(c => p.Key.StartsWith(c, StringComparison.Ordinal)))
                .Where(p => !before.TryGetValue(p.Key, out var v) || v != p.Value)
                .Select(p => $"{p.Key} {p.Value}")
                .ToList();
        }

        private static Dictionary<string, string> ReadPrm(string path) =>
            File.ReadAllLines(path)
                .Where(line => line.StartsWith("CPXPARAM_", StringComparison.Ordinal))
                .Select(line => line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1].Trim(), StringComparer.Ordinal);

        private static string DescribeStatus(int status) =>
            status == ILOG.CPLEX.Cplex.TuningStatus.Complete ? "Complete"
            : status == ILOG.CPLEX.Cplex.TuningStatus.TimeLim ? "TimeLim（總預算用完，建議仍可用但未試完）"
            : status == ILOG.CPLEX.Cplex.TuningStatus.DetTimeLim ? "DetTimeLim"
            : status == ILOG.CPLEX.Cplex.TuningStatus.Abort ? "Abort"
            : status.ToString(CultureInfo.InvariantCulture);
    }
}
