using System;
using System.IO;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// 管理求解、實驗及其輸出資源。
    /// 建立專案時會設定 log 檔、建立所需資料夾，並刪除超過保留天數的輸出檔；實驗紀錄不會刪除。
    /// 可多次 Solve；Engine / IsSuccess / Trial 保留最近一次結果，下次 Solve 時釋放前一次引擎。
    /// 多個專案需依序執行，因為 Logging 與 FolderDir 由整個 process 共用。
    /// </summary>
    public sealed class OptProject : IDisposable
    {
        /// <summary>建立專案。</summary>
        /// <param name="name">專案名：用作 log、實驗紀錄與 LP / MPS / Sol 輸出檔的檔名前綴。不可空白或含非法檔名字元。</param>
        /// <param name="retentionDays">輸出檔保留天數，建立專案時清掉更舊的 Log / Model / Solution / IIS / Output 檔；&lt;= 0 關閉清理。</param>
        public OptProject(string name, int retentionDays = 30)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw Logging.ErrorOnce(
                    new ArgumentException("Project name is required.", nameof(name)),
                    "PROJECT_INVALID", "專案設定不合法", nameof(OptProject), name, "name_is_empty");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw Logging.ErrorOnce(
                    new ArgumentException($"Project name '{name}' contains invalid file name characters.", nameof(name)),
                    "PROJECT_INVALID", "專案設定不合法", nameof(OptProject), name, "invalid_file_name_char");

            Name = name;
            RetentionDays = retentionDays;

            try
            {
                Initialize();
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "PROJECT_INIT_FAILED", "專案初始化失敗", nameof(OptProject), name,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        /// <summary>專案名。</summary>
        public string Name { get; }

        /// <summary>輸出檔保留天數；&lt;= 0 表示不清理。</summary>
        public int RetentionDays { get; } = 30;

        private void Initialize()
        {
            Logging.SetLogFileName(Name);
            FolderDir.CreateAll();
            int purged = FolderDir.PurgeAllOutputs(RetentionDays);
            Logging.Info($"[Project] Name={Name} RetentionDays={RetentionDays} Folders={FolderDir.ProjFolder.ProjectPath} Purged={purged}");
        }

        #region 正式求解

        private ProjectConfig _projectConfig = new ProjectConfig();

        /// <summary>載入正式求解的專案設定（solver log、LP / MPS / Sol 匯出），之後每次 <see cref="Solve"/> 都套用。預設 <c>new ProjectConfig()</c>；實驗另有自己的 LoadConfig。</summary>
        public OptProject LoadConfig(ProjectConfig config)
        {
            _projectConfig = config ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(config)),
                "PROJECT_INVALID", "專案設定不合法", nameof(LoadConfig), Name, "config_is_null");
            return this;
        }

        /// <summary>最近一次 Solve 的 engine，用來取解；建模或求解中途丟例外時仍可讀取。下一次 Solve 或 Dispose 時釋放。</summary>
        public OptEngine Engine { get; private set; }

        /// <summary>最近一次 Solve 是否找到可用解（Optimal 或 Feasible）。</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>最近一次 Solve 的設定副本與求解統計，格式與實驗的 Trial 相同；預設不記錄收斂過程。</summary>
        public Trial Trial { get; private set; }

        /// <summary>最近一次 Solve 的總耗時（CPLEX 時鐘）：從建好 CPLEX 模型起，包含建模、求解、儲存紀錄與執行 onSolved。</summary>
        public TimeSpan TotalElapsed { get; private set; }

        /// <summary>最近一次 Solve 的建模耗時（CPLEX 時鐘）；使用模型檔時，包含讀檔與建立變數、限制式查找索引的時間。</summary>
        public TimeSpan BuildModelElapsed => Engine?.ModelApplyElapsed ?? TimeSpan.Zero;

        /// <summary>
        /// 使用指定的模型與求解器設定，建立新引擎並求解一次。
        /// 結果以 Trial 寫成 Experiment/{專案名}-solve-trial.csv 與 -meta.csv，每次 Solve 覆寫；不寫 -summary.csv。
        /// 找到可用解後才執行 onSolved。
        /// </summary>
        /// <param name="model">要求解的模型。</param>
        /// <param name="config">求解器設定；執行時複製一份，之後修改原設定不會影響這次求解。</param>
        /// <param name="onSolved">只在成功後執行，通常用來讀解、驗證、寫出。</param>
        /// <param name="beforeSolve">建模完成後、求解前執行，例如開啟收斂軌跡。</param>
        public bool Solve(OptModel model, CplexConfig config,
            Action<OptEngine> onSolved = null, Action<OptEngine> beforeSolve = null)
        {
            if (model == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(model)),
                    "SOLVE_INVALID", "正式求解設定不合法", nameof(Solve), Name, "model_is_null");
            if (config == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(config)),
                    "SOLVE_INVALID", "正式求解設定不合法", nameof(Solve), model.Name, "config_is_null");

            try
            {
                return SolveCore(model, config, onSolved, beforeSolve);
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "SOLVE_EXECUTION_FAILED", "公開 API 執行失敗", nameof(Solve), model.Name,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        private bool SolveCore(OptModel model, CplexConfig config,
            Action<OptEngine> onSolved, Action<OptEngine> beforeSolve)
        {
            IsSuccess = false;
            Trial = null;
            // 實驗可能切換 log，求解前須切回專案 log。
            Logging.SetLogFileName(Name);

            string runId = NextRunId();
            Engine?.Dispose();
            // 先保存 Engine，失敗時仍可讀取診斷資訊。
            Engine = new OptEngine(config.Clone(), _projectConfig.Clone());
            Engine.SetModelName(Name);
            Trial = Engine.RunModel(model, "solve", captureTrajectory: false, beforeSolve, out bool solved);
            Trial.ExperimentId = runId;
            Trial.TrialId = 1;
            IsSuccess = solved;

            // 先存紀錄，避免 onSolved 失敗時遺失求解結果。
            var record = new Experiment(Name, SolveExperimentName, $"正式求解紀錄：{model.Name}") { WriteSummary = false };
            record.AddTrial(Trial);
            record.Save();

            if (IsSuccess) onSolved?.Invoke(Engine);

            TotalElapsed = Engine.ElapsedSinceRunStart;
            return IsSuccess;
        }

        /// <summary>釋放最近一次 Solve 的 engine（CPLEX native 資源）。</summary>
        public void Dispose() => Engine?.Dispose();

        #endregion

        /// <summary>正式求解紀錄的實驗名：檔名為 {專案名}-solve-trial.csv 等。</summary>
        public const string SolveExperimentName = "solve";

        /// <summary>
        /// 建立實驗，讓每個模型分別搭配每組求解器設定，並以 Trial 保存每次結果。
        /// 建立時就把 log 切到 {專案名}-{實驗名}_exp，之後的前置動作（例：warm-up）也收在同一檔。
        /// </summary>
        /// <param name="name">實驗名，輸出檔為 {專案名}-{實驗名}-trial.csv 等（<see cref="SolveExperimentName"/> 留給正式求解）；同名實驗再跑一次整組覆寫。</param>
        /// <param name="description">實驗目的，寫進 -meta.csv。</param>
        public OptExperiment Experiment(string name, string description = null)
            => new OptExperiment(this, name, description);

        #region 批次識別

        private static readonly object RunIdLock = new object();
        private static string _lastRunStamp;
        private static int _runStampRepeat;

        /// <summary>
        /// 批次識別使用 yyyyMMdd-HHmmss；同秒重複時加流水號。
        /// </summary>
        internal static string NextRunId()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            lock (RunIdLock)
            {
                _runStampRepeat = stamp == _lastRunStamp ? _runStampRepeat + 1 : 1;
                _lastRunStamp = stamp;
                return _runStampRepeat == 1 ? stamp : $"{stamp}-{_runStampRepeat}";
            }
        }

        #endregion
    }
}
