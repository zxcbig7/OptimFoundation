using System.Globalization;
using System.Text;
using OptimFoundation.Core;

namespace ModelInspector.Reporting
{
    /// <summary>把一份 <see cref="ModelInspection"/> 寫成 Console 摘要與 Reports/ 底下的完整檔案。</summary>
    public sealed class ReportWriter
    {
        // Reports 不在 FolderDir 的固定配置裡（那六個都是框架自己的產物），
        // 但路徑規則要一致，所以沿用 ProjFolder 而不是自己拼字串。
        private static readonly FolderDir.ProjFolder ReportFolder = new FolderDir.ProjFolder("Reports");

        private readonly ModelInspection _inspection;
        private readonly InspectionOptions _options;
        private readonly string _stamp;

        /// <summary>建立寫出器。</summary>
        public ReportWriter(ModelInspection inspection, InspectionOptions options)
        {
            _inspection = inspection;
            _options = options;
            _stamp = string.IsNullOrEmpty(inspection.StartTime)
                ? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
                : inspection.StartTime;
        }

        /// <summary>寫出全部報告檔，回傳產生的檔案路徑。</summary>
        public IReadOnlyList<string> WriteFiles()
        {
            ReportFolder.CreateFolder();
            var written = new List<string>();

            string reportPath = ReportFolder.GetPathFile($"{_inspection.Label}_Inspection_{_stamp}.md");
            File.WriteAllText(reportPath, BuildMarkdown(), new UTF8Encoding(false));
            written.Add(reportPath);

            if (_inspection.SolutionValueCount > 0)
            {
                string csvPath = ReportFolder.GetPathFile($"{_inspection.Label}_Variables_{_stamp}.csv");
                File.WriteAllText(csvPath, BuildVariableCsv(), new UTF8Encoding(false));
                written.Add(csvPath);
            }

            if (_inspection.Trajectory.Count > 0)
            {
                string trajPath = ReportFolder.GetPathFile($"{_inspection.Label}_Trajectory_{_stamp}.csv");
                File.WriteAllText(trajPath, BuildTrajectoryCsv(), new UTF8Encoding(false));
                written.Add(trajPath);
            }

            if (_inspection.ConflictConstraints.Count > 0)
            {
                string conflictPath = ReportFolder.GetPathFile($"{_inspection.Label}_Conflicts_{_stamp}.txt");
                File.WriteAllText(conflictPath, string.Join(Environment.NewLine, _inspection.ConflictConstraints),
                    new UTF8Encoding(false));
                written.Add(conflictPath);
            }

            return written;
        }

