using System.Globalization;
using System.IO;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 長格式 CSV（1 列 / 收斂點）：把各 Trial 的 Convergence 軌跡攤平，供直接畫
    /// 「incumbent / bound / gap 隨時間」的收斂曲線。與 <see cref="CsvExperimentWriter"/>
    /// （1 列 / Trial 的摘要）互補。軌跡以 JSON 為權威來源，故 Read 不回讀。
    /// </summary>
    public sealed class TrajectoryCsvWriter : IExperimentWriter
    {
        /// <summary>輸出格式為 CSV。</summary>
        public ExpWriterType Extension => ExpWriterType.CSV;

        /// <summary>
        /// 把所有 Trial 的收斂軌跡攤平成長格式 CSV（欄位 RunAt, Label, PointIndex, TimeMs, Objective, Bound, Gap）。
        /// 沒有軌跡的 Trial 直接跳過；NaN / Infinity 寫成空白，畫圖時自然斷點。
        /// </summary>
        public void Write(Experiment experiment, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RunAt,Label,PointIndex,TimeMs,Objective,Bound,Gap");

            foreach (var t in experiment.Trials)
            {
                var pts = t.Metrics?.Convergence;
                if (pts == null) continue;

                int i = 0;
                foreach (var p in pts)
                {
                    var cells = new[]
                    {
                        Cell(t.RunAt.ToString("yyyy-MM-dd HH:mm:ss")),
                        Cell(t.Label),
                        i.ToString(CultureInfo.InvariantCulture),
                        Num(p.TimeMs),
                        Num(p.Objective),
                        Num(p.Bound),
                        Num(p.Gap)
                    };
                    sb.AppendLine(string.Join(",", cells));
                    i++;
                }
            }

            // UTF-8 with BOM：與 CsvExperimentWriter 一致，讓 zh-TW Excel 正確辨識中文 Label
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>恆回 null：軌跡的累積以 JSON 為權威來源，本 writer 只寫不讀。</summary>
        public Experiment Read(string path) => null;

        private static string Num(double v)
            => double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("R", CultureInfo.InvariantCulture);

        private static string Cell(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
