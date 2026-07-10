using System.Globalization;
using System.IO;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 每個 Trial 一列的扁平 CSV：label + 抽象控制項目 + 指標。給人做 tuning 對照 / Excel / pandas。
    /// 軌跡不進 CSV（只進 JSON）。
    /// </summary>
    public sealed class CsvExperimentWriter : IExperimentWriter
    {
        private static readonly string[] TunableCols =
        {
            "Seed", "Emphasis", "FeasibilityTol", "OptimalityTol",
            "RootAlgorithm", "Presolve", "HeuristicEffort", "MemoryLimitMb",
            "TimeLimit", "MipGap", "Threads"
        };

        public ExpWriterType Extension => ExpWriterType.CSV;



        /// <summary>
        /// 將 Experiment 寫入 CSV，給人做 tuning 對照 / Excel / pandas。
        /// </summary>
        /// <param name="experiment"></param>
        /// <param name="path"></param>
        public void Write(Experiment experiment, string path)
        {
            var sb = new StringBuilder();
            sb.Append("RunAt,Label,Solver,");
            sb.Append(string.Join(",", TunableCols));
            sb.AppendLine(",Status,ObjectiveValue,BestBound,ResultGap,WallTimeMs,NodeCount,IterationCount,VarCount,ConstraintCount,Note");

            foreach (var t in experiment.Trials)
            {
                var c = t.Config;
                var m = t.Metrics;
                var cells = new System.Collections.Generic.List<string>
                {
                    Cell(t.RunAt.ToString("yyyy-MM-dd HH:mm:ss")),
                    Cell(t.Label),
                    Cell(c?.Solver)
                };
                foreach (var col in TunableCols)
                    cells.Add(Tunable(c, col));

                if (m != null)
                {
                    cells.Add(Cell(m.Status.ToString()));
                    cells.Add(Num(m.ObjectiveValue));
                    cells.Add(Num(m.BestBound));
                    cells.Add(Num(m.MipGap));
                    cells.Add(Num(m.WallTimeMs));
                    cells.Add(m.NodeCount?.ToString(CultureInfo.InvariantCulture) ?? "");
                    cells.Add(m.IterationCount?.ToString(CultureInfo.InvariantCulture) ?? "");
                    cells.Add(m.VarCount.ToString(CultureInfo.InvariantCulture));
                    cells.Add(m.ConstraintCount.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    for (int i = 0; i < 9; i++) cells.Add("");
                }
                cells.Add(Cell(t.Note));
                sb.AppendLine(string.Join(",", cells));
            }

            // UTF-8 with BOM：讓 zh-TW Excel 正確辨識中文（Label/Note），避免以 Big5(950) 解讀成亂碼
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        // CSV 以 JSON 為權威來源做累積，此處不回讀。
        public Experiment Read(string path) => null;

        private static string Tunable(ConfigSnapshot c, string key)
        {
            return c != null && c.Tunable.TryGetValue(key, out var v) ? Cell(v) : "";

        }

        /// <summary>
        /// 將數值轉換為 CSV 格式。
        /// </summary>
        /// <param name="d"></param>
        /// <returns></returns>
        private static string Num(double d)
        {
            return double.IsNaN(d) ? "" : d.ToString("R", CultureInfo.InvariantCulture);

        }

        /// <summary>
        /// 用於 CSV 的欄位值，若有逗號、雙引號、換行符號則加上雙引號，並將雙引號轉成兩個雙引號。
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        private static string Cell(object v)
        {
            if (v == null) return "";
            string s = v is double d
                ? (double.IsNaN(d) ? "" : d.ToString("R", CultureInfo.InvariantCulture))
                : v.ToString();
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