        /// <summary>印出 Console 摘要。明細筆數受 --max-print 限制，完整內容在報告檔。</summary>
        public void WriteConsole(IReadOnlyList<string> reportFiles)
        {
            Section("來源模型檔");
            Line("路徑", _inspection.SourceFile);
            Line("格式", $"{_inspection.SourceFormat}  {_inspection.SourceBytes:N0} bytes  "
                + $"最後寫入 {_inspection.SourceModified:yyyy-MM-dd HH:mm:ss}");

            Section("模型結構（re-index 後）");
            Line("變數數", _inspection.VariableCount.ToString("N0"));
            Line("限制式數", _inspection.ConstraintCount.ToString("N0"));

            Section("求解結果");
            Line("狀態", _inspection.Status.ToString());
            if (_inspection.Failure != null)
                Line("例外", _inspection.Failure);
            Line("目標值", Num(_inspection.ObjectiveValue));
            Line("最佳界", Num(_inspection.BestBound));
            Line("MIP gap", Pct(_inspection.MipGap));
            if (_inspection.Metrics is { } m)
            {
                Line("求解耗時", $"{m.RunTimeMs:N0} ms");
                Line("節點數", m.NodeCount?.ToString("N0") ?? "n/a");
                Line("迭代數", m.IterationCount?.ToString("N0") ?? "n/a");
                Line("首次可行", m.TFeasMs.HasValue ? $"{m.TFeasMs.Value:N0} ms" : "n/a");
                Line("界推進量", m.DeltaBound.HasValue ? Num(m.DeltaBound.Value) : "n/a");
                Line("界停滯於", m.TStallMs.HasValue ? $"{m.TStallMs.Value:N0} ms" : "n/a");
            }
            Line("整趟耗時", $"{_inspection.TotalElapsed.TotalMilliseconds:N0} ms"
                + $"（其中讀檔 + re-index {_inspection.BuildModelElapsed.TotalMilliseconds:N0} ms）");

            if (_inspection.SolutionValueCount > 0)
            {
                Section($"變數解值（{_inspection.SolutionValueCount:N0} 個）");
                Line("Binary", Distribution(_inspection.BinaryValues));
                Line("Integer", Distribution(_inspection.IntegerValues));
                Line("Continuous", Distribution(_inspection.ContinuousValues));

                var nonZero = AllValues().Where(kv => Math.Abs(kv.Value) > 1e-9)
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
                Console.WriteLine();
                Console.WriteLine($"  非零變數前 {Math.Min(_options.MaxPrint, nonZero.Count)} 筆"
                    + $"（共 {nonZero.Count:N0}，完整清單在 CSV）：");
                foreach (var kv in nonZero.Take(_options.MaxPrint))
                    Console.WriteLine($"    {kv.Key} = {Num(kv.Value)}");
            }

            if (_inspection.Trajectory.Count > 0)
            {
                Section($"收斂軌跡（{_inspection.Trajectory.Count:N0} 點）");
                foreach (var p in Sample(_inspection.Trajectory, _options.MaxPrint))
                    Console.WriteLine($"    t={p.TimeMs,8:N0} ms  obj={Num(p.Objective),14}  "
                        + $"bound={Num(p.Bound),14}  gap={Pct(p.Gap)}");
            }

            if (_inspection.ConflictConstraints.Count > 0)
            {
                Section($"IIS 衝突限制式（{_inspection.ConflictConstraints.Count:N0} 條）");
                foreach (string name in _inspection.ConflictConstraints.Take(_options.MaxPrint))
                    Console.WriteLine($"    {name}");
                if (_inspection.ConflictConstraints.Count > _options.MaxPrint)
                    Console.WriteLine($"    …其餘 {_inspection.ConflictConstraints.Count - _options.MaxPrint:N0} 條見報告檔");
            }
            else if (_inspection.Status == SolveStatus.Infeasible)
            {
                Section("IIS 衝突限制式");
                Console.WriteLine("    RefineConflict 未能識別出衝突子集。");
            }

            if (_inspection.Artifacts.Count > 0)
            {
                Section("框架輸出檔");
                foreach (var a in _inspection.Artifacts)
                    Console.WriteLine($"    {a.Kind}：{a.Path}（{a.Bytes:N0} bytes）");
            }

            Section("匯入模式的已知限制");
            foreach (string caveat in _inspection.Caveats)
                Console.WriteLine($"    - {caveat}");

            Section("報告檔");
            foreach (string path in reportFiles)
                Console.WriteLine($"    {path}");
            Console.WriteLine();
        }

        private string BuildMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# 模型檢視報告 — {_inspection.Label}");
            sb.AppendLine();
            sb.AppendLine($"產生時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}　"
                + $"引擎：OptimFoundation.Cplex　模式：ImportModel（`OptModel.FromFile`）");
            sb.AppendLine();

            sb.AppendLine("## 1. 來源");
            sb.AppendLine();
            sb.AppendLine("| 欄位 | 值 |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| 模型檔 | `{_inspection.SourceFile}` |");
            sb.AppendLine($"| 格式 | `{_inspection.SourceFormat}` |");
            sb.AppendLine($"| 大小 | {_inspection.SourceBytes:N0} bytes |");
            sb.AppendLine($"| 最後寫入 | {_inspection.SourceModified:yyyy-MM-dd HH:mm:ss} |");
            sb.AppendLine();

            sb.AppendLine("## 2. 執行設定");
            sb.AppendLine();
            sb.AppendLine("### 2.1 專案層（ProjectConfig）");
            sb.AppendLine();
            sb.AppendLine("| 欄位 | 值 |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| ProjectName | {_inspection.ProjectConfig.ProjectName} |");
            sb.AppendLine($"| RetentionDays | {_inspection.ProjectConfig.RetentionDays?.ToString() ?? "30（預設）"} |");
            sb.AppendLine($"| EnableSolverLog | {_inspection.ProjectConfig.EnableSolverLog} |");
            sb.AppendLine($"| ExportLP | {_inspection.ProjectConfig.ExportLP} |");
            sb.AppendLine($"| ExportMPS | {_inspection.ProjectConfig.ExportMPS} |");
            sb.AppendLine($"| ExportSol | {_inspection.ProjectConfig.ExportSol} |");
            sb.AppendLine();

