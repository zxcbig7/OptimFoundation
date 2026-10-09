using System;
using System.IO;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 執行檔目錄下的框架資料夾；OptProject 與寫入端會自動建立。
    /// </summary>
    public class FolderDir
    {
        /// <summary>輸入資料（參數 CSV、set 檔）。唯一的「讀」資料夾，不會被保留期清理掃到。</summary>
        public static ProjFolder Input = new ProjFolder("Input");

        /// <summary>解輸出的 CSV（CsvSolutionSink 寫這裡）。</summary>
        public static ProjFolder Output = new ProjFolder("Output");

        /// <summary>框架與 solver 的 log 檔。</summary>
        public static ProjFolder Log = new ProjFolder("Log");

        /// <summary>模型匯出檔（.lp / .mps）。</summary>
        public static ProjFolder Model = new ProjFolder("Model");

        /// <summary>infeasible 時的 conflict / IIS 分析結果。</summary>
        public static ProjFolder IIS = new ProjFolder("IIS");

        /// <summary>solver 的解檔（.sol）。</summary>
        public static ProjFolder Solution = new ProjFolder("Solution");

        /// <summary>實驗與正式環境紀錄：每個實驗四個檔 {專案}-{實驗}-trial.csv / -meta.csv / -summary.csv / -trajectory.csv。</summary>
        public static ProjFolder Experiment = new ProjFolder("Experiment");

        /// <summary>全部資料夾，供 <see cref="CreateAll"/> 一次建立。</summary>
        private static readonly ProjFolder[] _all = { Input, Output, Log, Model, IIS, Solution, Experiment };

        /// <summary>
        /// 依保留期清理的資料夾。不含 Input 與 Experiment，保留輸入資料與調參紀錄。
        /// </summary>
        private static readonly ProjFolder[] _outputs = { Log, Model, Solution, IIS, Output };

        /// <summary>建立全部資料夾（Input / Output / Log / Model / IIS / Solution / Experiment），已存在的不動。</summary>
        public static void CreateAll()
        {
            foreach (var folder in _all) folder.CreateFolder();
        }

        /// <summary>清除輸出資料夾（不含 Input 與 Experiment）中 LastWriteTime 超過 retentionDays 天的舊檔，回傳總刪除數。retentionDays &lt;= 0 時視為關閉、不清理。</summary>
        public static int PurgeAllOutputs(int retentionDays)
        {
            if (retentionDays <= 0) return 0;
            int total = 0;
            foreach (var folder in _outputs) total += folder.PurgeOlderThan(retentionDays);
            return total;
        }

        /// <summary>保存一個資料夾名稱，提供完整路徑計算、建立與舊檔清理方法。</summary>
        public class ProjFolder
        {
            /// <summary>應用程式的基底目錄；所有框架資料夾都放在此目錄下。</summary>
            public static string ProjectPath => System.AppDomain.CurrentDomain.BaseDirectory;

            private readonly string _folderName;

            /// <summary>指定資料夾名（相對於執行檔目錄）；此時不建立實體資料夾。</summary>
            public ProjFolder(string folderName)
            {
                _folderName = folderName;
            }

            /// <summary>取得資料夾完整路徑（ProjectPath + folderName）；不檢查是否存在。</summary>
            public string GetPath() => Path.Combine(ProjectPath, _folderName);

            /// <summary>組出這個資料夾下某檔案的完整路徑；不建立資料夾也不建立檔案。</summary>
            public string GetPathFile(string fileName) => Path.Combine(GetPath(), fileName);

            /// <summary>
            /// 建立資料夾，保留既有內容。
            /// </summary>
            public void CreateFolder()
            {
                Directory.CreateDirectory(GetPath());
            }

            /// <summary>建立空檔（連同資料夾）。檔案已存在時不覆寫、直接回 false。</summary>
            /// <returns>true = 這次真的建了新檔；false = 檔案本來就在。</returns>
            public bool TryCreateFile(string fileName)
            {
                string path = GetPathFile(fileName);
                if (File.Exists(path)) return false;
                Directory.CreateDirectory(GetPath());   // 先確保資料夾存在，避免 File.CreateText 因找不到目錄而失敗。
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
                    Logging.Warn($"[舊檔清除略過] 部分舊檔未清除 | 資料夾={_folderName} 數量={skipped} 原因={firstFailure} 結果=繼續");
                return deleted;
            }
        }
    }
}
