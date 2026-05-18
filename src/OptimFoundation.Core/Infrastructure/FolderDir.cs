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

        public class ProjFolder
        {
            /// <summary>
            //   目前專案的根目錄，預設為執行檔所在的目錄
            /// </summary>
            public static string ProjectPath => System.AppDomain.CurrentDomain.BaseDirectory;

            /// <summary>
            /// 專案內的資料夾名稱，會自動在根目錄下建立對應的資料夾
            /// </summary>
            private readonly string _folderName;


            public ProjFolder(string folderName)
            {
                // 初始化資料夾名稱，並確保資料夾存在
                _folderName = folderName;
            }

            // 建立資料夾
            public void CreateFolder() {
                if (!Directory.Exists(GetPath())) Directory.CreateDirectory(GetPath());
            }

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
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

    }
}