            sb.AppendLine("### 2.2 求解器（ConfigSnapshot）");
            sb.AppendLine();
            sb.AppendLine($"求解器：`{_inspection.ConfigSnapshot.Solver}`。快照只記「真的有設」的旋鈕，"
                + "沒列到的一律是 CPLEX 自己的預設值。");
            sb.AppendLine();
            AppendConfigTable(sb, "共通旋鈕", _inspection.ConfigSnapshot.Tunable);
            AppendConfigTable(sb, "CPLEX 專屬（含與上表重疊者）", _inspection.ConfigSnapshot.SolverSpecific);

            sb.AppendLine("## 3. 模型結構");
            sb.AppendLine();
            sb.AppendLine("`ImportModel` 讀檔後由 `ReindexFromModel` 從 active model 的 `ILPMatrix` "
                + "反向取回 `INumVar` 與 `IRange`，下列前兩項即該次 re-index 的結果。");
            sb.AppendLine();
            sb.AppendLine("| 指標 | 值 | 匯入模式下是否有效 |");
            sb.AppendLine("| --- | --- | --- |");
            sb.AppendLine($"| VariableCount | {_inspection.VariableCount:N0} | 有效 |");
            sb.AppendLine($"| ConstraintCount | {_inspection.ConstraintCount:N0} | 有效 |");
            sb.AppendLine($"| RegisteredVariableCount | {_inspection.RegisteredVariableCount:N0} | 無效（量 VariableSets） |");
            sb.AppendLine($"| ObjectiveSense | {_inspection.ObjectiveSense} | 無效（框架預設值，非檔案內容） |");
            sb.AppendLine($"| ObjectiveTermCount | {_inspection.ObjectiveTermCount:N0} | 無效（量 pool 累積） |");
            sb.AppendLine($"| SoftConstraintCount | {_inspection.SoftConstraintCount:N0} | 無效（量 pool 累積） |");
            sb.AppendLine($"| SoftPenaltyTermCount | {_inspection.SoftPenaltyTermCount:N0} | 無效（量 pool 累積） |");
            sb.AppendLine($"| VariableBuildCounts | {_inspection.VariableBuildCounts.Count} 組 | 無效（Build*Vs 專用） |");
            sb.AppendLine($"| ConstraintBuildCounts | {_inspection.ConstraintBuildCounts.Count} 組 | 無效（Build*Vs 專用） |");
            sb.AppendLine($"| SupportsTrajectory | {_inspection.SupportsTrajectory} | 有效 |");
            sb.AppendLine();

            if (_inspection.SolutionValueCount > 0)
            {
                sb.AppendLine("解出來之後才量得到的變數型別分布（來自三支取解 API 的鍵數）：");
                sb.AppendLine();
                sb.AppendLine("| 型別 | 變數數 | 非零數 |");
                sb.AppendLine("| --- | --- | --- |");
                sb.AppendLine($"| Binary | {_inspection.BinaryValues.Count:N0} | {NonZero(_inspection.BinaryValues):N0} |");
                sb.AppendLine($"| Integer | {_inspection.IntegerValues.Count:N0} | {NonZero(_inspection.IntegerValues):N0} |");
                sb.AppendLine($"| Continuous | {_inspection.ContinuousValues.Count:N0} | {NonZero(_inspection.ContinuousValues):N0} |");
                sb.AppendLine();
            }

