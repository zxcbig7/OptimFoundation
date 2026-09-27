using System.Globalization;
using OptimFoundation.Core;
using OptimFoundation.Cplex;
using CplexParam = ILOG.CPLEX.Cplex.Param;

namespace ModelTuner
{
    /// <summary>
    /// S2.5（solver-tuning-guide §3.6）：CPLEX 內建 tuning tool 直接吃 Instances/tune 的模型檔。
    /// 框架沒有封裝 <c>TuneParam</c>（guide 附錄 B 缺口），這裡用子類別取得 protected <c>Model</c> 補上，
    /// 好處是 productionBaseline 的全部旋鈕照樣經框架的 <c>Configuration()</c> 套進 CPLEX；框架補上 AutoTune 後改用它。
    /// 產出只是候選來源：每個建議參數拆成獨立 variant 走 §4 驗證，NEVER 直接 promote。
    /// </summary>
    internal sealed class CplexTuner : OptEngine
    {
        private static readonly string[] TuningControlParams =
        {
            "CPXPARAM_TimeLimit", "CPXPARAM_DetTimeLimit", "CPXPARAM_Tune_",
        };

        private CplexTuner(CplexConfig config, ProjectConfig project) : base(config, project) { }

        public static int Run(TunerWorkspace workspace, CplexConfig productionBaseline, double budgetSeconds, int repeat)
        {
            if (workspace.Tune.Count == 0)
            {
                Logging.Error($"[INSTANCE_SET_EMPTY] Instances/tune 沒有模型檔 | context={nameof(CplexTuner)} reason=no_model_file result=aborted");
                return 2;
            }

            // TuneParam 把 TiLim / DetTiLim 當成「整個 tune 的總時限」，每次試跑的時限改由 Tune.TimeLimit / Tune.DetTimeLimit 管。
            // 所以契約的時限搬到 Tune.*，其餘設定全部當 fixed set（tune 不准動）——建議值才會是「baseline + 幾顆」。
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

            var project = new ProjectConfig { EnableSolverLog = true, ExportLP = false, ExportMPS = false, ExportSol = false };
            Logging.SetLogFileName($"{workspace.ProjectName}-cplex-tune");

            using var tuner = new CplexTuner(fixedConfig, project);
            tuner.SetModelName($"{workspace.ProjectName}-cplex-tune");
            tuner.Build();
            return tuner.Tune(workspace, fixedConfig, budgetSeconds, perTrialSeconds, perTrialTicks, repeat);
        }

        private int Tune(TunerWorkspace workspace, CplexConfig fixedConfig, double budgetSeconds, double? perTrialSeconds, double? perTrialTicks, int repeat)
        {
            var fixedSet = Model.GetParameterSet();
            // 值剛好等於 CPLEX 預設的旋鈕不會出現在 GetParameterSet()；停止契約的 gap 明確釘進 fixed set，tune 才不會動到終點線
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
                // TuningRepeat 只在單一模型的 TuneParam 有效：CPLEX 以 permutation 人工製造多樣本，補單 instance 缺樣本的洞
                Model.SetParam(CplexParam.Tune.Repeat, repeat);
                ImportModel(files[0]);
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

        // 建議 = tuned 參數檔裡有、但 fixed set 沒有（或值不同）的那幾行；時限與 Tune.* 是這次 tune 自己的控制參數，不算
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
