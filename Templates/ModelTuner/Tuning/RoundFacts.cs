using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;

namespace ModelTuner
{
    /// <summary>
    /// 從 archive CSV（主表 + -meta.csv）產出每輪的機械事實：TUNING-FACTS block（solver-tuning-guide §6.2.2）、
    /// 三種主指標的彙總與 θ（§3.0 / §4.3）、結果不變式（§0.1.1）、dynamic search 檢查（§2.3.1）。
    /// 只算不判：用哪個主指標、誰是 champion、promote 或 retain，仍由分析者依契約與 §4 決定。
    /// </summary>
    public static class RoundFacts
    {
        private static readonly Regex RoundPattern = new Regex(@"-tuning-r(\d+)(-holdout)?$");
        private static readonly Regex RoundShorthand = new Regex(@"^r\d+(-holdout)?$");
        private static readonly Regex SeedSuffix = new Regex(@"-s\d+$");
        private static readonly Regex SolverSense = new Regex(@"solver=(\w+)");

        // CPLEX 的 MipGap 預設；baseline 沒設 MipGap 時拿它當 Optimal 分散的容許值
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
            var rows = LoadRows(workspace, experimentName);
            if (!match.Success || rows.Count == 0)
            {
                Logging.Error($"[FACTS_SOURCE_MISSING] 找不到本輪 archive | context={nameof(Report)} value={Path.Combine(workspace.ArchiveDir, experimentName + ".csv")} reason={(match.Success ? "archive_missing_or_empty" : "not_a_tuning_round_name")} result=aborted");
                return 2;
            }

            int round = int.Parse(match.Groups[1].Value);
            bool holdout = match.Groups[2].Success;
            string baselineLabel = $"r{round}-baseline";

            var r0Rows = round == 0 && !holdout ? rows : LoadRows(workspace, workspace.ExperimentName(0));
            var r0Baseline = r0Rows.Where(r => r.ConfigLabel == "r0-baseline").ToList();
            var reference = r0Baseline.Count > 0 ? r0Baseline : rows.Where(r => r.ConfigLabel == baselineLabel).ToList();
            double shift = ChooseShift(reference, out double medianSec);
            var theta = ComputeTheta(r0Baseline, shift);

            var report = new StringBuilder();
            AppendSummary(report, workspace, experimentName, rows, baselineLabel, shift, medianSec, r0Baseline.Count > 0, theta);
            AppendModelStats(report, rows, Section(LoadMeta(workspace, experimentName), "modelStats"));
            AppendInvariants(report, workspace, rows.FirstOrDefault(r => r.ConfigLabel == baselineLabel)?.MipGapContract);
            AppendDynamicSearch(report, experimentName);

            var file = new StringBuilder();
            file.AppendLine($"# {experimentName} — machine facts");
            file.AppendLine();
            file.AppendLine("> 由 `dotnet run -- facts` 從 archive CSV 產生，NEVER 手改；History 引用數字一律指回這裡。");
            file.AppendLine();
            AppendFactsBlock(file, workspace, experimentName, $"R{round}{(holdout ? "-holdout" : "")}", rows, baselineLabel);
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

        /// <summary>production 模式每個 instance 一行，給契約區塊的「參考結果基線」抄。</summary>
        public static string DescribeProduction(TuningInstance instance, SolveMetrics? m)
        {
            if (m == null) return $"[Production] {instance.Name} Status=NotSolved";
            var recorded = m.ModelStats?.Solver?.Objective;
            return $"[Production] {instance.Name} Status={m.Status} Objective={Num(m.ObjectiveValue)} BestBound={Num(m.BestBound)} " +
                   $"MipGap={Num(m.MipGap)} RunTimeMs={m.RunTimeMs:0} " +
                   $"Sense={(recorded.HasValue ? recorded.Value.ToString() : InferSense(m.ObjectiveValue, m.BestBound))} " +
                   $"ModelStats={m.ModelStats?.Summary ?? "n/a"}";
        }

        #region 模型統計對帳（建模記帳 vs solver 模型）

