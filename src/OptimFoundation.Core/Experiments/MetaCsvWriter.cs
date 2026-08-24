using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 實驗的「說明檔」：整批實驗裡不會變的東西只寫一次。
    ///
    /// 包含：這批實驗是什麼時候跑的、跑的是哪個模型多大、求解環境、以及**基準的完整設定**。
    /// 主表因此只要記「跟基準差在哪」就好，不必每一列重抄一次設定。
    ///
    /// 格式是三欄的 Section / Key / Value，Excel 打開就是一張清單，加東西也不用改欄位。
    /// </summary>
    public sealed class MetaCsvWriter : IExperimentWriter
    {
        /// <summary>輸出格式為 CSV。</summary>
        public ExpWriterType Extension => ExpWriterType.CSV;

        /// <summary>格式版本。之後欄位有變動就加一，讀的人才知道自己在看哪一版。</summary>
        public const int SchemaVersion = 1;

        /// <summary>寫出說明檔。</summary>
        public void Write(Experiment experiment, string path)
        {
            var rows = new List<(string Section, string Key, string Value)>
            {
                ("schema", "version", SchemaVersion.ToString(CultureInfo.InvariantCulture)),
                ("experiment", "name", experiment?.Name),
                ("experiment", "description", experiment?.Description),
                ("experiment", "trialCount", (experiment?.Trials?.Count ?? 0).ToString(CultureInfo.InvariantCulture)),
                ("experiment", "writtenAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
            };

            var trials = experiment?.Trials ?? new List<Trial>();

            // 這個檔可能累積了好幾批（同名實驗重跑），把每批的識別與開始時間都列出來
            foreach (var runId in trials.Select(t => t.RunId).Where(r => !string.IsNullOrEmpty(r)).Distinct().OrderBy(r => r))
            {
                var first = trials.First(t => t.RunId == runId);
                rows.Add(("run", $"{runId}.startedAt", first.RunAt.ToString("yyyy-MM-dd HH:mm:ss")));
                rows.Add(("run", $"{runId}.trialCount", trials.Count(t => t.RunId == runId).ToString(CultureInfo.InvariantCulture)));
            }

            // 模型：跑了哪些、各自多大
            foreach (var model in trials.Select(t => t.Model).Where(m => !string.IsNullOrEmpty(m)).Distinct().OrderBy(m => m))
            {
                var sample = trials.First(t => t.Model == model && t.Metrics != null);
                if (sample?.Metrics == null) continue;
                rows.Add(("model", $"{model}.varCount", sample.Metrics.VarCount.ToString(CultureInfo.InvariantCulture)));
                rows.Add(("model", $"{model}.constraintCount", sample.Metrics.ConstraintCount.ToString(CultureInfo.InvariantCulture)));
            }

            var baseline = CsvExperimentWriter.FindBaseline(experiment);
            if (baseline?.Config != null)
            {
                rows.Add(("environment", "solver", baseline.Config.Solver));
                rows.Add(("environment", "machine", Environment.MachineName));

                rows.Add(("baseline", "label", baseline.Label));
                // 基準的完整設定：只有「真的有設」的旋鈕，沒列到的就是用求解器預設
                foreach (var kv in baseline.Config.SolverSpecific.OrderBy(k => k.Key))
                    rows.Add(("baseline", kv.Key, Convert.ToString(kv.Value, CultureInfo.InvariantCulture)));
            }

            var sb = new StringBuilder();
            sb.AppendLine("Section,Key,Value");
            foreach (var r in rows)
                sb.AppendLine($"{Cell(r.Section)},{Cell(r.Key)},{Cell(r.Value)}");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>恆回 null：說明檔只寫不讀。</summary>
        public Experiment Read(string path) => null;

        private static string Cell(object v)
        {
            if (v == null) return "";
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
