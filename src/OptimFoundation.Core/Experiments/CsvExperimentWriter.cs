using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 每個 Trial 一列的扁平 CSV，給人用 Excel 直接看：這次跑了哪些設定、各自在基準上改了什麼、結果如何。
    ///
    /// 不再把一堆設定欄攤平在這裡——那樣改到沒被攤平的旋鈕時，兩列會長得一模一樣。
    /// 改成只寫「跟基準比差在哪」（DiffKnobs），基準本身的完整設定寫在同名的 -meta.csv。
    /// 收斂軌跡另外寫 -trajectory.csv。
    /// </summary>
    public sealed class CsvExperimentWriter : IExperimentWriter
    {
        private static readonly string[] Header =
        {
            "RunId", "TrialId", "Model", "TrialLabel", "BasedOn", "DiffKnobs", "Seed", "RunAt",
            "Status", "ObjectiveValue", "BestBound", "MipGap", "RunTimeMs",
            "TFeasMs", "TStallMs", "DeltaBound",
            "NodeCount", "IterationCount", "TrajectoryPoints",
            "VarCount", "ConstraintCount", "Note"
        };

        /// <summary>輸出格式為 CSV。</summary>
        public ExpWriterType Extension => ExpWriterType.CSV;

        /// <summary>把整個實驗寫成一列一 trial 的 CSV。</summary>
        public void Write(Experiment experiment, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Header));

            // 每一批（RunId）各自找自己的基準。
            // 同名實驗重跑會累積在同一個檔裡，如果拿最舊那批當基準去比今天的，
            // 會比出「跨版本的設定差異」——那不是這輪改了什麼，只會誤導人。
            var baselineOfRun = experiment.Trials
                .GroupBy(t => t.RunId ?? "")
                .ToDictionary(g => g.Key, g => FindBaselineIn(g.ToList()));

            foreach (var t in experiment.Trials)
            {
                var m = t.Metrics;
                var baseline = baselineOfRun[t.RunId ?? ""];
                bool isBaseline = ReferenceEquals(t, baseline);

                var cells = new List<string>
                {
                    Cell(t.RunId),
                    t.TrialId > 0 ? t.TrialId.ToString(CultureInfo.InvariantCulture) : "",
                    Cell(t.Model),
                    Cell(t.Label),
                    isBaseline ? "" : Cell(baseline?.Label),
                    isBaseline ? "" : Cell(DiffAgainst(baseline, t)),
                    Setting(t, "Seed"),
                    Cell(t.RunAt.ToString("yyyy-MM-dd HH:mm:ss"))
                };

                if (m != null)
                {
                    cells.Add(Cell(m.Status.ToString()));
                    cells.Add(Num(m.ObjectiveValue));
                    cells.Add(Num(m.BestBound));
                    cells.Add(Num(m.MipGap));
                    cells.Add(Num(m.RunTimeMs));
                    cells.Add(NumOrBlank(m.TFeasMs));
                    cells.Add(NumOrBlank(m.TStallMs));
                    cells.Add(NumOrBlank(m.DeltaBound));
                    cells.Add(m.NodeCount?.ToString(CultureInfo.InvariantCulture) ?? "");
                    cells.Add(m.IterationCount?.ToString(CultureInfo.InvariantCulture) ?? "");
                    cells.Add(m.TrajectoryPoints.ToString(CultureInfo.InvariantCulture));
                    cells.Add(m.VarCount.ToString(CultureInfo.InvariantCulture));
                    cells.Add(m.ConstraintCount.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    for (int i = 0; i < 13; i++) cells.Add("");
                }

                cells.Add(Cell(t.Note));
                sb.AppendLine(string.Join(",", cells));
            }

            // UTF-8 with BOM：讓 zh-TW Excel 正確辨識中文，避免被當成 Big5 讀成亂碼
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>恆回 null：累積以 JSON 為權威來源，本 writer 只寫不讀。</summary>
        public Experiment Read(string path) => null;

        /// <summary>
        /// 找出當作比較基準的那一筆：標籤含 "baseline" 的第一筆；都沒有的話用第一筆。
        /// 一輪實驗裡只會有一組基準，所以這個規則夠用，也不必額外設定。
        /// </summary>
        internal static Trial FindBaselineIn(IList<Trial> trials)
        {
            if (trials == null || trials.Count == 0) return null;
            return trials.FirstOrDefault(t =>
                       (t.Label ?? "").IndexOf("baseline", System.StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? trials[0];
        }

        /// <summary>取整個實驗最後一批的基準，給說明檔用。</summary>
        internal static Trial FindBaseline(Experiment experiment)
        {
            var trials = experiment?.Trials;
            if (trials == null || trials.Count == 0) return null;
            var lastRunId = trials[trials.Count - 1].RunId ?? "";
            return FindBaselineIn(trials.Where(t => (t.RunId ?? "") == lastRunId).ToList());
        }

        /// <summary>
        /// 這筆跟基準比差在哪，寫成 "旋鈕=值"，多顆用分號隔開。
        /// Seed 不算差異——它是同一組設定重跑幾次用的，本身另有一欄。
        /// </summary>
        internal static string DiffAgainst(Trial baseline, Trial trial)
        {
            var base_ = baseline?.Config?.SolverSpecific;
            var mine = trial?.Config?.SolverSpecific;
            if (base_ == null || mine == null) return "";

            var keys = new SortedSet<string>(base_.Keys);
            keys.UnionWith(mine.Keys);

            var parts = new List<string>();
            foreach (var k in keys)
            {
                if (k == "Seed") continue;
                base_.TryGetValue(k, out var b);
                mine.TryGetValue(k, out var v);
                if (Equals(Text(b), Text(v))) continue;
                // 設回預設（本次沒設、基準有設）就寫成「旋鈕=預設」，讓人看得出是被拿掉了
                parts.Add($"{k}={(v == null ? "預設" : Text(v))}");
            }
            return string.Join(";", parts);
        }

        private static string Setting(Trial t, string key)
        {
            var c = t?.Config;
            if (c == null) return "";
            if (c.Tunable.TryGetValue(key, out var v)) return Cell(v);
            if (c.SolverSpecific.TryGetValue(key, out var v2)) return Cell(v2);
            return "";
        }

        private static string Text(object v) =>
            v == null ? null : System.Convert.ToString(v, CultureInfo.InvariantCulture);

        /// <summary>
        /// 數值轉字串。NaN 照實寫 "NaN"——空白代表「沒這個值 / 沒設定」，
        /// 跟「求解器算出來就是 NaN」是兩件事，混在一起會讓人把 NaN 當成 0 去平均。
        /// </summary>
        private static string Num(double d) =>
            double.IsNaN(d) ? "NaN"
            : double.IsPositiveInfinity(d) ? "Infinity"
            : double.IsNegativeInfinity(d) ? "-Infinity"
            : d.ToString("R", CultureInfo.InvariantCulture);

        private static string NumOrBlank(double? d) => d.HasValue ? Num(d.Value) : "";

        /// <summary>CSV 欄位值：含逗號、雙引號或換行時加引號並跳脫。</summary>
        private static string Cell(object v)
        {
            if (v == null) return "";
            string s = v is double d ? Num(d) : System.Convert.ToString(v, CultureInfo.InvariantCulture);
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
