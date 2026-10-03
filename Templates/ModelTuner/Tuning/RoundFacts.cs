using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;

namespace ModelTuner
{
    /// <summary>
    /// 讀取備存的實驗累積檔（{專案}-trial.csv 與 -meta.csv，靠 Experiment 欄與 RunId 篩出一輪），產生每輪的 TUNING-FACTS 結果表（solver-tuning-guide §6.2.2）、
    /// 逐 seed 跟 baseline 比大小的勝負表（§4；勝負由框架寫在主表 VsBaseline 欄），並檢查各輪結果是否一致（§0.1.1）及 dynamic search 是否啟用（§2.3.1）。
    /// 程式負責列出結果；是否取代正式設定，仍須依 §4 與 §5 判斷。
    /// </summary>
    public static class RoundFacts
    {
        private static readonly Regex RoundPattern = new Regex(@"-tuning-r(\d+)(-holdout)?$");
        private static readonly Regex RoundShorthand = new Regex(@"^r\d+(-holdout)?$");
        private static readonly Regex RoundExperiment = new Regex(@"^tuning-r\d+(-holdout)?$");
        private static readonly Regex SeedSuffix = new Regex(@"-s\d+$");

        // 基準設定未指定 MipGap 時，使用 CPLEX 預設值，計算各輪 Optimal 目標值可接受的差距。
        private const double CplexDefaultMipGap = 1e-4;

        /// <summary>接受 <c>1</c>、<c>r1</c>、<c>r1-holdout</c> 或完整 experiment 名。</summary>
        public static string ResolveExperimentName(TunerWorkspace workspace, string arg)
        {
            if (int.TryParse(arg, out int round)) return workspace.ExperimentName(round);
            if (RoundShorthand.IsMatch(arg)) return $"{workspace.ProjectName}-tuning-{arg}";
            return arg;
        }

        public static int Report(TunerWorkspace workspace, string experimentName)
        {
            var match = RoundPattern.Match(experimentName);
            var rows = match.Success
                ? LoadRound(workspace, TunerWorkspace.ExperimentShortName(int.Parse(match.Groups[1].Value), match.Groups[2].Success))
                : new List<Row>();
            if (!match.Success || rows.Count == 0)
            {
                Logging.Error($"[FACTS_SOURCE_MISSING] 找不到本輪 archive | context={nameof(Report)} value={Path.Combine(workspace.ArchiveDir, workspace.ArtifactName("trial"))} experiment={experimentName} reason={(match.Success ? "archive_missing_or_round_absent" : "not_a_tuning_round_name")} result=aborted");
                return 2;
            }

            int round = int.Parse(match.Groups[1].Value);
            bool holdout = match.Groups[2].Success;
            string baselineLabel = $"r{round}-baseline";

            var report = new StringBuilder();
            AppendSummary(report, workspace, rows, baselineLabel, holdout);
            AppendInvariants(report, workspace, rows.FirstOrDefault(r => r.ConfigLabel == baselineLabel)?.MipGapContract);
            AppendDynamicSearch(report, experimentName);

            var file = new StringBuilder();
            file.AppendLine($"# {experimentName} — machine facts");
            file.AppendLine();
            file.AppendLine("> 由 `dotnet run -- facts` 從 archive CSV 產生，NEVER 手改；History 引用數字一律指回這裡。");
            file.AppendLine();
            AppendFactsBlock(file, workspace, $"R{round}{(holdout ? "-holdout" : "")}", rows, baselineLabel);
            file.AppendLine();
            file.Append(report);

            FolderDir.Experiment.CreateFolder();
            string path = FolderDir.Experiment.GetPathFile($"{experimentName}-facts.md");
            File.WriteAllText(path, file.ToString(), new UTF8Encoding(false));

            Console.WriteLine();
            Console.WriteLine(report.ToString());
            Logging.Info($"[Facts] TUNING-FACTS 與彙總已寫入 {path}");
            return 0;
        }

