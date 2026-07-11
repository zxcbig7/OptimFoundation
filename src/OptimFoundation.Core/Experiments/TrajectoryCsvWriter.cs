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
        public ExpWriterType Extension => ExpWriterType.CSV;

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

        // 軌跡以 JSON 為權威來源做累積，此處不回讀。
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