        // 每個 trial 在 Solve() 前都對帳過一次；-meta.csv 的 modelStats 區段每個 instance 一份（有不一致就是那一筆），這裡原樣列出
        private static void AppendModelStats(StringBuilder sb, List<Row> rows, Dictionary<string, string> modelStats)
        {
            sb.AppendLine("## 模型統計對帳（框架建模記帳 vs solver 模型；Solve() 前逐 trial 擷取）");
            sb.AppendLine();

            var instances = rows.Select(r => r.Model).Distinct().Where(m => modelStats.ContainsKey($"{m}.result")).ToList();
            if (instances.Count == 0)
            {
                sb.AppendLine("- archive 沒有對帳資料（framework 加入模型統計對帳之前的紀錄）→ 人工確認");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("| instance | 結果 | 變數 | 型別 記帳/solver | 限制式 | 特殊元素 | 目標式 | 不一致 trial |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");

            var details = new List<string>();
            foreach (var instance in instances)
            {
                sb.AppendLine(
                    $"| {Md(instance)} | {Stat(modelStats, instance, "result")} | {Stat(modelStats, instance, "variables")} " +
                    $"| {Stat(modelStats, instance, "types")} | {Stat(modelStats, instance, "constraints")} " +
                    $"| {Stat(modelStats, instance, "specialElements")} | {Stat(modelStats, instance, "objective")} " +
                    $"| {Stat(modelStats, instance, "mismatchTrials")} |");
                string prefix = $"{instance}.mismatch.";
                foreach (var kv in modelStats.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)))
                    details.Add($"- {instance} · {kv.Key.Substring(prefix.Length)}：{kv.Value}");
            }

            sb.AppendLine();
            if (details.Count > 0)
            {
                sb.AppendLine("不一致明細（log 另有逐 trial 的 `[MODEL_STATS_MISMATCH]`；框架統計不涵蓋的部分，facts 的彙總與不變式也看不到）：");
                sb.AppendLine();
                foreach (var d in details) sb.AppendLine(d);
                sb.AppendLine();
            }
        }

        private static string Stat(Dictionary<string, string> modelStats, string instance, string field) =>
            Md(modelStats.GetValueOrDefault($"{instance}.{field}", ""));

        #endregion

        #region TUNING-FACTS（§6.2.2）

