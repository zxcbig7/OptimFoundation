using System.IO;

namespace OptimFoundation.Core
{
    public class FolderDir
    {
        public static ProjFolder Data     = new ProjFolder("Data");
        public static ProjFolder Solution = new ProjFolder("Solution");
        public static ProjFolder Log      = new ProjFolder("Logs");
        public static ProjFolder Model    = new ProjFolder("Models");
        public static ProjFolder IIS      = new ProjFolder("IISs");
        public static ProjFolder Sol      = new ProjFolder("Sols");

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
            /// 建立資料夾。Directory.CreateDirectory 是 idempotent，目錄已存在時不 throw。
            /// </summary>
            public void CreateFolder() => Directory.CreateDirectory(GetPath());

            public string GetPath() => Path.Combine(ProjectPath, _folderName);

            public string GetFilePath(string fileName) => Path.Combine(GetPath(), fileName);

            public static string PathCombine(string folder, string fileName) => Path.Combine(folder, fileName);
            public static string PathCombine(params string[] paths) => Path.Combine(paths);

            public bool TryCreateFile(string fileName)
            {
                string path = GetFilePath(fileName);
                if (File.Exists(path)) return false;
                File.CreateText(path).Close();
                return true;
            }
        }

        public static void TryCreateFolder(string path)
        {
            Directory.CreateDirectory(path);
        }
    }
}
