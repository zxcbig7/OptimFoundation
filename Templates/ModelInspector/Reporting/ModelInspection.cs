using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace ModelInspector.Reporting
{
    /// <summary>
    /// 一次檢視作業從框架挖出的全部資訊。純資料收集，不做任何輸出格式化。
    /// 每個欄位都對應一支框架的 public API；匯入模式下語意失真的欄位在
    /// <see cref="Caveats"/> 逐條列名，NEVER 把失真值當有效資料直接印出。
    /// </summary>
    public sealed class ModelInspection
    {
        /// <summary>來源模型檔絕對路徑。</summary>
        public string SourceFile { get; private set; } = "";

        /// <summary>來源檔位元組數。</summary>
        public long SourceBytes { get; private set; }

        /// <summary>來源檔最後寫入時間。</summary>
        public DateTime SourceModified { get; private set; }

        /// <summary>來源檔副檔名（小寫，含點）。</summary>
        public string SourceFormat { get; private set; } = "";

        /// <summary>本次執行的名稱前綴，同時是框架各輸出檔的檔名前綴。</summary>
        public string Label { get; private set; } = "";

        /// <summary>engine 的啟動時間戳，框架用它組所有輸出檔名。</summary>
        public string StartTime { get; private set; } = "";

        /// <summary>專案層設定（ProjectName / 匯出開關 / 保留天數）。</summary>
        public ProjectConfig ProjectConfig { get; private set; } = new ProjectConfig();

        /// <summary>solver 設定快照——只記真的有設的旋鈕，沒列到的就是用 CPLEX 預設。</summary>
        public ConfigSnapshot ConfigSnapshot { get; private set; } = new ConfigSnapshot();

        /// <summary>ImportModel 回填框架索引後的變數數。</summary>
        public int VariableCount { get; private set; }

        /// <summary>ImportModel 回填框架索引後的限制式數。</summary>
        public int ConstraintCount { get; private set; }

        /// <summary>經 Build*Vs 登記進 VariableSets 的變數數；匯入模式恆為 0。</summary>
        public int RegisteredVariableCount { get; private set; }

        /// <summary>各變數型別的預期 / 實際建立數；匯入模式為空。</summary>
        public IReadOnlyDictionary<string, (int Expected, int Actual)> VariableBuildCounts { get; private set; }
            = new Dictionary<string, (int, int)>();

        /// <summary>各限制式群組的預期 / 實際建立數；匯入模式為空。</summary>
        public IReadOnlyDictionary<string, (int Expected, int Actual)> ConstraintBuildCounts { get; private set; }
            = new Dictionary<string, (int, int)>();

        /// <summary>框架記錄的目標式方向；匯入模式不反映檔案內容（見 Caveats）。</summary>
        public ObjectiveSense ObjectiveSense { get; private set; }

        /// <summary>框架 pool 累積的目標式項數；匯入模式恆為 0。</summary>
        public int ObjectiveTermCount { get; private set; }

        /// <summary>已建立的軟性限制式條數；匯入模式恆為 0。</summary>
        public int SoftConstraintCount { get; private set; }

        /// <summary>soft penalty 併入目標式的項數；匯入模式恆為 0。</summary>
        public int SoftPenaltyTermCount { get; private set; }

        /// <summary>engine 是否支援收斂軌跡。</summary>
        public bool SupportsTrajectory { get; private set; }

        /// <summary>本次是否開啟了軌跡記錄。</summary>
        public bool TrajectoryRequested { get; private set; }

        /// <summary>求解狀態。</summary>
        public SolveStatus Status { get; private set; } = SolveStatus.NotSolved;

        /// <summary>OptProject 認定的成功（Optimal 或 Feasible）。</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>目標式解值；無解時為 NaN。</summary>
        public double ObjectiveValue { get; private set; } = double.NaN;

        /// <summary>最佳界；無解時為 NaN。</summary>
        public double BestBound { get; private set; } = double.NaN;

        /// <summary>相對 MIP gap；無解時為 NaN。</summary>
        public double MipGap { get; private set; } = double.NaN;

        /// <summary>Solve() 本身的統一 telemetry；求解丟例外時為 null。</summary>
        public SolveMetrics? Metrics { get; private set; }

        /// <summary>OptProject 量到的整趟耗時（含建模、匯出、housekeeping）。</summary>
        public TimeSpan TotalElapsed { get; private set; }

        /// <summary>OptProject 量到的模型套用耗時；匯入模式下即讀檔 + re-index 的時間。</summary>
        public TimeSpan BuildModelElapsed { get; private set; }

        /// <summary>二元變數解值（依名稱）。</summary>
        public IReadOnlyDictionary<string, double> BinaryValues { get; private set; }
            = new Dictionary<string, double>();

        /// <summary>整數變數解值（依名稱）。</summary>
        public IReadOnlyDictionary<string, double> IntegerValues { get; private set; }
            = new Dictionary<string, double>();

        /// <summary>連續變數解值（依名稱）。</summary>
        public IReadOnlyDictionary<string, double> ContinuousValues { get; private set; }
            = new Dictionary<string, double>();

        /// <summary>收斂軌跡取樣點。</summary>
        public IReadOnlyList<ConvergencePoint> Trajectory { get; private set; }
            = new List<ConvergencePoint>();

        /// <summary>RefineConflict 判定的衝突限制式名稱；非 Infeasible 時為空。</summary>
        public IReadOnlyList<string> ConflictConstraints { get; private set; } = new List<string>();

        /// <summary>框架自動產生的輸出檔（存在者才列入）。</summary>
        public IReadOnlyList<ArtifactFile> Artifacts { get; private set; } = new List<ArtifactFile>();

        /// <summary>匯入模式下失真或不可用的項目，逐條說明原因。</summary>
        public IReadOnlyList<string> Caveats { get; private set; } = new List<string>();

        /// <summary>求解過程丟出的例外訊息；正常結束時為 null。</summary>
        public string? Failure { get; private set; }

        /// <summary>全體變數解值總筆數。</summary>
        public int SolutionValueCount => BinaryValues.Count + IntegerValues.Count + ContinuousValues.Count;

        /// <summary>框架輸出檔的一筆記錄。</summary>
        public sealed record ArtifactFile(string Kind, string Path, long Bytes);

        /// <summary>
        /// 從一次已執行完的 <see cref="OptProject"/> 收集全部可得資訊。
        /// MUST 在 Execute() 之後呼叫（成功或失敗都可以——infeasible 的 IIS 只有這時候拿得到）。
        /// </summary>
        public static ModelInspection Capture(
            OptProject project, ProjectConfig projectConfig, InspectionOptions options, string? failure)
        {
            var inspection = new ModelInspection
            {
                SourceFile = options.ModelFile,
                Label = options.Label,
                ProjectConfig = projectConfig,
                TrajectoryRequested = options.CaptureTrajectory,
                Failure = failure,
                IsSuccess = project.IsSuccess,
                TotalElapsed = project.TotalElapsed,
                BuildModelElapsed = project.BuildModelElapsed,
            };

            var sourceInfo = new FileInfo(options.ModelFile);
            inspection.SourceBytes = sourceInfo.Length;
            inspection.SourceModified = sourceInfo.LastWriteTime;
            inspection.SourceFormat = sourceInfo.Extension.ToLowerInvariant();

            OptEngine? engine = project.Engine;
            if (engine == null) return inspection;

            inspection.StartTime = engine.StartTime;
            inspection.ConfigSnapshot = ConfigSnapshot.From(engine.Config);

            inspection.VariableCount = engine.VariableCount;
            inspection.ConstraintCount = engine.ConstraintCount;
            inspection.RegisteredVariableCount = engine.RegisteredVariableCount;
            inspection.VariableBuildCounts = engine.VariableBuildCounts;
            inspection.ConstraintBuildCounts = engine.ConstraintBuildCounts;
            inspection.ObjectiveSense = engine.ObjectiveSense;
            inspection.ObjectiveTermCount = engine.ObjectiveTermCount;
            inspection.SoftConstraintCount = engine.SoftConstraintCount;
            inspection.SoftPenaltyTermCount = engine.SoftPenaltyTermCount;
            inspection.SupportsTrajectory = engine.SupportsTrajectory;

            inspection.Status = engine.Status;
            inspection.Metrics = engine.LastMetrics;
            inspection.Trajectory = engine.Trajectory;

            if (project.IsSuccess)
            {
                inspection.ObjectiveValue = engine.GetObjectiveValue();
                inspection.BestBound = engine.BestObjValue;
                inspection.MipGap = engine.MIPGap;

                // 三支取解 API 都走 Variables 索引，ImportModel 的 re-index 已把它填好，故匯入模式照常可用。
                inspection.BinaryValues = engine.GetBVSolution();
                inspection.IntegerValues = engine.GetIVSolution();
                inspection.ContinuousValues = engine.GetCVSolution();
            }

            // Infeasible 時 Solve() 已跑過 RefineConflict 並快取，這裡取的是快取、不會重跑。
            inspection.ConflictConstraints = engine.GetConflictConstraints();

            inspection.Artifacts = CollectArtifacts(engine, projectConfig, inspection.ConflictConstraints.Count > 0);
            inspection.Caveats = BuildCaveats(inspection);
            return inspection;
        }

        // 框架的輸出檔名是「前綴 + 用途 + 啟動時間戳」的固定組合，engine 把兩個組件都開成 public，
        // 因此這裡用組路徑 + File.Exists 判定，比掃資料夾精準——不會撈到前幾次執行的殘留。
        private static List<ArtifactFile> CollectArtifacts(OptEngine engine, ProjectConfig config, bool hasConflict)
        {
            string name = engine.ModelName;
            string stamp = engine.StartTime;

            var candidates = new List<(string Kind, string Path)>();
            if (config.ExportLP)
                candidates.Add(("LP 模型檔", FolderDir.Model.GetFilePath($"{name}_LP_{stamp}.lp")));
            if (config.ExportMPS)
                candidates.Add(("MPS 模型檔", FolderDir.Model.GetFilePath($"{name}_MPS_{stamp}.mps")));
            if (config.ExportSol)
                candidates.Add(("解檔", FolderDir.Sol.GetFilePath($"{name}_Solution_{stamp}.sol")));
            if (hasConflict)
                candidates.Add(("IIS 衝突模型", FolderDir.IIS.GetFilePath($"{name}_IIS_{stamp}.ilp")));

            var found = new List<ArtifactFile>();
            foreach (var (kind, path) in candidates)
            {
                if (!File.Exists(path)) continue;
                found.Add(new ArtifactFile(kind, path, new FileInfo(path).Length));
            }
            return found;
        }

        private static List<string> BuildCaveats(ModelInspection inspection)
        {
            var caveats = new List<string>
            {
                "VariableSets 為空：匯入的模型沒有 C# 變數類別可對應，"
                    + "GetSetVarValues<T>() / GetSetVarNames<T>() / GetSolution(\"TypeName\") / CsvCtrl.WriteSolution<T>() 全部不可用，"
                    + "本報告一律改用名稱層級的 GetBVSolution / GetIVSolution / GetCVSolution。",
                $"RegisteredVariableCount = {inspection.RegisteredVariableCount}（恆為 0）：它量的是 VariableSets，"
                    + $"匯入模式請看 VariableCount = {inspection.VariableCount}。",
                $"ObjectiveSense 顯示 {inspection.ObjectiveSense}：這是 EngineBase 的預設值，"
                    + "ImportModel 不會依檔案內容更新它，真正的最佳化方向請看模型檔本身或匯出的 LP。",
                "ObjectiveTermCount / SoftConstraintCount / SoftPenaltyTermCount 恆為 0："
                    + "這三個量的是框架 pool 的累積，匯入的目標式直接來自檔案、沒有經過 pool。",
                "VariableBuildCounts / ConstraintBuildCounts 為空：Expected vs Actual 對帳只在 Build*Vs 建模路徑成立。",
            };

            if (inspection.SourceFormat is ".lp" or ".mps")
                caveats.Add($"來源是文字格式（{inspection.SourceFormat}），係數經十進位截斷；"
                    + "要精確重現原專案的求解結果請改用 .sav。");

            if (inspection.TrajectoryRequested)
                caveats.Add("已開啟收斂軌跡：掛 callback 會關閉 CPLEX dynamic search，"
                    + "本次的 RunTimeMs 不適合拿去跟未開軌跡的執行比較。加 --no-trajectory 可關掉。");

            return caveats;
        }
    }
}
