using System;
using System.IO;

namespace OptimFoundation.Core
{
    public class FolderDir
    {
        public static ProjFolder Data = new ProjFolder("Data");
        public static ProjFolder Solution = new ProjFolder("Solution");
        public static ProjFolder Log = new ProjFolder("Logs");
        public static ProjFolder Model = new ProjFolder("Models");
        public static ProjFolder IIS = new ProjFolder("IISs");
        public static ProjFolder Sol = new ProjFolder("Sols");
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

        public class ProjFolder
        {
            /// <summary>執行檔所在目錄（AppDomain.BaseDirectory）</summary>
            public static string ProjectPath => System.AppDomain.CurrentDomain.BaseDirectory;

            private readonly string _folderName;

            public ProjFolder(string folderName)
            {
                _folderName = folderName;
            }


            /// <summary>
            /// 取得資料夾完整路徑。ProjectPath + folderName
            /// </summary>
            /// <returns></returns>
            public string GetPath() => Path.Combine(ProjectPath, _folderName);

            /// <summary>
            /// 建立資料夾。Directory.CreateDirectory 是 idempotent，目錄已存在時不 throw。
            /// </summary>
            public void CreateFolder()
            {
                Directory.CreateDirectory(GetPath());
            }

            /// <summary>
            /// 建立資料夾，若已存在則不 throw。
            /// </summary>
            /// <param name="fileName"></param>
            /// <returns></returns>
            public string GetFilePath(string fileName) => Path.Combine(GetPath(), fileName);

            public bool TryCreateFile(string fileName)
            {
                string path = GetFilePath(fileName);
                if (File.Exists(path)) return false;
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
                    catch (IOException) { }                 // 檔案使用中，跳過
                    catch (UnauthorizedAccessException) { } // 無權限，跳過
                }
                return deleted;
            }
        }
    }
}
