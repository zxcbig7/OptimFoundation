using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OptimFoundation.Core;

namespace ModelTuner
{
    /// <summary>一份模型檔 instance。Name 是實驗紀錄裡的模型名（Trial.Model）。</summary>
    public sealed record TuningInstance(string Name, string RelativePath, string FullPath);

    /// <summary>
    /// 專案根的檔案配置與 S0 契約凍結。
    /// 模型檔本身就是凍結的「模型 + 資料」：instances.lock 記下每個檔的 SHA-256，之後每次執行都比對，
    /// 對不上就中止——這是 Phase 2 專案「model chain 不准出現在 diff」那條規則的檔案版。
    /// </summary>
    public sealed class TunerWorkspace
    {
        public const string LockFileName = "instances.lock";

        private static readonly string[] ModelExtensions = { ".lp", ".mps", ".sav" };
        private static readonly string[] Compressions = { ".gz", ".bz2" };

        // 框架匯出的檔名帶用途與時間戳（RosteringProblem_SAV_2026-08-30_17-36-04.sav），剝掉才是穩定的模型名
        private static readonly Regex FrameworkStamp =
            new Regex(@"_(LP|MPS|SAV)_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$", RegexOptions.IgnoreCase);

        private TunerWorkspace(string projectName, string root)
        {
            ProjectName = projectName;
            Root = root;
            Tune = Scan("tune");
            Holdout = Scan("holdout");
        }

        /// <summary>輸出檔名與 experiment 名的根。</summary>
        public string ProjectName { get; }

        /// <summary>專案根（csproj 所在目錄）。Instances/、Experiments/、instances.lock、TuningHistory.md 都在這裡。</summary>
        public string Root { get; }

        /// <summary>調參用 instance（Instances/tune）。</summary>
        public IReadOnlyList<TuningInstance> Tune { get; }

        /// <summary>hold-out instance（Instances/holdout）；只在 holdout 模式使用，NEVER 用來選 config。</summary>
        public IReadOnlyList<TuningInstance> Holdout { get; }

        /// <summary>每輪原始證據的永久 archive（bin 會被 clean，這裡不會）。</summary>
        public string ArchiveDir => Path.Combine(Root, "Experiments");

        public string LockPath => Path.Combine(Root, LockFileName);

        public static TunerWorkspace Open(string projectName)
        {
            var workspace = new TunerWorkspace(projectName, FindProjectRoot());
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

        public string ExperimentName(int round, bool holdout = false) =>
            $"{ProjectName}-tuning-r{round}{(holdout ? "-holdout" : "")}";

        #region S0 契約凍結（instances.lock）

        /// <summary>寫入 instances.lock。已存在且內容不同時拒絕：換模型檔 = 契約變更，要人刪 lock 並重跑 S1 / R0。</summary>
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

        /// <summary>比對 instances.lock。required=false 只給 production：S0 凍結前要先跑它確認正確性。</summary>
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

        #region 每輪原始證據（bin/Experiments → Experiments/）

        private static readonly string[] ArtifactSuffixes = { ".csv", "-meta.csv", "-trajectory.csv" };

        public bool IsArchived(string experimentName) =>
            File.Exists(Path.Combine(ArchiveDir, $"{experimentName}.csv"));

        /// <summary>
        /// 開跑前的防呆：archive 已有同名 experiment → 拒跑（archive 是不可變的證據，同名重跑的結果無處可放）；
        /// bin 殘留同名檔（例：Phase 2 管線驗證跑過）→ 刪掉，確保之後 archive 的一定是本輪產物。
        /// </summary>
        public bool PrepareRun(string experimentName)
        {
            if (IsArchived(experimentName))
            {
                Logging.Error($"[ROUND_ALREADY_ARCHIVED] 本輪已 archive，禁止重跑 | context={nameof(PrepareRun)} value={experimentName} reason=archived_round_is_immutable result=aborted");
                Logging.Error("[ROUND_ALREADY_ARCHIVED] 要重做請開新的 r<N+1>，並在 TuningHistory.md 註明是 replication");
                return false;
            }

            foreach (var suffix in ArtifactSuffixes)
            {
                string stale = FolderDir.Experiment.GetPathFile($"{experimentName}{suffix}");
                if (!File.Exists(stale)) continue;
                File.Delete(stale);
                Logging.Warn($"[ROUND_STALE_OUTPUT_REMOVED] 刪除 bin 殘留的同名實驗檔 | value={stale} reason=unarchived_leftover result=deleted");
            }
            return true;
        }

        /// <summary>把本輪 bin 產物複製到專案根 Experiments/；.csv / -meta.csv 缺一不可。</summary>
        public IReadOnlyList<string> Archive(string experimentName)
        {
            Directory.CreateDirectory(ArchiveDir);
            var copied = new List<string>();
            foreach (var suffix in ArtifactSuffixes)
            {
                string source = FolderDir.Experiment.GetPathFile($"{experimentName}{suffix}");
                if (!File.Exists(source))
                {
                    if (suffix == "-trajectory.csv") continue;
                    throw Logging.ErrorOnce(
                        new FileNotFoundException($"Experiment artifact '{source}' is missing.", source),
                        "ROUND_ARCHIVE_FAILED", "本輪 archive 失敗", nameof(Archive), source, "required_artifact_missing");
                }

                string target = Path.Combine(ArchiveDir, Path.GetFileName(source));
                File.Copy(source, target, overwrite: false);
                copied.Add(target);
                Logging.Info($"[Archive] {target}");
            }
            return copied;
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
            // .sav.gz 這類雙副檔名會留下一層，再剝一次
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
