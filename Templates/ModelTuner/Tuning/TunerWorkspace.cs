using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace ModelTuner
{
    /// <summary>一份待求解的模型檔。Name 是寫入實驗紀錄的模型名稱（Trial.Model）。</summary>
    public sealed record TuningInstance(string Name, string RelativePath, string FullPath);

    /// <summary>
    /// 管理調參專案的模型檔、輸出位置，以及 S0 階段建立的 instances.lock。
    /// 模型檔包含模型與資料；instances.lock 記錄每個檔的 SHA-256 雜湊值，之後每次執行都比對，
    /// 檔案新增、遺失或內容變更就中止，確保各輪調參使用相同的模型與資料。
    /// </summary>
    public sealed class TunerWorkspace
    {
        public const string LockFileName = "instances.lock";

        private static readonly string[] ModelExtensions = { ".lp", ".mps", ".sav" };
        private static readonly string[] Compressions = { ".gz", ".bz2" };

        // 框架匯出的檔名帶用途與時間戳（RosteringProblem_SAV_2026-08-30_17-36-04.sav），移除這些部分，取得固定的模型名稱
        private static readonly Regex FrameworkStamp =
            new Regex(@"_(LP|MPS|SAV)_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$", RegexOptions.IgnoreCase);

        private TunerWorkspace(OptProject project, string root)
        {
            Project = project;
            Root = root;
            Tune = Scan("tune");
            Holdout = Scan("holdout");
        }

        /// <summary>管理 log、資料夾與檔案保留天數的 OptProject；每輪實驗都透過它建立。</summary>
        public OptProject Project { get; }

        /// <summary>專案名稱，作為輸出檔名與實驗名稱的前綴。</summary>
        public string ProjectName => Project.Name;

        /// <summary>專案根（csproj 所在目錄）。Instances/、Experiments/、instances.lock、TuningHistory.md 都在這裡。</summary>
        public string Root { get; }

        /// <summary>用來比較參數設定的模型檔，讀自 Instances/tune。</summary>
        public IReadOnlyList<TuningInstance> Tune { get; }

        /// <summary>保留到最後驗證的模型檔，讀自 Instances/holdout；不參與挑選參數設定。</summary>
        public IReadOnlyList<TuningInstance> Holdout { get; }

        /// <summary>保存實驗原始輸出檔的目錄；清理 bin 時不會刪除這裡的檔案。</summary>
        public string ArchiveDir => Path.Combine(Root, "Experiments");

        public string LockPath => Path.Combine(Root, LockFileName);

        public static TunerWorkspace Open(OptProject project)
        {
            var workspace = new TunerWorkspace(project, FindProjectRoot());
            Logging.Info($"[ModelTuner] root={workspace.Root} tune={workspace.Tune.Count} holdout={workspace.Holdout.Count}");

            var duplicated = workspace.Tune.Concat(workspace.Holdout)
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicated != null)
                throw Logging.ErrorOnce(
                    new InvalidOperationException($"Instance name '{duplicated.Key}' is used by more than one model file."),
                    "INSTANCE_NAME_DUPLICATED", "instance 名稱重複", nameof(Open), duplicated.Key,
                    "trial_model_name_must_be_unique", $"files={string.Join("|", duplicated.Select(i => i.RelativePath))}");

            return workspace;
        }

        /// <summary>實驗完整名稱，與 OptExperiment.FullName 相同：{專案名}-tuning-r&lt;N&gt;[-holdout]；log 檔名與統計報告檔名使用這個名稱。</summary>
        public string ExperimentName(int round, bool holdout = false) =>
            $"{ProjectName}-{ExperimentShortName(round, holdout)}";

        /// <summary>傳給 <see cref="OptProject.Experiment"/> 的實驗名（不含專案名），也是累積檔 Experiment 欄的值。</summary>
        public static string ExperimentShortName(int round, bool holdout = false) =>
            $"tuning-r{round}{(holdout ? "-holdout" : "")}";

        #region S0 契約凍結（instances.lock）

        /// <summary>記錄模型檔雜湊到 instances.lock；既有紀錄與檔案不符時拒絕覆寫。確認更換模型後，需手動刪除 lock、重新建立，並重跑 S1 規模測試與 R0 基準量測。</summary>
        public int WriteLock()
        {
            var all = Tune.Concat(Holdout).ToList();
            if (all.Count == 0)
            {
                Logging.Error($"[INSTANCE_SET_EMPTY] 沒有可凍結的模型檔 | context={nameof(WriteLock)} value={Path.Combine(Root, "Instances")} reason=no_model_file result=aborted");
                return 2;
            }

            var current = all.Select(Fingerprint).ToList();
            if (File.Exists(LockPath))
            {
                var mismatches = Compare(ReadLock(), current);
                if (mismatches.Count == 0)
                {
                    Logging.Info($"[InstanceLock] 已凍結且一致 | files={current.Count}");
                    return 0;
                }

                foreach (var m in mismatches) Logging.Error(m);
                Logging.Error($"[INSTANCE_LOCK_EXISTS] 契約已凍結，模型檔不同 | context={nameof(WriteLock)} value={LockPath} reason=instance_change_is_contract_change result=aborted");
                Logging.Error("[INSTANCE_LOCK_EXISTS] 確認要換模型檔：手動刪除 instances.lock 後重新 lock，並從 S1 sizing / R0 重來");
                return 3;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# {ProjectName} instances.lock — S0 契約凍結（dotnet run -- lock 產生，NEVER 手改）");
            sb.AppendLine("# 格式：sha256 bytes 相對路徑；任何一行對不上 = 模型或資料被換掉，既有 round 全部作廢");
            foreach (var f in current) sb.AppendLine($"{f.Sha256} {f.Bytes} {f.RelativePath}");
            File.WriteAllText(LockPath, sb.ToString(), new UTF8Encoding(false));

            foreach (var f in current) Logging.Info($"[InstanceLock] {f.RelativePath} sha256={f.Sha256} bytes={f.Bytes}");
            Logging.Info($"[InstanceLock] 已寫入 {LockPath}（{current.Count} 個檔）");
            return 0;
        }

        /// <summary>比對模型檔是否符合 instances.lock。required=false 允許尚未建立 lock 時繼續，只供正式求解模式在 S0 前先驗證模型。</summary>
        public bool VerifyLock(bool required)
        {
            if (!File.Exists(LockPath))
            {
                if (!required)
                {
                    Logging.Warn($"[INSTANCE_LOCK_MISSING] 模型檔尚未凍結 | value={LockPath} reason=before_s0 result=continued");
                    return true;
                }

                Logging.Error($"[INSTANCE_LOCK_MISSING] 模型檔尚未凍結 | context={nameof(VerifyLock)} value={LockPath} reason=run_lock_first result=aborted");
                return false;
            }

            var mismatches = Compare(ReadLock(), Tune.Concat(Holdout).Select(Fingerprint).ToList());
            foreach (var m in mismatches) Logging.Error(m);
            if (mismatches.Count > 0) return false;

            Logging.Info($"[InstanceLock] 指紋一致 | files={Tune.Count + Holdout.Count}");
            return true;
        }

        private sealed record FileFingerprint(string Sha256, long Bytes, string RelativePath);

        private FileFingerprint Fingerprint(TuningInstance instance)
        {
            using var stream = File.OpenRead(instance.FullPath);
            string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return new FileFingerprint(hash, new FileInfo(instance.FullPath).Length, instance.RelativePath);
        }

        private List<FileFingerprint> ReadLock() =>
            File.ReadAllLines(LockPath)
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
                .Select(line => line.Split(' ', 3))
                .Select(p => new FileFingerprint(p[0], long.Parse(p[1]), p[2]))
                .ToList();

        private static List<string> Compare(List<FileFingerprint> locked, List<FileFingerprint> current)
        {
            var mismatches = new List<string>();
            var now = current.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
            foreach (var l in locked)
            {
                if (!now.TryGetValue(l.RelativePath, out var c))
                    mismatches.Add($"[INSTANCE_LOCK_MISMATCH] 凍結的模型檔不見了 | value={l.RelativePath} reason=missing result=aborted");
                else if (!string.Equals(l.Sha256, c.Sha256, StringComparison.OrdinalIgnoreCase))
                    mismatches.Add($"[INSTANCE_LOCK_MISMATCH] 模型檔內容被換掉 | value={l.RelativePath} reason=sha256_changed locked={l.Sha256} current={c.Sha256} result=aborted");
            }

            var lockedPaths = new HashSet<string>(locked.Select(l => l.RelativePath), StringComparer.OrdinalIgnoreCase);
            foreach (var c in current.Where(c => !lockedPaths.Contains(c.RelativePath)))
                mismatches.Add($"[INSTANCE_LOCK_MISMATCH] 出現未凍結的模型檔 | value={c.RelativePath} reason=not_in_lock result=aborted");

            return mismatches;
        }

        #endregion

        #region 原始證據（bin/Experiment → Experiments/）

        // 每個專案共用四個累積檔，以 Experiment（tuning-r<N>）與 RunId 區分各輪結果。
        private static readonly string[] ArtifactKinds = { "trial", "meta", "summary", "trajectory" };

        /// <summary>累積檔名：{專案名}-trial.csv / -meta.csv / -summary.csv / -trajectory.csv。</summary>
        public string ArtifactName(string kind) => $"{ProjectName}-{kind}.csv";

        /// <summary>archive 的 -trial.csv 已有這一輪（Experiment 欄 = 實驗名）的列。</summary>
        public bool IsArchived(string shortName)
        {
            string path = Path.Combine(ArchiveDir, ArtifactName("trial"));
            return File.Exists(path) && ReadTable(path).Any(row => row.GetValueOrDefault("Experiment") == shortName);
        }

        /// <summary>
        /// 執行前檢查：archive 已有這一輪就拒絕執行（archive 的輪次不可變）。
        /// bin 的累積檔遺失但 archive 仍在時，先還原到 bin，讓新結果接在完整歷史之後。
        /// </summary>
        public bool PrepareRun(string shortName)
        {
            if (IsArchived(shortName))
            {
                Logging.Error($"[ROUND_ALREADY_ARCHIVED] 本輪已 archive，禁止重跑 | context={nameof(PrepareRun)} value={shortName} reason=archived_round_is_immutable result=aborted");
                Logging.Error("[ROUND_ALREADY_ARCHIVED] 要重做請開新的 r<N+1>，並在 TuningHistory.md 註明是 replication");
                return false;
            }

            foreach (var kind in ArtifactKinds)
            {
                string archived = Path.Combine(ArchiveDir, ArtifactName(kind));
                string working = FolderDir.Experiment.GetPathFile(ArtifactName(kind));
                if (!File.Exists(archived) || File.Exists(working)) continue;
                FolderDir.Experiment.CreateFolder();
                File.Copy(archived, working);
                Logging.Warn($"[ROUND_HISTORY_RESTORED] bin 沒有累積檔，先從 archive 複製回來再接著寫 | value={working} source={archived} result=restored");
            }
            return true;
        }

        /// <summary>
        /// 把 bin 的累積檔複製到專案根目錄的 Experiments/（覆寫）。-trial / -meta / -summary 必須有本輪的列，-trajectory 有才複製。
        /// 只允許附加紀錄：覆寫前確認 bin 檔以 archive 的完整內容開頭；不符就拒絕，保留舊結果。
        /// </summary>
        public IReadOnlyList<string> Archive(string shortName)
        {
            Directory.CreateDirectory(ArchiveDir);
            var copied = new List<string>();
            foreach (var kind in ArtifactKinds)
            {
                string source = FolderDir.Experiment.GetPathFile(ArtifactName(kind));
                string target = Path.Combine(ArchiveDir, ArtifactName(kind));
                if (!File.Exists(source))
                {
                    if (kind == "trajectory") continue;
                    throw Logging.ErrorOnce(
                        new FileNotFoundException($"Experiment artifact '{source}' is missing.", source),
                        "ROUND_ARCHIVE_FAILED", "本輪 archive 失敗", nameof(Archive), source, "required_artifact_missing");
                }
                if (kind != "trajectory" && !ReadTable(source).Any(row => row.GetValueOrDefault("Experiment") == shortName))
                    throw Logging.ErrorOnce(
                        new InvalidDataException($"Experiment artifact '{source}' has no rows for '{shortName}'."),
                        "ROUND_ARCHIVE_FAILED", "本輪 archive 失敗", nameof(Archive), source, "round_rows_missing");
                if (File.Exists(target) && !StartsWith(source, target))
                    throw Logging.ErrorOnce(
                        new InvalidDataException($"Archive '{target}' is not the beginning of '{source}'."),
                        "ROUND_ARCHIVE_FAILED", "本輪 archive 失敗", nameof(Archive), target, "archive_is_not_a_prefix_of_bin");

                File.Copy(source, target, overwrite: true);
                copied.Add(target);
                Logging.Info($"[Archive] {target}");
            }
            return copied;
        }

        // 確認 bin 檔保留 archive 的全部內容，僅在檔尾新增資料。
        private static bool StartsWith(string path, string prefixPath)
        {
            byte[] prefix = File.ReadAllBytes(prefixPath);
            using var stream = File.OpenRead(path);
            if (stream.Length < prefix.Length) return false;
            var head = new byte[prefix.Length];
            stream.ReadExactly(head);
            return head.AsSpan().SequenceEqual(prefix);
        }

        /// <summary>依表頭讀取 CSV，不依賴欄位順序；StreamReader 會略過 UTF-8 BOM。</summary>
        internal static List<Dictionary<string, string>> ReadTable(string path)
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

        #endregion

        private List<TuningInstance> Scan(string group)
        {
            string dir = Path.Combine(Root, "Instances", group);
            if (!Directory.Exists(dir)) return new List<TuningInstance>();

            return Directory.GetFiles(dir)
                .Where(IsModelFile)
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => new TuningInstance(
                    InstanceName(f),
                    Path.GetRelativePath(Path.Combine(Root, "Instances"), f).Replace('\\', '/'),
                    Path.GetFullPath(f)))
                .ToList();
        }

        private static bool IsModelFile(string path)
        {
            string name = Path.GetFileName(path);
            string? compression = Compressions.FirstOrDefault(c => name.EndsWith(c, StringComparison.OrdinalIgnoreCase));
            if (compression != null) name = name.Substring(0, name.Length - compression.Length);
            return ModelExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        }

        private static string InstanceName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            // .sav.gz 有兩層副檔名，需再移除一層。
            if (Path.HasExtension(name)) name = Path.GetFileNameWithoutExtension(name);
            return FrameworkStamp.Replace(name, "");
        }

        // bin/<Configuration>/net8.0 往上找 csproj；找不到（例：exe 被複製到別處）才退回工作目錄
        private static string FindProjectRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (dir.GetFiles("*.csproj").Length > 0)
                    return dir.FullName;

            Logging.Warn($"[PROJECT_ROOT_FALLBACK] 找不到 csproj，改用工作目錄 | value={Environment.CurrentDirectory} result=continued");
            return Environment.CurrentDirectory;
        }
    }
}
