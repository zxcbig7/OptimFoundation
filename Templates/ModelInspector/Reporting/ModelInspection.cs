using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace ModelInspector.Reporting
{
    /// <summary>
    /// 收集一次模型檢視的來源檔、求解結果與輸出檔資訊；顯示格式由 ReportWriter 處理。
    /// 資料透過框架的 public API 取得；匯入模型時，某些欄位無法反映模型的實際內容，原因記在
    /// <see cref="Caveats"/> 中，報告需一併顯示這些限制，避免讀者誤解數值。
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

        /// <summary>專案名（OptProject.Name），用作 log 與輸出檔名的前綴。</summary>
        public string ProjectName { get; private set; } = "";

        /// <summary>專案保留天數（OptProject.RetentionDays）；&lt;= 0 表示不清理。</summary>
        public int RetentionDays { get; private set; }

        /// <summary>本次求解的專案設定（solver log / LP / MPS / Sol 匯出）。</summary>
        public ProjectConfig ProjectConfig { get; private set; } = new ProjectConfig();

        /// <summary>本次指定的 solver 參數值；未列出的參數使用 CPLEX 預設值。</summary>
        public ConfigSnapshot ConfigSnapshot { get; private set; } = new ConfigSnapshot();

        /// <summary>ReadModel 讀取模型後，記錄到框架名稱索引中的變數總數。</summary>
        public int VariableCount { get; private set; }

        /// <summary>ReadModel 讀取模型後，記錄到框架名稱索引中的限制式總數。</summary>
        public int ConstraintCount { get; private set; }

        /// <summary>各變數型別的預期 / 實際建立數；匯入模式為空。</summary>
        public IReadOnlyDictionary<string, (int Expected, int Actual)> VariableBuildCounts { get; private set; }
            = new Dictionary<string, (int, int)>();

        /// <summary>各限制式群組的預期 / 實際建立數；匯入模式為空。</summary>
        public IReadOnlyDictionary<string, (int Expected, int Actual)> ConstraintBuildCounts { get; private set; }
            = new Dictionary<string, (int, int)>();

        /// <summary>框架記錄的目標式方向；匯入模式依檔案內容同步（.mps 的 maximize 會讀成反號的 minimize，見 Caveats）。</summary>
        public ObjectiveSense ObjectiveSense { get; private set; }

        /// <summary>透過框架暫存算式建立的目標式項數；匯入模型不經過這個步驟，因此為 0。</summary>
        public int ObjectiveTermCount { get; private set; }

        /// <summary>已建立的軟性限制式條數；匯入模式恆為 0。</summary>
        public int SoftConstraintCount { get; private set; }

        /// <summary>加入目標式的軟性限制違反懲罰項數；匯入模式恆為 0。</summary>
        public int SoftPenaltyTermCount { get; private set; }

        /// <summary>engine 是否支援收斂軌跡。</summary>
        public bool SupportsTrajectory { get; private set; }

        /// <summary>本次是否開啟了軌跡記錄。</summary>
        public bool TrajectoryRequested { get; private set; }

        /// <summary>求解狀態。</summary>
        public SolveStatus Status { get; private set; } = SolveStatus.NotSolved;

        /// <summary>OptProject.Solve 認定的成功（Optimal 或 Feasible）。</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>目標式解值；無解時為 NaN。</summary>
        public double ObjectiveValue { get; private set; } = double.NaN;

        /// <summary>最佳界；無解時為 NaN。</summary>
        public double BestBound { get; private set; } = double.NaN;

        /// <summary>求解結束時實際達到的相對 MIP gap（不是 --mipgap 停止門檻）；無解時為 NaN。</summary>
        public double Gap { get; private set; } = double.NaN;

        /// <summary>Solve() 記錄的求解狀態、耗時、目標值等指標；求解丟出例外時為 null。</summary>
        public SolveMetrics? Metrics { get; private set; }

        /// <summary>OptProject.Solve 的總耗時，包含建模、求解、匯出與寫入紀錄檔。</summary>
        public TimeSpan TotalElapsed { get; private set; }

        /// <summary>OptProject.Solve 準備模型的耗時；匯入模式下包含讀檔及建立變數、限制式名稱索引。</summary>
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

        /// <summary>列出匯入模型後無法使用或不能直接解讀的統計項目，並說明原因。</summary>
        public IReadOnlyList<string> Caveats { get; private set; } = new List<string>();

        /// <summary>求解過程丟出的例外訊息；正常結束時為 null。</summary>
        public string? Failure { get; private set; }

        /// <summary>全體變數解值總筆數。</summary>
        public int SolutionValueCount => BinaryValues.Count + IntegerValues.Count + ContinuousValues.Count;

        /// <summary>框架輸出檔的一筆記錄。</summary>
        public sealed record ArtifactFile(string Kind, string Path, long Bytes);

        /// <summary>
        /// 從一次已執行完的 <see cref="OptProject.Solve"/> 收集全部可得資訊。
        /// 必須在 Solve() 結束後呼叫，成功或失敗都可以；無可行解時的衝突分析結果也要等求解後才能取得。
        /// </summary>
        public static ModelInspection Capture(
            OptProject project, ProjectConfig projectConfig, InspectionOptions options, string? failure)
        {
            var inspection = new ModelInspection
            {
                SourceFile = options.ModelFile,
                Label = options.Label,
                ProjectName = project.Name,
                RetentionDays = project.RetentionDays,
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
                inspection.Gap = engine.MIPGap;

                // ReadModel 已建立 Variables 名稱索引，因此這三個 API 也能讀取匯入模型的變數解值。
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

        // 輸出檔名由「模型名稱 + 用途 + 啟動時間」組成，名稱與時間可從 engine 取得，
        // 所以直接組出本次檔案的路徑並用 File.Exists 檢查，避免把以前執行留下的檔案算進來。
        private static List<ArtifactFile> CollectArtifacts(OptEngine engine, ProjectConfig config, bool hasConflict)
        {
            string name = engine.ModelName;
            string stamp = engine.StartTime;

            var candidates = new List<(string Kind, string Path)>();
            if (config.ExportLP)
                candidates.Add(("LP 模型檔", FolderDir.Model.GetPathFile($"{name}_LP_{stamp}.lp")));
            if (config.ExportMPS)
                candidates.Add(("MPS 模型檔", FolderDir.Model.GetPathFile($"{name}_MPS_{stamp}.mps")));
            if (config.ExportSol)
                candidates.Add(("解檔", FolderDir.Solution.GetPathFile($"{name}_Solution_{stamp}.sol")));
            if (hasConflict)
                candidates.Add(("IIS 衝突模型", FolderDir.IIS.GetPathFile($"{name}_IIS_{stamp}.ilp")));

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
                "型別化取解（GetSetVarValues / GetSetVarNames / GetSolution(\"TypeName\") / CsvCtrl.WriteSolution<T>()）以型別名篩選變數池，"
                    + "檔案裡的變數名不符 TypeName@… 時回空；本報告一律改用不看名稱的 GetBVSolution / GetIVSolution / GetCVSolution。",
                $"ObjectiveSense = {inspection.ObjectiveSense}：ReadModel 依檔案內容同步。"
                    + "注意 .mps 沒有方向欄位，CPLEX 把 maximize 寫成係數取負的 minimize，讀回來的目標值會反號。",
                "ObjectiveTermCount / SoftConstraintCount / SoftPenaltyTermCount 恆為 0："
                    + "這三個量的是框架 pool 的累積，匯入的目標式直接來自檔案、沒有經過 pool。",
                "VariableBuildCounts / ConstraintBuildCounts 為空：Expected vs Actual 對帳只在 Build*Vs 建模路徑成立。",
            };

            if (inspection.SourceFormat is ".lp" or ".mps")
                caveats.Add($"來源是文字格式（{inspection.SourceFormat}），係數經十進位截斷；"
                    + "要精確重現原專案的求解結果請改用 .sav。");

            if (inspection.TrajectoryRequested)
                caveats.Add("已開啟收斂軌跡：掛 callback 會關閉 CPLEX dynamic search，"
                    + "本次的 SolveTimeMs 不適合拿去跟未開軌跡的執行比較。加 --no-trajectory 可關掉。");

            return caveats;
        }
    }
}
