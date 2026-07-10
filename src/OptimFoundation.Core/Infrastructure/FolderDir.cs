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
        }
    }
}