            sb.AppendLine("## 4. 求解結果");
            sb.AppendLine();
            sb.AppendLine("| 指標 | 值 |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| Status | {_inspection.Status} |");
            sb.AppendLine($"| IsSuccess | {_inspection.IsSuccess} |");
            sb.AppendLine($"| ObjectiveValue | {Num(_inspection.ObjectiveValue)} |");
            sb.AppendLine($"| BestBound | {Num(_inspection.BestBound)} |");
            sb.AppendLine($"| MIPGap | {Pct(_inspection.MipGap)} |");
            if (_inspection.Metrics is { } metrics)
            {
                sb.AppendLine($"| RunTimeMs | {metrics.RunTimeMs:N2} |");
                sb.AppendLine($"| NodeCount | {metrics.NodeCount?.ToString("N0") ?? "n/a"} |");
                sb.AppendLine($"| IterationCount | {metrics.IterationCount?.ToString("N0") ?? "n/a"} |");
                sb.AppendLine($"| VarCount（metrics） | {metrics.VarCount:N0} |");
                sb.AppendLine($"| ConstraintCount（metrics） | {metrics.ConstraintCount:N0} |");
                sb.AppendLine($"| TrajectoryPoints | {metrics.TrajectoryPoints:N0} |");
                sb.AppendLine($"| TFeasMs | {metrics.TFeasMs?.ToString("N2") ?? "n/a"} |");
                sb.AppendLine($"| DeltaBound | {(metrics.DeltaBound.HasValue ? Num(metrics.DeltaBound.Value) : "n/a")} |");
                sb.AppendLine($"| TStallMs | {metrics.TStallMs?.ToString("N2") ?? "n/a"} |");
            }
            else
            {
                sb.AppendLine("| LastMetrics | null（求解未正常結束） |");
            }
            sb.AppendLine($"| TotalElapsed | {_inspection.TotalElapsed.TotalMilliseconds:N2} ms |");
            sb.AppendLine($"| BuildModelElapsed | {_inspection.BuildModelElapsed.TotalMilliseconds:N2} ms |");
            if (_inspection.Failure != null)
                sb.AppendLine($"| 例外 | {_inspection.Failure} |");
            sb.AppendLine();

            sb.AppendLine("## 5. 收斂軌跡");
            sb.AppendLine();
            if (_inspection.Trajectory.Count == 0)
            {
                sb.AppendLine(_inspection.TrajectoryRequested
                    ? "已開啟軌跡但沒有取樣點——純 LP 沒有分支定界過程，或求解太快來不及取樣。"
                    : "本次以 `--no-trajectory` 執行，未記錄軌跡。");
            }
            else
            {
                sb.AppendLine($"共 {_inspection.Trajectory.Count:N0} 點，完整序列見同名 `_Trajectory_*.csv`。");
                sb.AppendLine();
                sb.AppendLine("| TimeMs | Objective | Bound | Gap |");
                sb.AppendLine("| --- | --- | --- | --- |");
                foreach (var p in Sample(_inspection.Trajectory, 30))
                    sb.AppendLine($"| {p.TimeMs:N0} | {Num(p.Objective)} | {Num(p.Bound)} | {Pct(p.Gap)} |");
            }
            sb.AppendLine();

            sb.AppendLine("## 6. 變數解值");
            sb.AppendLine();
            if (_inspection.SolutionValueCount == 0)
            {
                sb.AppendLine("無解，取不到任何變數值。");
            }
            else
            {
                sb.AppendLine($"共 {_inspection.SolutionValueCount:N0} 個變數，完整清單見同名 `_Variables_*.csv`。"
                    + "下表為非零變數（依名稱排序）的前 100 筆。");
                sb.AppendLine();
                sb.AppendLine("| 變數 | 型別 | 值 |");
                sb.AppendLine("| --- | --- | --- |");
                foreach (var (name, type, value) in TypedValues()
                    .Where(t => Math.Abs(t.Value) > 1e-9)
                    .OrderBy(t => t.Name, StringComparer.Ordinal)
                    .Take(100))
                    sb.AppendLine($"| `{name}` | {type} | {Num(value)} |");
            }
            sb.AppendLine();

            sb.AppendLine("## 7. Infeasible 診斷（IIS）");
            sb.AppendLine();
            if (_inspection.Status != SolveStatus.Infeasible)
            {
                sb.AppendLine($"狀態為 {_inspection.Status}，未觸發 conflict 分析。"
                    + "框架只在 Infeasible 時自動跑 `RefineConflict`。");
            }
            else if (_inspection.ConflictConstraints.Count == 0)
            {
                sb.AppendLine("`RefineConflict` 未能識別出衝突子集（可能是限制式全部無名，或 CPLEX 判定無最小衝突集）。");
            }
            else
            {
                sb.AppendLine($"`RefineConflict` 以等權重（全 1.0）找出 {_inspection.ConflictConstraints.Count:N0} "
                    + "條衝突限制式，另有一份 `.ilp` 衝突模型寫進 `IISs/`。完整清單見同名 `_Conflicts_*.txt`。");
                sb.AppendLine();
                foreach (string name in _inspection.ConflictConstraints)
                    sb.AppendLine($"- `{name}`");
            }
            sb.AppendLine();