        // 數值逐字取自 archive 主表（R 格式來回不失真）；一列一 trial
        private static void AppendFactsBlock(StringBuilder sb, TunerWorkspace workspace, string experimentName, string marker, List<Row> rows, string baselineLabel)
        {
            var baseline = rows.FirstOrDefault(r => r.ConfigLabel == baselineLabel);
            bool hasTrajectory = File.Exists(Path.Combine(workspace.ArchiveDir, $"{experimentName}-trajectory.csv"));

            sb.AppendLine($"<!-- TUNING-FACTS:{marker}:BEGIN -->");
            sb.AppendLine($"- experiment：`{experimentName}`");
            sb.AppendLine(
                $"- archive：`Experiments/{experimentName}.csv`、`Experiments/{experimentName}-meta.csv`" +
                (hasTrajectory ? $"、`Experiments/{experimentName}-trajectory.csv`" : ""));
            sb.AppendLine();
            sb.AppendLine("| instance | label | seed | status | objectiveValue | bestBound | mipGap | runTimeMs | configDiffFromBaseline |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var r in rows)
            {
                string diff = baseline == null || r.ConfigLabel == baselineLabel ? "" : Diff(baseline.Config, r.Config);
                sb.AppendLine(
                    $"| {Md(r.Model)} | {Md(r.Label)} | {r.Seed?.ToString(CultureInfo.InvariantCulture)} | {r.Status} " +
                    $"| {Raw(r.Objective)} | {Raw(r.Bound)} | {Raw(r.Gap)} | {Raw(r.RunTimeMs)} | {Md(diff)} |");
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

        #region 彙總與 θ（§3.0 產出 A、§4.3）

        private sealed record Aggregate(
            int N, int Optimal, int Feasible, int NoIncumbent, int Other, int Found,
            double? RuntimeSec, double? EndGapPct, double? TFeasSec);

        private sealed record Theta(double Runtime, double EndGap, double TFeas, int Seeds);

        private static void AppendSummary(
            StringBuilder sb, TunerWorkspace workspace, string experimentName, List<Row> rows, string baselineLabel,
            double shift, double medianSec, bool fromR0, Theta? theta)
        {
            var groups = rows.GroupBy(r => r.ConfigLabel)
                .OrderBy(g => g.Key == baselineLabel ? 0 : 1)
                .ToList();
            var baseline = groups.FirstOrDefault(g => g.Key == baselineLabel);
            var baseAggregate = baseline == null ? null : Summarize(baseline.ToList(), shift);
            double? timeLimit = rows.Select(r => r.TimeLimit).FirstOrDefault(t => t.HasValue);

            sb.AppendLine("## 彙總（主指標依契約區塊擇一；公式見 solver-tuning-guide §3.0 / §4.3）");
            sb.AppendLine();
            sb.AppendLine($"- 來源：`Experiments/{experimentName}.csv`，trials={rows.Count}，runs={rows.Select(r => r.RunId).Distinct().Count()}，instances={rows.Select(r => r.Model).Distinct().Count()}");
            sb.AppendLine($"- shift = {shift:0}s（{(fromR0 ? "R0" : "本輪")} baseline runtime 中位數 {medianSec:0.###}s：< 60s 取 1s，否則 10s）");
            sb.AppendLine(timeLimit.HasValue
                ? $"- PAR10 = 10 × TimeLimit = {10 * timeLimit.Value:0.###}s：非 Optimal 的 runtime、無解的 t_feas 以此計入"
                : "- ⚠ baseline 沒設 TimeLimit，無法套 PAR10：非 Optimal 的 runtime 以實際秒數計入，會低估逾時");
            sb.AppendLine(theta == null
                ? "- θ：R0 尚未 archive（或 seed < 2）→ 本表的改善一律不能下結論"
                : $"- θ（R0 baseline，{theta.Seeds} seeds，逐 seed 先跨 instance 彙總）：runtime = {theta.Runtime:0.###}（比值）｜endGap = {theta.EndGap:0.###} pp｜t_feas = {theta.TFeas:0.###}（比值）｜found 差 ≤ 1 視同平手");
            sb.AppendLine("- Δ 一律「正值 = 比 baseline 好」；> θ 才算改善，≤ θ 視同平手");
            sb.AppendLine();
            sb.AppendLine("| config | n | Opt/Feas/NoSol/Other | sgm runtime (s) | Δ runtime | mean endGap (pp) | Δ endGap | found | sgm t_feas (s) | Δ t_feas |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var group in groups)
            {
                var a = Summarize(group.ToList(), shift);
                bool isBase = group.Key == baselineLabel;
                sb.AppendLine(
                    $"| {group.Key} | {a.N} | {a.Optimal}/{a.Feasible}/{a.NoIncumbent}/{a.Other} " +
                    $"| {Sec(a.RuntimeSec)} | {(isBase ? "—" : Relative(baseAggregate?.RuntimeSec, a.RuntimeSec, theta?.Runtime))} " +
                    $"| {Pp(a.EndGapPct)} | {(isBase ? "—" : Absolute(baseAggregate?.EndGapPct, a.EndGapPct, theta?.EndGap))} " +
                    $"| {a.Found}/{a.N - a.Other} {(isBase ? "" : FoundDelta(baseAggregate?.Found, a.Found))}" +
                    $"| {Sec(a.TFeasSec)} | {(isBase ? "—" : Relative(baseAggregate?.TFeasSec, a.TFeasSec, theta?.TFeas))} |");
            }

            var instances = rows.Select(r => r.Model).Distinct().ToList();
            if (instances.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine("逐 instance（判讀用，排名仍看上表）：");
                sb.AppendLine();
                sb.AppendLine("| instance | config | sgm runtime (s) | mean endGap (pp) | found |");
                sb.AppendLine("| --- | --- | --- | --- | --- |");
                foreach (var instance in instances)
                    foreach (var group in groups)
                    {
                        var a = Summarize(group.Where(r => r.Model == instance).ToList(), shift);
                        sb.AppendLine($"| {instance} | {group.Key} | {Sec(a.RuntimeSec)} | {Pp(a.EndGapPct)} | {a.Found}/{a.N - a.Other} |");
                    }
            }
            sb.AppendLine();
        }

        private static Aggregate Summarize(List<Row> rows, double shift)
        {
            var measured = rows.Where(r => r.Measurable).ToList();
            return new Aggregate(
                rows.Count,
                rows.Count(r => r.Status == SolveStatus.Optimal),
                rows.Count(r => r.Status == SolveStatus.Feasible),
                rows.Count(r => r.Status == SolveStatus.TimeLimit),
                rows.Count(r => !r.Measurable),
                measured.Count(r => r.HasIncumbent),
                Sgm(measured.Select(RuntimeSec).ToList(), shift),
                Mean(measured.Select(EndGapPct).ToList()),
                Sgm(measured.Select(TFeasSec).ToList(), shift));
        }

        // 逐 seed 先跨 instance 彙總，再量 seed 之間的離散；單一 instance 時就退化成 §3.0 的原始定義
        private static Theta? ComputeTheta(List<Row> r0Baseline, double shift)
        {
            var perSeed = r0Baseline.Where(r => r.Measurable && r.Seed.HasValue).GroupBy(r => r.Seed!.Value).ToList();
            if (perSeed.Count < 2) return null;

            var runtime = perSeed.Select(g => Sgm(g.Select(RuntimeSec).ToList(), shift)!.Value).ToList();
            var endGap = perSeed.Select(g => Mean(g.Select(EndGapPct).ToList())!.Value).ToList();
            var tfeas = perSeed.Select(g => Sgm(g.Select(TFeasSec).ToList(), shift)!.Value).ToList();
            return new Theta(
                (runtime.Max() - runtime.Min()) / Sgm(runtime, shift)!.Value,
                endGap.Max() - endGap.Min(),
                (tfeas.Max() - tfeas.Min()) / Sgm(tfeas, shift)!.Value,
                perSeed.Count);
        }

        private static double ChooseShift(List<Row> reference, out double medianSec)
        {
            var raw = reference.Where(r => r.Measurable).Select(r => r.RunTimeMs / 1000).OrderBy(x => x).ToList();
            medianSec = raw.Count == 0 ? 0
                : raw.Count % 2 == 1 ? raw[raw.Count / 2]
                : (raw[raw.Count / 2 - 1] + raw[raw.Count / 2]) / 2;
            return medianSec >= 60 ? 10 : 1;
        }

        private static double RuntimeSec(Row r) => r.Status == SolveStatus.Optimal ? r.RunTimeMs / 1000 : Par10(r);

        private static double EndGapPct(Row r) => r.HasIncumbent ? r.Gap * 100 : 100;

        // 有解但軌跡沒抓到首解（求解太快、純 LP）時以總時間當上界
        private static double TFeasSec(Row r) => r.HasIncumbent ? (r.TFeasMs ?? r.RunTimeMs) / 1000 : Par10(r);

        private static double Par10(Row r) => r.TimeLimit.HasValue ? 10 * r.TimeLimit.Value : r.RunTimeMs / 1000;

        private static double? Sgm(List<double> values, double shift) =>
            values.Count == 0 ? null : Math.Exp(values.Average(v => Math.Log(v + shift))) - shift;

        private static double? Mean(List<double> values) => values.Count == 0 ? null : values.Average();

        private static string Relative(double? baseline, double? value, double? theta)
        {
            if (baseline is not double b || value is not double v || b == 0) return "n/a";
            double delta = (b - v) / b;
            return $"{delta.ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture)}{Verdict(delta, theta)}";
        }

        private static string Absolute(double? baseline, double? value, double? theta)
        {
            if (baseline is not double b || value is not double v) return "n/a";
            double delta = b - v;
            return $"{delta.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)} pp{Verdict(delta, theta)}";
        }

        private static string Verdict(double delta, double? theta) =>
            theta is not double t ? "（θ 未定）"
            : delta > t ? "（> θ）"
            : delta < -t ? "（變差 > θ）"
            : "（≤ θ 平手）";

        private static string FoundDelta(int? baseline, int found)
        {
            if (baseline is not int b) return "";
            int delta = found - b;
            return $"({delta:+0;-0;0}{(Math.Abs(delta) <= 1 ? " 平手" : "")}) ";
        }

        #endregion

        #region 結果不變式（§0.1.1）

        // sense 以模型統計對帳記下的 solver 目標式方向為準；舊 archive 沒有對帳資料時，才由 incumbent 與 bound 的相對位置推論
        private static void AppendInvariants(StringBuilder sb, TunerWorkspace workspace, double? contractGap)
        {
            // 萬用字元也會掃到 -meta.csv / -trajectory.csv，只留主表（檔名以 round 結尾）
            var all = Directory.Exists(workspace.ArchiveDir)
                ? Directory.GetFiles(workspace.ArchiveDir, $"{workspace.ProjectName}-tuning-r*.csv")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => RoundPattern.IsMatch(name!))
                    .SelectMany(name => LoadRows(workspace, name!))
                    .ToList()
                : new List<Row>();
            double gap = contractGap ?? CplexDefaultMipGap;

            sb.AppendLine($"## 結果不變式（§0.1.1；跨全部已 archive round 共 {all.Count} 筆 trial，逐 instance）");
            sb.AppendLine();
            sb.AppendLine("| instance | sense | best incumbent | best bound | Optimal objective 分散 | bound 越線 | 判定 |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

            var details = new List<string>();
            foreach (var instance in all.GroupBy(r => r.Model))
            {
                var incumbents = instance.Where(r => r.HasIncumbent).ToList();
                if (incumbents.Count == 0)
                {
                    sb.AppendLine($"| {instance.Key} | n/a | n/a | n/a | n/a | 0 | 無 incumbent（情境 C）|");
                    continue;
                }

                bool minEvidence = incumbents.Any(r => r.Bound < r.Objective - Tol(r.Objective));
                bool maxEvidence = incumbents.Any(r => r.Bound > r.Objective + Tol(r.Objective));
                var recorded = instance.Select(r => r.SolverObjective).FirstOrDefault(s => s.HasValue);
                bool maximize;
                string sense;
                if (recorded.HasValue)
                {
                    maximize = recorded.Value == ObjectiveSense.Maximize;
                    // bound 與 incumbent 的相對位置和模型方向相反，數學上不可能
                    sense = (maximize ? minEvidence : maxEvidence) ? $"矛盾（模型 {recorded.Value}）" : $"{recorded.Value}（模型）";
                }
                else
                {
                    maximize = maxEvidence && !minEvidence;
                    sense = minEvidence && maxEvidence ? "矛盾" : minEvidence ? "Minimize（推論）" : maxEvidence ? "Maximize（推論）" : "未定（gap 皆 0）";
                }

                double best = maximize ? incumbents.Max(r => r.Objective) : incumbents.Min(r => r.Objective);
                double bestBound = maximize ? incumbents.Min(r => r.Bound) : incumbents.Max(r => r.Bound);
                var crossing = incumbents
                    .Where(r => maximize ? r.Bound < best - Tol(best) : r.Bound > best + Tol(best))
                    .ToList();
                foreach (var r in crossing)
                    details.Add($"- 越線：{r.Experiment} | {r.Model} | {r.Label} bound={Num(r.Bound)} vs best incumbent={Num(best)}");

                var optimal = incumbents.Where(r => r.Status == SolveStatus.Optimal).Select(r => r.Objective).ToList();
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
            // 同一個 process 裡 log 還開著寫入，要用 ReadWrite 分享模式才讀得到
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

        #region archive 讀取（主表 .csv + 說明檔 -meta.csv）

        private sealed class Row
        {
            public Row(string experiment, Dictionary<string, string> cells, Dictionary<string, string> baseline, Dictionary<string, string> modelStats)
            {
                Experiment = experiment;
                RunId = Cell(cells, "RunId");
                Model = Cell(cells, "Model");
                Label = Cell(cells, "TrialLabel");
                ConfigLabel = SeedSuffix.Replace(Label, "");
                Config = ApplyDiff(baseline, Cell(cells, "DiffKnobs"));
                Seed = int.TryParse(Cell(cells, "Seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed) ? seed : null;
                TimeLimit = Number(Config.GetValueOrDefault("TimeLimit"));
                MipGapContract = Number(Config.GetValueOrDefault("MipGap"));
                // 沒有指標的 trial（求解前就失敗）主表整段空白，視同 NotSolved
                Status = Enum.TryParse(Cell(cells, "Status"), out SolveStatus status) ? status : SolveStatus.NotSolved;
                Objective = Number(Cell(cells, "ObjectiveValue")) ?? double.NaN;
                Bound = Number(Cell(cells, "BestBound")) ?? double.NaN;
                Gap = Number(Cell(cells, "MipGap")) ?? double.NaN;
                RunTimeMs = Number(Cell(cells, "RunTimeMs")) ?? 0;
                TFeasMs = Number(Cell(cells, "TFeasMs"));
                SolverObjective = ParseSense(modelStats.GetValueOrDefault($"{Model}.objective"));
            }

            public string Experiment { get; }
            public string RunId { get; }
            public string Model { get; }
            public string Label { get; }
            public string ConfigLabel { get; }
            public int? Seed { get; }
            public double? TimeLimit { get; }
            public double? MipGapContract { get; }
            public Dictionary<string, string> Config { get; }
            public SolveStatus Status { get; }
            public double Objective { get; }
            public double Bound { get; }
            public double Gap { get; }
            public double RunTimeMs { get; }
            public double? TFeasMs { get; }

            /// <summary>Solve() 前向 solver 讀到的目標式方向；舊 archive 沒有對帳資料時為 null。</summary>
            public ObjectiveSense? SolverObjective { get; }

            public bool HasIncumbent => !double.IsNaN(Objective);

            // Infeasible / Unbounded / Error / NotSolved 不進彙總（§4.1 直接淘汰），只計數
            public bool Measurable => Status is SolveStatus.Optimal or SolveStatus.Feasible or SolveStatus.TimeLimit;
        }

        private static List<Row> LoadRows(TunerWorkspace workspace, string experimentName)
        {
            string path = Path.Combine(workspace.ArchiveDir, $"{experimentName}.csv");
            if (!File.Exists(path)) return new List<Row>();

            var meta = LoadMeta(workspace, experimentName);
            var baseline = Section(meta, "baseline").Where(kv => kv.Key != "label")
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var modelStats = Section(meta, "modelStats");
            return ReadTable(path).Select(cells => new Row(experimentName, cells, baseline, modelStats)).ToList();
        }

        // -meta.csv 是 Section,Key,Value 三欄；依 section 分組成查表
        private static Dictionary<string, Dictionary<string, string>> LoadMeta(TunerWorkspace workspace, string experimentName)
        {
            var meta = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            string path = Path.Combine(workspace.ArchiveDir, $"{experimentName}-meta.csv");
            if (!File.Exists(path)) return meta;

            foreach (var row in ReadTable(path))
            {
                string name = Cell(row, "Section");
                if (!meta.TryGetValue(name, out var section))
                    meta[name] = section = new Dictionary<string, string>(StringComparer.Ordinal);
                section[Cell(row, "Key")] = Cell(row, "Value");
            }
            return meta;
        }

        private static Dictionary<string, string> Section(Dictionary<string, Dictionary<string, string>> meta, string name) =>
            meta.TryGetValue(name, out var section) ? section : new Dictionary<string, string>(StringComparer.Ordinal);

        // 以表頭取欄，欄位順序日後變動也不會錯位；StreamReader 會吃掉 writer 寫的 UTF-8 BOM
        private static List<Dictionary<string, string>> ReadTable(string path)
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            var records = CsvCtrl.ParseCsv(reader).ToList();
            if (records.Count == 0) return new List<Dictionary<string, string>>();

            string[] header = records[0];
            return records.Skip(1)
                .Where(r => r.Length > 1 || r[0].Length > 0)
                .Select(r => header
                    .Select((column, i) => (column, value: i < r.Length ? r[i] : ""))
                    .ToDictionary(c => c.column, c => c.value, StringComparer.Ordinal))
                .ToList();
        }

        // 主表只記「跟基準差在哪」（旋鈕=值;…，設回預設寫成「旋鈕=預設」）；基準的完整設定在 -meta.csv，兩者疊起來才是這個 trial 的設定
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

        // -meta.csv 的 objective 寫成 "framework=Minimize solver=Minimize"；沒有 solver 方向（None）時為 null
        private static ObjectiveSense? ParseSense(string? objective)
        {
            var match = SolverSense.Match(objective ?? "");
            return match.Success && Enum.TryParse(match.Groups[1].Value, out ObjectiveSense sense) ? sense : null;
        }

        private static string Cell(Dictionary<string, string> cells, string column) =>
            cells.TryGetValue(column, out var value) ? value : "";

        private static double? Number(string? text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;

        private static string Num(double d) => double.IsNaN(d) ? "NaN" : d.ToString("G10", CultureInfo.InvariantCulture);

        private static string Raw(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        private static string Md(string text) => text.Replace("|", "\\|");

        private static string Sec(double? d) => d.HasValue ? d.Value.ToString("0.###", CultureInfo.InvariantCulture) : "n/a";

        private static string Pp(double? d) => d.HasValue ? d.Value.ToString("0.00", CultureInfo.InvariantCulture) : "n/a";

        #endregion
    }
}
