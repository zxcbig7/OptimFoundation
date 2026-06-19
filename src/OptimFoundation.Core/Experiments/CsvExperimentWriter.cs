using System.Globalization;
using System.IO;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 每個 Trial 一列的扁平 CSV：label + 抽象旋鈕 + 指標。給人做 tuning 對照 / Excel / pandas。
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

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        // CSV 以 JSON 為權威來源做累積，此處不回讀。
        public Experiment Read(string path) => null;

        private static string Tunable(ConfigSnapshot c, string key)
            => c != null && c.Tunable.TryGetValue(key, out var v) ? Cell(v) : "";

        private static string Num(double d)
            => double.IsNaN(d) ? "" : d.ToString("R", CultureInfo.InvariantCulture);

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