            sb.AppendLine("## 8. 框架輸出檔");
            sb.AppendLine();
            if (_inspection.Artifacts.Count == 0)
            {
                sb.AppendLine("本次沒有產生框架輸出檔。");
            }
            else
            {
                sb.AppendLine("| 用途 | 路徑 | 大小 |");
                sb.AppendLine("| --- | --- | --- |");
                foreach (var a in _inspection.Artifacts)
                    sb.AppendLine($"| {a.Kind} | `{a.Path}` | {a.Bytes:N0} bytes |");
            }
            sb.AppendLine();
            sb.AppendLine("solver log 與框架 log 一律寫進 `Logs/`（`EnableSolverLog` 只控制要不要同時洗 Console）。");
            sb.AppendLine();

            sb.AppendLine("## 9. 匯入模式的已知限制");
            sb.AppendLine();
            foreach (string caveat in _inspection.Caveats)
                sb.AppendLine($"- {caveat}");
            sb.AppendLine();

            return sb.ToString();
        }

        private static void AppendConfigTable(StringBuilder sb, string title, Dictionary<string, object> values)
        {
            sb.AppendLine($"**{title}**");
            sb.AppendLine();
            if (values.Count == 0)
            {
                sb.AppendLine("（未設定任何旋鈕）");
                sb.AppendLine();
                return;
            }
            sb.AppendLine("| 旋鈕 | 值 |");
            sb.AppendLine("| --- | --- |");
            foreach (var kv in values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                sb.AppendLine($"| {kv.Key} | {kv.Value} |");
            sb.AppendLine();
        }

        private string BuildVariableCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Name,Type,Value");
            foreach (var (name, type, value) in TypedValues().OrderBy(t => t.Name, StringComparer.Ordinal))
                sb.AppendLine($"{Csv(name)},{type},{value.ToString("R", CultureInfo.InvariantCulture)}");
            return sb.ToString();
        }

        private string BuildTrajectoryCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("TimeMs,Objective,Bound,Gap");
            foreach (var p in _inspection.Trajectory)
                sb.AppendLine($"{p.TimeMs.ToString("R", CultureInfo.InvariantCulture)},"
                    + $"{p.Objective.ToString("R", CultureInfo.InvariantCulture)},"
                    + $"{p.Bound.ToString("R", CultureInfo.InvariantCulture)},"
                    + $"{p.Gap.ToString("R", CultureInfo.InvariantCulture)}");
            return sb.ToString();
        }

        private IEnumerable<(string Name, string Type, double Value)> TypedValues()
        {
            foreach (var kv in _inspection.BinaryValues) yield return (kv.Key, "Binary", kv.Value);
            foreach (var kv in _inspection.IntegerValues) yield return (kv.Key, "Integer", kv.Value);
            foreach (var kv in _inspection.ContinuousValues) yield return (kv.Key, "Continuous", kv.Value);
        }

        private IEnumerable<KeyValuePair<string, double>> AllValues()
            => _inspection.BinaryValues
                .Concat(_inspection.IntegerValues)
                .Concat(_inspection.ContinuousValues);

        // 軌跡動輒上千點，等距抽樣並保證首尾都在，讓形狀看得出來又不洗版。
        private static List<ConvergencePoint> Sample(IReadOnlyList<ConvergencePoint> points, int max)
        {
            if (points.Count <= max) return points.ToList();
            var sampled = new List<ConvergencePoint>(max);
            double step = (points.Count - 1) / (double)(max - 1);
            for (int i = 0; i < max; i++)
                sampled.Add(points[(int)Math.Round(i * step)]);
            return sampled;
        }

        private static int NonZero(IReadOnlyDictionary<string, double> values)
            => values.Count(kv => Math.Abs(kv.Value) > 1e-9);

        private static string Distribution(IReadOnlyDictionary<string, double> values)
            => values.Count == 0 ? "0" : $"{values.Count:N0}（非零 {NonZero(values):N0}）";

        private static string Num(double value)
            => double.IsNaN(value) ? "n/a" : value.ToString("G6", CultureInfo.InvariantCulture);

        private static string Pct(double value)
            => double.IsNaN(value) ? "n/a" : value.ToString("P4", CultureInfo.InvariantCulture);

        private static string Csv(string value)
            => value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine($"── {title} " + new string('─', Math.Max(0, 60 - title.Length)));
        }

        private static void Line(string label, string value)
            => Console.WriteLine($"  {label}: {value}");
    }
}
