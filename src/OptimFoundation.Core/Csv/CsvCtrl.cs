using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OptimFoundation.Core
{
    public static class CsvCtrl
    {
        public static void ClearData(string fileName)
        {
            using var sw = new StreamWriter(FolderDir.Data.GetFilePath(fileName));
            sw.WriteLine("");
        }

        public static void CreateParamTable<T>()
        {
            string name = typeof(T).Name;
            FolderDir.Data.TryCreateFile($"{name}.csv");
            using var sw = new StreamWriter(FolderDir.Data.GetFilePath($"{name}.csv"));
            string cols = "DATA_ID," + string.Join(",", ReflectionHelper.GetMemberNames(typeof(T)).Select(s => s.ToUpper()));
            sw.WriteLine(cols);
        }

        public static List<int>      ReadIntSet(string fileName)    => ReadLines(FolderDir.Data.GetFilePath(fileName), int.Parse);
        public static List<double>   ReadDoubleSet(string fileName)  => ReadLines(FolderDir.Data.GetFilePath(fileName), double.Parse);
        public static List<string>   ReadStrSet(string fileName)     => ReadLines(FolderDir.Data.GetFilePath(fileName), s => s);
        public static List<DateTime> ReadDateSet(string fileName)    => ReadLines(FolderDir.Data.GetFilePath(fileName), DateTime.Parse);

        private static List<T> ReadLines<T>(string path, Func<string, T> parser)
        {
            var list = new List<T>();
            using var sr = new StreamReader(path);
            string line;
            while ((line = sr.ReadLine()) != null)
                list.Add(parser(line.Replace("\"", "")));
            return list;
        }

        public static Dictionary<string, double> ReadParameter(string fileName)
        {
            string path = FolderDir.Data.GetFilePath($"{fileName}.csv");
            var data = new Dictionary<string, double>();
            using var sr = new StreamReader(path);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                var parts = line.Split(',');
                if (parts.Length >= 2 && double.TryParse(parts.Last(), out double val))
                    data["@" + string.Join("@", parts.Take(parts.Length - 1))] = val;
            }
            return data;
        }

        public static List<T> BuildParameter<T>(string fileName = null)
        {
            Type type = typeof(T);
            string path = fileName == null
                ? FolderDir.Data.GetFilePath($"{type.Name}.csv")
                : FolderDir.Data.GetFilePath(fileName);

            var data = new List<T>();
            foreach (var kv in ReadParameter(path))
            {
                string combined = kv.Key + "@" + kv.Value;
                string[] parts = combined.Split('@').Skip(1).ToArray();
                data.Add((T)Activator.CreateInstance(type, new object[] { parts }));
            }
            return data;
        }

        /// <summary>
        /// 讀取矩陣格式 CSV
        /// </summary>
        public static double[,] ReadMatrixCsv(string fileName)
        {
            string path = FolderDir.Data.GetFilePath(fileName);
            var lines = File.ReadAllLines(path);
            int rows = lines.Length;
            int cols = lines[0].Split(',').Length;
            var matrix = new double[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                var parts = lines[r].Split(',');
                for (int c = 0; c < cols; c++)
                    matrix[r, c] = double.Parse(parts[c].Trim());
            }
            return matrix;
        }

        public static void SaveSolutionToCSV<T>(ISolverEngine engine, string dataId, string userId)
        {
            var classInfo = new ClassInfo(typeof(T));
            FolderDir.Solution.TryCreateFile($"{classInfo.TypeName}.csv");
            string file = FolderDir.Solution.GetFilePath($"{classInfo.TypeName}.csv");
            var sol = engine.GetSolution(classInfo.TypeName);

            using var sw = new StreamWriter(file);
            string cols = "DATA_ID,VAR_TYPE," + string.Join(",", classInfo.SetNames.Select(s => s.ToUpper())) + ",QTY,USER";
            sw.WriteLine(cols);

            foreach (var kv in sol)
            {
                string[] parts = kv.Key.Split('@');
                string row = dataId + "," + parts[0] + "," + string.Join(",", parts.Skip(1)) + "," + kv.Value + "," + userId;
                sw.WriteLine(row);
            }

            Logging.Info($"[CsvCtrl] Solution exported: {file}");
        }
    }
}