        /// <summary>將一個模型的正式求解結果整理成一行，供 TuningHistory.md 的「參考結果基線」引用。</summary>
        public static string DescribeProduction(TuningInstance instance, SolveMetrics? m)
        {
            if (m == null) return $"[Production] {instance.Name} Status=NotSolved";
            return $"[Production] {instance.Name} Status={m.Status} Objective={Num(m.ObjectiveValue)} BestBound={Num(m.BestBound)} " +
                   $"Gap={Num(m.Gap)} SolveTimeMs={m.SolveTimeMs:0} " +
                   $"Sense={m.ObjectiveSense?.ToString() ?? InferSense(m.ObjectiveValue, m.BestBound)}";
        }

        #region TUNING-FACTS（§6.2.2）

        // 數值取自備存主表，再用 R 格式輸出，避免 double 精度因格式化而損失；每列對應一次試跑。
        private static void AppendFactsBlock(StringBuilder sb, TunerWorkspace workspace, string marker, List<Row> rows, string baselineLabel)
        {
            var baseline = rows.FirstOrDefault(r => r.ConfigLabel == baselineLabel);
            string experiment = rows[0].Experiment;
            string runId = rows[0].RunId;

            sb.AppendLine($"<!-- TUNING-FACTS:{marker}:BEGIN -->");
            sb.AppendLine($"- experiment：`{experiment}`；RunId：`{runId}`");
            sb.AppendLine(
                $"- archive：`Experiments/{workspace.ArtifactName("trial")}`、`Experiments/{workspace.ArtifactName("meta")}`、`Experiments/{workspace.ArtifactName("summary")}`" +
                (HasTrajectory(workspace, experiment, runId) ? $"、`Experiments/{workspace.ArtifactName("trajectory")}`" : "") +
                "（篩 Experiment + RunId）");
            sb.AppendLine();
            sb.AppendLine("| instance | label | seed | status | objectiveValue | bestBound | gap | solveTimeMs | vsBaseline | configDiffFromBaseline |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var r in rows)
            {
                string diff = baseline == null || r.ConfigLabel == baselineLabel ? "" : Diff(baseline.Config, r.Config);
                sb.AppendLine(
                    $"| {Md(r.Model)} | {Md(r.Label)} | {r.Seed?.ToString(CultureInfo.InvariantCulture)} | {r.Status} " +
                    $"| {Raw(r.ObjectiveValue)} | {Raw(r.BestBound)} | {Raw(r.Gap)} | {Raw(r.SolveTimeMs)} " +
                    $"| {(r.VsBaseline.Length == 0 ? CsvExperimentWriter.NotAvailable : r.VsBaseline)} | {Md(diff)} |");
            }
            sb.AppendLine($"<!-- TUNING-FACTS:{marker}:END -->");
        }

        // 只列與 baseline 真正不同的欄位；Seed 是重複量測條件，不算差異
        private static string Diff(Dictionary<string, string> baseline, Dictionary<string, string> mine)
        {
            var keys = new SortedSet<string>(baseline.Keys, StringComparer.Ordinal);
            keys.UnionWith(mine.Keys);

            var parts = new List<string>();
            foreach (var key in keys.Where(k => k != "Seed"))
            {
                baseline.TryGetValue(key, out var b);
                mine.TryGetValue(key, out var a);
                if (b != a) parts.Add($"{key}: {b ?? "null"} → {a ?? "null"}");
            }
            return string.Join("; ", parts);
        }

        #endregion

        #region 勝負（§4：同一個 seed 跟 baseline 比大小）

        private sealed record Tally(int N, int Optimal, int Feasible, int NoSolution, int Failed, int Wins, int Losses, int Ties, int NotCompared);

        // 勝負由框架寫在主表 VsBaseline 欄，這裡只計數並套 §4 的規則
        private static void AppendSummary(StringBuilder sb, TunerWorkspace workspace, List<Row> rows, string baselineLabel, bool holdout)
        {
            var groups = rows.GroupBy(r => r.ConfigLabel)
                .OrderBy(g => g.Key == baselineLabel ? 0 : 1)
                .ToList();

            sb.AppendLine("## 勝負（同一個 seed 跟 baseline 比大小；規則見 solver-tuning-guide §4）");
            sb.AppendLine();
            sb.AppendLine($"- 來源：`Experiments/{workspace.ArtifactName("trial")}` 中 Experiment = `{rows[0].Experiment}`、RunId = `{rows[0].RunId}` 的 VsBaseline 欄，trials={rows.Count}，instances={rows.Select(r => r.Model).Distinct().Count()}");
            sb.AppendLine("- 比法：同一個 instance、同一個 seed 跟 baseline 比，依序看有沒有找到解 → 有沒有證明最佳 → 都證明最佳比時間 → 都沒證明比 gap；都沒找到解算平手");
            sb.AppendLine(holdout
                ? "- 通過條件（hold-out）：一個 seed 都不能輸"
                : "- 勝出條件：一個 seed 都不能輸，而且至少贏 3 個");
            if (rows.All(r => r.VsBaseline.Length == 0))
                sb.AppendLine("- ⚠ 這份 archive 沒有 VsBaseline 欄（schema v6 之前的舊格式），無法判勝負");
            sb.AppendLine();
            sb.AppendLine("| config | n | Optimal/Feasible/NoSolution/Failed | 贏 | 輸 | 平 | 無法比較 | 結論 |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var group in groups)
            {
                var t = TallyOf(group.ToList());
                string counts = $"{t.N} | {t.Optimal}/{t.Feasible}/{t.NoSolution}/{t.Failed}";
                sb.AppendLine(group.Key == baselineLabel
                    ? $"| {Md(group.Key)} | {counts} | — | — | — | — | baseline |"
                    : $"| {Md(group.Key)} | {counts} | {t.Wins} | {t.Losses} | {t.Ties} | {t.NotCompared} | {Verdict(t, holdout)} |");
            }

            var instances = rows.Select(r => r.Model).Distinct().ToList();
            if (instances.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine("逐 instance（判讀用，結論看上表）：");
                sb.AppendLine();
                sb.AppendLine("| instance | config | 贏 | 輸 | 平 | 無法比較 |");
                sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
                foreach (var instance in instances)
                    foreach (var group in groups.Where(g => g.Key != baselineLabel))
                    {
                        var t = TallyOf(group.Where(r => r.Model == instance).ToList());
                        sb.AppendLine($"| {Md(instance)} | {Md(group.Key)} | {t.Wins} | {t.Losses} | {t.Ties} | {t.NotCompared} |");
                    }
            }
            sb.AppendLine();
        }

        private static Tally TallyOf(List<Row> rows) => new Tally(
            rows.Count,
            rows.Count(r => r.Status == SolveStatus.Optimal),
            rows.Count(r => r.Status == SolveStatus.Feasible),
            rows.Count(r => r.Status == SolveStatus.TimeLimit),
            rows.Count(r => !r.Measurable),
            rows.Count(r => r.VsBaseline == CsvExperimentWriter.Win),
            rows.Count(r => r.VsBaseline == CsvExperimentWriter.Lose),
            rows.Count(r => r.VsBaseline == CsvExperimentWriter.Tie),
            rows.Count(r => r.VsBaseline != CsvExperimentWriter.Win && r.VsBaseline != CsvExperimentWriter.Lose && r.VsBaseline != CsvExperimentWriter.Tie));

        // §4：一般輪一個 seed 都不能輸、而且至少贏 3 個；hold-out 一個 seed 都不能輸
        private static string Verdict(Tally t, bool holdout) =>
            holdout ? (t.Losses == 0 ? "通過" : "不通過")
            : t.Losses == 0 && t.Wins >= 3 ? "勝出" : "不換";

        #endregion

        #region 結果不變式（§0.1.1）

        // 優先使用模型統計中記錄的 solver 目標式方向；舊備存檔缺少這項資料時，才比較目前最佳解與界限，推測是最小化或最大化。
        private static void AppendInvariants(StringBuilder sb, TunerWorkspace workspace, double? contractGap)
        {
            // archive 累積檔裡所有調參輪次的 trial（正式求解等其他 Experiment 不算）
            var all = LoadRows(workspace).Where(r => RoundExperiment.IsMatch(r.Experiment)).ToList();
            double gap = contractGap ?? CplexDefaultMipGap;

            sb.AppendLine($"## 結果不變式（§0.1.1；跨全部已 archive round 共 {all.Count} 筆 trial，逐 instance）");
            sb.AppendLine();
            sb.AppendLine("| instance | sense | best incumbent | best bound | Optimal objective 分散 | bound 越線 | 判定 |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

            var details = new List<string>();
            foreach (var instance in all.GroupBy(r => r.Model))
            {
                var incumbents = instance.Where(r => r.HasSolution).ToList();
                if (incumbents.Count == 0)
                {
                    sb.AppendLine($"| {instance.Key} | n/a | n/a | n/a | n/a | 0 | 無 incumbent（情境 C）|");
                    continue;
                }

                bool minEvidence = incumbents.Any(r => r.BestBound < r.ObjectiveValue - Tol(r.ObjectiveValue));
                bool maxEvidence = incumbents.Any(r => r.BestBound > r.ObjectiveValue + Tol(r.ObjectiveValue));
                var recorded = instance.Select(r => r.SolverObjective).FirstOrDefault(s => s.HasValue);
                bool maximize;
                string sense;
                if (recorded.HasValue)
                {
                    maximize = recorded.Value == ObjectiveSense.Maximize;
                    // 最小化時界限不應高於可行解；最大化時界限不應低於可行解，超出容差就標成矛盾。
                    sense = (maximize ? minEvidence : maxEvidence) ? $"矛盾（模型 {recorded.Value}）" : $"{recorded.Value}（模型）";
                }
                else
                {
                    maximize = maxEvidence && !minEvidence;
                    sense = minEvidence && maxEvidence ? "矛盾" : minEvidence ? "Minimize（推論）" : maxEvidence ? "Maximize（推論）" : "未定（gap 皆 0）";
                }

                double best = maximize ? incumbents.Max(r => r.ObjectiveValue) : incumbents.Min(r => r.ObjectiveValue);
                double bestBound = maximize ? incumbents.Min(r => r.BestBound) : incumbents.Max(r => r.BestBound);
                var crossing = incumbents
                    .Where(r => maximize ? r.BestBound < best - Tol(best) : r.BestBound > best + Tol(best))
                    .ToList();
                foreach (var r in crossing)
                    details.Add($"- 越線：{r.Experiment} | {r.Model} | {r.Label} bound={Num(r.BestBound)} vs best incumbent={Num(best)}");

                var optimal = incumbents.Where(r => r.Status == SolveStatus.Optimal).Select(r => r.ObjectiveValue).ToList();
                double spread = optimal.Count < 2 ? 0 : optimal.Max() - optimal.Min();
                bool spreadBroken = spread > gap * Math.Abs(best) + 1e-6 + Tol(best);
                if (spreadBroken)
                    details.Add($"- 分散：{instance.Key} 的 Optimal objective 相差 {Num(spread)}，超過 MipGap {gap} 容許的 {Num(gap * Math.Abs(best))}");

                bool pass = !sense.StartsWith("矛盾", StringComparison.Ordinal) && crossing.Count == 0 && !spreadBroken;
                sb.AppendLine(
                    $"| {instance.Key} | {sense} | {Num(best)} | {Num(bestBound)} " +
                    $"| {Num(spread)}（容許 {Num(gap * Math.Abs(best))}）| {crossing.Count} | {(pass ? "PASS" : "FAIL")} |");
            }

            sb.AppendLine();
            if (details.Count > 0)
            {
                sb.AppendLine("FAIL 明細（越線 = 數學上不可能，代表容差被放寬或模型被換掉；多筆同時出現 → §7.1 停止條件 G）：");
                sb.AppendLine();
                foreach (var d in details) sb.AppendLine(d);
                sb.AppendLine();
            }
        }

        private static double Tol(double x) => 1e-6 * Math.Max(1, Math.Abs(x));

        private static string InferSense(double objective, double bound)
        {
            if (double.IsNaN(objective) || double.IsNaN(bound)) return "n/a";
            if (bound < objective - Tol(objective)) return "Minimize";
            if (bound > objective + Tol(objective)) return "Maximize";
            return "未定（gap=0）";
        }

        #endregion

        #region dynamic search（§2.3.1）

        private static void AppendDynamicSearch(StringBuilder sb, string experimentName)
        {
            sb.AppendLine("## dynamic search（§2.3.1）");
            sb.AppendLine();

            string dir = FolderDir.Log.GetPath();
            var log = Directory.Exists(dir)
                ? new DirectoryInfo(dir).GetFiles($"{experimentName}_exp_*.txt").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                : null;
            if (log == null)
            {
                sb.AppendLine("- 找不到本輪 log（bin/Logs 可能已被 clean 或 retention 清掉）→ 人工確認");
                return;
            }

            int dynamicSearch = 0, traditional = 0;
            // log 仍在寫入，讀取時須使用 FileShare.ReadWrite。
            using (var stream = new FileStream(log.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
                while (reader.ReadLine() is string line)
                {
                    if (line.Contains("MIP search method: dynamic search", StringComparison.OrdinalIgnoreCase)) dynamicSearch++;
                    else if (line.Contains("traditional branch-and-cut", StringComparison.OrdinalIgnoreCase)) traditional++;
                }

            sb.AppendLine($"- log：`{log.FullName}`");
            sb.AppendLine($"- dynamic search = {dynamicSearch}（含 warm-up 1 次），traditional branch-and-cut = {traditional}");
            sb.AppendLine(traditional > 0
                ? "- **FAIL**：有 solve 退回 traditional B&C → 依 §4.1 淘汰並查明 control callback 來源"
                : dynamicSearch == 0
                    ? "- 沒有 MIP search 紀錄（純 LP 或 log 不完整）→ 人工確認"
                    : "- PASS：無 dynamic search 停用");
        }

        #endregion

        #region archive 讀取（累積檔 -trial.csv + 說明檔 -meta.csv）

        private sealed class Row
        {
            public Row(Dictionary<string, string> cells, Dictionary<string, string> baseline, Dictionary<string, string> model)
            {
                Experiment = Cell(cells, "Experiment");
                RunId = Cell(cells, "RunId");
                Model = Cell(cells, "Model");
                Label = Cell(cells, "TrialLabel");
                ConfigLabel = SeedSuffix.Replace(Label, "");
                Config = ApplyDiff(baseline, Cell(cells, "ConfigChanges"));
                Seed = int.TryParse(Cell(cells, "Seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed) ? seed : null;
                MipGapContract = Number(Config.GetValueOrDefault("MipGap"));
                // 試跑若在求解前失敗，CSV 的求解指標會空白；此時將狀態視為 NotSolved。
                Status = Enum.TryParse(Cell(cells, "Status"), out SolveStatus status) ? status : SolveStatus.NotSolved;
                ObjectiveValue = Number(Cell(cells, "ObjectiveValue")) ?? double.NaN;
                BestBound = Number(Cell(cells, "BestBound")) ?? double.NaN;
                Gap = Number(Cell(cells, "Gap")) ?? double.NaN;
                SolveTimeMs = Number(Cell(cells, "SolveTimeMs")) ?? 0;
                VsBaseline = Cell(cells, "VsBaseline");
                SolverObjective = ParseSense(model.GetValueOrDefault($"{Model}.objectiveSense"));
            }

            public string Experiment { get; }
            public string RunId { get; }
            public string Model { get; }
            public string Label { get; }
            public string ConfigLabel { get; }
            public int? Seed { get; }
            public double? MipGapContract { get; }
            public Dictionary<string, string> Config { get; }
            public SolveStatus Status { get; }
            public double ObjectiveValue { get; }
            public double BestBound { get; }
            public double Gap { get; }
            public double SolveTimeMs { get; }

            /// <summary>框架判好的勝負（baseline / win / lose / tie / n/a）；schema v6 之前的舊備存檔沒有這欄，為空字串。</summary>
            public string VsBaseline { get; }

            /// <summary>說明檔 model 區段記下的目標式方向（取自 CPLEX）；schema v11 之前的舊備存檔沒有這項，為 null。</summary>
            public ObjectiveSense? SolverObjective { get; }

            public bool HasSolution => !double.IsNaN(ObjectiveValue);

            // Infeasible / Unbounded / Error / NotSolved 不進彙總（§4.1 直接淘汰），只計數
            public bool Measurable => Status is SolveStatus.Optimal or SolveStatus.Feasible or SolveStatus.TimeLimit;
        }

        // archive 累積檔的每一列；基準設定與目標式方向取自同一批（Experiment + RunId）的說明檔
        private static List<Row> LoadRows(TunerWorkspace workspace)
        {
            string path = Path.Combine(workspace.ArchiveDir, workspace.ArtifactName("trial"));
            if (!File.Exists(path)) return new List<Row>();

            var meta = LoadMeta(workspace);
            return TunerWorkspace.ReadTable(path).Select(cells =>
            {
                var run = meta.GetValueOrDefault((Cell(cells, "Experiment"), Cell(cells, "RunId")))
                    ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                var baseline = Section(run, "baseline").Where(kv => kv.Key != "label")
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                return new Row(cells, baseline, Section(run, "model"));
            }).ToList();
        }

        // 一輪 = Experiment 欄等於這一輪名稱、RunId 最新的那一批；同一輪在 bin 重跑過時，較早的批次照留但不算
        private static List<Row> LoadRound(TunerWorkspace workspace, string shortName)
        {
            var rows = LoadRows(workspace).Where(r => r.Experiment == shortName).ToList();
            if (rows.Count == 0) return rows;
            string latest = rows.Select(r => r.RunId).Max(StringComparer.Ordinal)!;
            return rows.Where(r => r.RunId == latest).ToList();
        }

        // -meta.csv 每批一份，每份有 Section、Key、Value；先按批次（Experiment + RunId）、再按 Section 分組，用 Key 查詢 Value。
        private static Dictionary<(string Experiment, string RunId), Dictionary<string, Dictionary<string, string>>> LoadMeta(TunerWorkspace workspace)
        {
            var meta = new Dictionary<(string Experiment, string RunId), Dictionary<string, Dictionary<string, string>>>();
            string path = Path.Combine(workspace.ArchiveDir, workspace.ArtifactName("meta"));
            if (!File.Exists(path)) return meta;

            foreach (var row in TunerWorkspace.ReadTable(path))
            {
                var key = (Cell(row, "Experiment"), Cell(row, "RunId"));
                if (!meta.TryGetValue(key, out var sections))
                    meta[key] = sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                string name = Cell(row, "Section");
                if (!sections.TryGetValue(name, out var section))
                    sections[name] = section = new Dictionary<string, string>(StringComparer.Ordinal);
                section[Cell(row, "Key")] = Cell(row, "Value");
            }
            return meta;
        }

        private static Dictionary<string, string> Section(Dictionary<string, Dictionary<string, string>> meta, string name) =>
            meta.TryGetValue(name, out var section) ? section : new Dictionary<string, string>(StringComparer.Ordinal);

        // 軌跡檔可能很大：逐列串流，只看 Experiment、RunId 兩欄
        private static bool HasTrajectory(TunerWorkspace workspace, string experiment, string runId)
        {
            string path = Path.Combine(workspace.ArchiveDir, workspace.ArtifactName("trajectory"));
            if (!File.Exists(path)) return false;
            using var reader = new StreamReader(path, Encoding.UTF8);
            return CsvCtrl.ParseCsv(reader).Skip(1).Any(r => r.Length > 2 && r[1] == experiment && r[2] == runId);
        }

        // 主表以「參數=值;…」記錄相較基準的修改；先讀取 -meta.csv 的完整基準設定，再套用差異，還原這次試跑的設定。「參數=預設」表示移除自訂值。
        private static Dictionary<string, string> ApplyDiff(Dictionary<string, string> baseline, string diff)
        {
            var config = new Dictionary<string, string>(baseline, StringComparer.Ordinal);
            foreach (string part in diff.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq);
                string value = part.Substring(eq + 1);
                if (value == "預設") config.Remove(key);
                else config[key] = value;
            }
            return config;
        }

        // -meta.csv 的 objectiveSense 寫 Minimize / Maximize；沒有目標式（none）或舊備存檔沒這項時為 null
        private static ObjectiveSense? ParseSense(string? objectiveSense) =>
            Enum.TryParse(objectiveSense, out ObjectiveSense sense) ? sense : null;

        private static string Cell(Dictionary<string, string> cells, string column) =>
            cells.TryGetValue(column, out var value) ? value : "";

        private static double? Number(string? text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;

        private static string Num(double d) => double.IsNaN(d) ? "NaN" : d.ToString("G10", CultureInfo.InvariantCulture);

        private static string Raw(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        private static string Md(string text) => text.Replace("|", "\\|");

        #endregion
    }
}
