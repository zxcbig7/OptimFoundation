using System;
using System.IO;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 框架固定的資料夾配置：全部掛在執行檔目錄下，一個屬性對應一個用途。
    /// 各資料夾在需要時才建立（寫入端自行呼叫 CreateFolder），不會在啟動時全部生出來。
    /// </summary>
    public class FolderDir
    {
        /// <summary>輸入資料（參數 CSV、set 檔）。唯一的「讀」資料夾，不會被保留期清理掃到。</summary>
        public static ProjFolder Data = new ProjFolder("Data");

        /// <summary>解輸出的 CSV（CsvSolutionSink 寫這裡）。</summary>
        public static ProjFolder Solution = new ProjFolder("Solution");

        /// <summary>框架與 solver 的 log 檔。</summary>
        public static ProjFolder Log = new ProjFolder("Logs");

        /// <summary>模型匯出檔（.lp / .mps）。</summary>
        public static ProjFolder Model = new ProjFolder("Models");

        /// <summary>infeasible 時的 conflict / IIS 分析結果。</summary>
        public static ProjFolder IIS = new ProjFolder("IISs");

        /// <summary>solver 的解檔（.sol）。</summary>
        public static ProjFolder Sol = new ProjFolder("Sols");

        /// <summary>實驗記錄（.csv / .json / -trajectory.csv）。</summary>
        public static ProjFolder Experiment = new ProjFolder("Experiments");

        /// <summary>框架產生的輸出資料夾（不含輸入用的 Data），供保留期清理逐一掃描。</summary>
        private static readonly ProjFolder[] _outputs = { Log, Model, Sol, IIS, Experiment, Solution };

        /// <summary>清除所有輸出資料夾中 LastWriteTime 超過 retentionDays 天的舊檔，回傳總刪除數。retentionDays &lt;= 0 時視為關閉、不清理。</summary>
        public static int PurgeOutputs(int retentionDays)
        {
            if (retentionDays <= 0) return 0;
            int total = 0;
            foreach (var folder in _outputs) total += folder.PurgeOlderThan(retentionDays);
            return total;
        }

        /// <summary>單一資料夾的路徑計算與檔案操作；不持有狀態，只記資料夾名。</summary>
        public class ProjFolder
        {
            /// <summary>執行檔所在目錄（AppDomain.BaseDirectory）</summary>
            public static string ProjectPath => System.AppDomain.CurrentDomain.BaseDirectory;

            private readonly string _folderName;

            /// <summary>指定資料夾名（相對於執行檔目錄）；此時不建立實體資料夾。</summary>
            public ProjFolder(string folderName)
            {
                _folderName = folderName;
            }


            /// <summary>取得資料夾完整路徑（ProjectPath + folderName）；不檢查是否存在。</summary>
            public string GetPath() => Path.Combine(ProjectPath, _folderName);

            /// <summary>
            /// 建立資料夾。Directory.CreateDirectory 是 idempotent，目錄已存在時不 throw。
            /// </summary>
            public void CreateFolder()
            {
                Directory.CreateDirectory(GetPath());
            }

            /// <summary>組出這個資料夾下某檔案的完整路徑；不建立資料夾也不建立檔案。</summary>
            public string GetFilePath(string fileName) => Path.Combine(GetPath(), fileName);

            /// <summary>建立空檔（連同資料夾）。檔案已存在時不覆寫、直接回 false。</summary>
            /// <returns>true = 這次真的建了新檔；false = 檔案本來就在。</returns>
            public bool TryCreateFile(string fileName)
            {
                string path = GetFilePath(fileName);
                if (File.Exists(path)) return false;
                Directory.CreateDirectory(GetPath());   // 確保資料夾存在（idempotent），否則 File.CreateText 丟 DirectoryNotFound
                File.CreateText(path).Close();
                return true;
            }

            /// <summary>刪除此資料夾中 LastWriteTime 早於 retentionDays 天前的檔案，回傳刪除數。資料夾不存在回 0；使用中或無權限的檔案跳過。</summary>
            public int PurgeOlderThan(int retentionDays)
            {
                string dir = GetPath();
                if (!Directory.Exists(dir)) return 0;

                DateTime cutoff = DateTime.Now.AddDays(-retentionDays);
                int deleted = 0;
                int skipped = 0;
                string firstFailure = null;
                foreach (var file in Directory.GetFiles(dir))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                        {
                            File.Delete(file);
                            deleted++;
                        }
                    }
                    catch (IOException ex)
                    {
                        skipped++;
                        firstFailure ??= $"{Path.GetFileName(file)}: {ex.Message}";
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        skipped++;
                        firstFailure ??= $"{Path.GetFileName(file)}: {ex.Message}";
                    }
                }
                if (skipped > 0)
                    Logging.Warn($"[OUTPUT_PURGE_SKIPPED] 部分舊檔未清除 | folder={_folderName} count={skipped} reason={firstFailure} result=kept");
                return deleted;
            }
        }
    }
}
