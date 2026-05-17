using System.IO;

namespace OptimFoundation.Core
{
    public class FolderDir
    {
        public class ProjFolder
        {
            private readonly string _folderName;

            public ProjFolder(string folderName)
            {
                _folderName = folderName;
                CreateFolder();
            }

            public string GetPath() => Path.Combine(ProjectPath, _folderName);

            public void CreateFolder()
            {
                if (!Directory.Exists(GetPath()))
                    Directory.CreateDirectory(GetPath());
            }

            public string GetFilePath(string fileName) => Path.Combine(GetPath(), fileName);

            public bool TryCreateFile(string fileName)
            {
                string path = GetFilePath(fileName);
                if (File.Exists(path)) return false;
                File.CreateText(path).Close();
                return true;
            }
        }

        public static string ProjectPath => System.AppDomain.CurrentDomain.BaseDirectory;

        public static ProjFolder Result = new ProjFolder("Results");
        public static ProjFolder Data   = new ProjFolder("Data");
        public static ProjFolder Log    = new ProjFolder("Logs");
        public static ProjFolder LP     = new ProjFolder("LPs");
        public static ProjFolder Model  = new ProjFolder("Models");
        public static ProjFolder IIS    = new ProjFolder("IISs");
        public static ProjFolder Sol    = new ProjFolder("Sols");

        public static string PathCombine(string folder, string fileName) => Path.Combine(folder, fileName);
        public static string PathCombine(params string[] paths) => Path.Combine(paths);

        public static void TryCreateFolder(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }
}
