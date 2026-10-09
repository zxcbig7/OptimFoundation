using System;
using System.IO;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>管理求解、實驗與輸出檔。Logging 與 FolderDir 為 process 共用，多個專案請依序執行。</summary>
    public sealed class OptProject : IDisposable
    {
        /// <summary>建立專案。</summary>
        /// <param name="name">專案名：用作 log、實驗紀錄與 LP / MPS / Sol 輸出檔的檔名前綴。不可空白或含非法檔名字元。</param>
        /// <param name="retentionDays">輸出檔保留天數，建立專案時清掉更舊的 Log / Model / Solution / IIS / Output 檔；&lt;= 0 關閉清理。</param>
        public OptProject(string name, int retentionDays = 30)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw Logging.ErrorOnce(
                    new ArgumentException("專案名稱不得為空白", nameof(name)),
                    "專案設定不合法", null, nameof(OptProject), name, "名稱為空");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw Logging.ErrorOnce(
                    new ArgumentException($"專案名稱含不合法的檔名字元：{name}", nameof(name)),
                    "專案設定不合法", null, nameof(OptProject), name, "檔名含不合法字元");

            Name = name;
            RetentionDays = retentionDays;

            try
            {
                // 初始化 Logging 與 FolderDir，之後每次 Production 或 Experiment 都會使用同一個專案資料夾。
                Logging.SetLogFileName(Name);
                FolderDir.CreateAll();
                int purged = FolderDir.PurgeAllOutputs(RetentionDays);
                Logging.Info($"[專案初始化完成] 名稱={Name} 保留天數={RetentionDays} 資料夾={FolderDir.ProjFolder.ProjectPath} 清除檔案數量={purged}");
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "專案初始化失敗", null, nameof(OptProject), name,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        /// <summary>專案名。</summary>
        public string Name { get; }

        /// <summary>輸出檔保留天數；&lt;= 0 表示不清理。</summary>
        public int RetentionDays { get; } = 30;

        #region 正式環境結果

        // 跨多次 Production 共用；OptProduction 執行時寫入，專案只負責對外提供。
        private readonly ProductionResult _production = new ProductionResult();

        /// <summary>最近一次 Production 的 engine，用來取解；建模或求解中途丟例外時仍可讀取。下一次 Production 或 Dispose 時釋放。</summary>
        public OptEngine Engine => _production.Engine;

        /// <summary>最近一次 Production 是否找到可用解（Optimal 或 Feasible）。</summary>
        public bool IsSuccess => _production.IsSuccess;

        /// <summary>最近一次 Production 的設定副本與求解統計，格式與實驗的 Trial 相同；預設不記錄收斂過程。</summary>
        public Trial Trial => _production.Trial;

        /// <summary>最近一次 Production 的總耗時（CPLEX 時鐘）：從建好 CPLEX 模型起，包含建模、求解、執行 OnSolved 與儲存紀錄。</summary>
        public TimeSpan TotalElapsed => _production.TotalElapsed;

        /// <summary>最近一次 Production 的建模耗時（CPLEX 時鐘）；使用模型檔時，包含讀檔與建立變數、限制式查找索引的時間。</summary>
        public TimeSpan BuildModelElapsed => Engine?.ModelApplyElapsed ?? TimeSpan.Zero;

        /// <summary>釋放最近一次 Production 的 engine（CPLEX native 資源）。</summary>
        public void Dispose() => Engine?.Dispose();

        #endregion

        /// <summary>正式環境紀錄的實驗名：檔名為 {專案名}-production-trial.csv 等。</summary>
        public const string ProductionExperimentName = "production";

        /// <summary>建立正式求解流程：只允許一組模型與設定，保留 engine 供取解，不記錄收斂軌跡。</summary>
        /// <param name="description">寫進 -meta.csv 的說明；省略時寫「正式環境紀錄：{模型名}」。</param>
        public OptProduction Production(string description = null)
            => new OptProduction(Name, ProductionExperimentName, description, _production);

        /// <summary>建立實驗，讓每個模型搭配每組求解器設定，並保存每次結果。</summary>
        /// <param name="name">實驗名，輸出檔為 {專案名}-{實驗名}-trial.csv 等（<see cref="ProductionExperimentName"/> 留給正式環境）；同名實驗再跑一次整組覆寫。</param>
        /// <param name="description">實驗目的，寫進 -meta.csv。</param>
        public OptExperiment Experiment(string name, string description = null)
            => new OptExperiment(Name, name, description);
    }
}
