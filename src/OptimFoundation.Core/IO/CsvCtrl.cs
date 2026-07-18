using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OptimFoundation.Core.IO
{
    public static class CsvCtrl
    {
        // CSV 給人 / Excel 開啟：UTF-8 with BOM，避免 zh-TW Excel 以 Big5(950) 誤判中文成亂碼
        private static readonly Encoding _csvWrite = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        // CSV 讀入：固定 UTF-8（StreamReader/ReadAllLines 會自動偵測並去除 BOM），來源檔請一律存成 UTF-8
        private static readonly Encoding _csvRead = Encoding.UTF8;

        // 檔名慣例統一：帶不帶 .csv 皆可（統一在入口補齊，呼叫端不必記哪個 API 要帶副檔名）
        private static string EnsureCsv(string fileName)
            => fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".csv";

        // 統一的行切割：去引號後以逗號切（框架資料值不含逗號；不做完整 CSV 引號跳脫）
        private static string[] SplitLine(string line) => line.Replace("\"", "").Split(',');

        public static void ClearData(string fileName)
        {
            using var sw = new StreamWriter(FolderDir.Data.GetFilePath(EnsureCsv(fileName)), append: false, _csvWrite);
            sw.WriteLine("");
        }

        public static void CreateParamTable<TParameter>()
        {
            string name = typeof(TParameter).Name;
            FolderDir.Data.TryCreateFile($"{name}.csv");
            using var sw = new StreamWriter(FolderDir.Data.GetFilePath($"{name}.csv"), append: false, _csvWrite);
            string cols = "DATA_ID," + string.Join(",", ReflectionHelper.GetMemberNames(typeof(TParameter)).Select(s => s.ToUpper()));
            sw.WriteLine(cols);
        }

        public static List<int> ReadIntSet(string fileName) => ReadLines(FolderDir.Data.GetFilePath(EnsureCsv(fileName)), int.Parse);
        public static List<double> ReadDoubleSet(string fileName) => ReadLines(FolderDir.Data.GetFilePath(EnsureCsv(fileName)), double.Parse);
        public static List<string> ReadStrSet(string fileName) => ReadLines(FolderDir.Data.GetFilePath(EnsureCsv(fileName)), s => s);
        public static List<DateTime> ReadDateSet(string fileName) => ReadLines(FolderDir.Data.GetFilePath(EnsureCsv(fileName)), DateTime.Parse);

        private static List<TValue> ReadLines<TValue>(string path, Func<string, TValue> parser)
        {
            var list = new List<TValue>();
            using var sr = new StreamReader(path, _csvRead);
            string line;
            while ((line = sr.ReadLine()) != null)
                list.Add(parser(line.Replace("\"", "")));
            return list;
        }

        /// <summary>
        /// 讀整張 CSV 為 DataTable（供 set/param 以外的通用用途）：第一列 = 欄名，其餘 = 資料列，全欄型別 string。
        /// 與 typed 的 BuildParameter 不同——不對 class 對位、不轉型，回原始表格。fileName 帶不帶 .csv 皆可。
        /// </summary>
        public static DataTable ReadTable(string fileName)
        {
            string path = FolderDir.Data.GetFilePath(EnsureCsv(fileName));
            var table = new DataTable();
            var lines = File.ReadAllLines(path, _csvRead).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            if (lines.Length == 0) return table;

            foreach (var col in SplitLine(lines[0]))
                table.Columns.Add(col.Trim());

            foreach (var line in lines.Skip(1))
            {
                var parts = SplitLine(line);
                var row = table.NewRow();
                for (int i = 0; i < table.Columns.Count && i < parts.Length; i++)
                    row[i] = parts[i].Trim();
                table.Rows.Add(row);
            }
            return table;
        }

        /// <summary>
        /// 讀「key 欄在前、值在最後一欄」的參數檔為字典：key = "@k1@k2@…"、value = 最後一欄。
        /// 最後一欄 parse 不成數字的行（如表頭）自動跳過；無法忽略多餘欄，欄位對名請改用 BuildParameter。
        /// </summary>
        public static Dictionary<string, double> ReadParameter(string fileName)
        {
            string path = FolderDir.Data.GetFilePath(EnsureCsv(fileName));
            var data = new Dictionary<string, double>();
            using var sr = new StreamReader(path, _csvRead);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                var parts = SplitLine(line);
                if (parts.Length >= 2 && double.TryParse(parts.Last(), NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    data["@" + string.Join("@", parts.Take(parts.Length - 1))] = val;
            }
            return data;
        }

        /// <summary>
        /// 從 CSV 讀取 Parameter 列表（canonical schema：set 欄 + QTY，建議帶表頭）。
        /// 有表頭 → 依「欄名 = property 名」（大小寫不敏感）對位，多餘欄（DATA_ID / VAR_TYPE / USER …）自動忽略，
        /// 表頭缺任何 property 欄即丟例外——按名對位徹底解掉欄序錯位問題，框架自己輸出的檔（CreateParamTable /
        /// WriteSolution）皆可直接讀回（round-trip）。
        /// 無表頭 → legacy 按 property 宣告順序對位（set 欄在前、QTY 最後）。
        /// fileName 帶不帶 .csv 皆可；省略時用 {型別名}.csv（呼叫端可就地指定檔名覆寫慣例）。
        /// TParameter 必須繼承 ModelElementBase 並有無參建構子（properties-only 類別符合此要求）。
        /// </summary>
        public static List<TParameter> BuildParameter<TParameter>(string fileName = null) where TParameter : ModelElementBase, new()
        {
            Type type = typeof(TParameter);
            string path = FolderDir.Data.GetFilePath(EnsureCsv(fileName ?? type.Name));

            // 與 InitClassBySets 相同的順序來源（property 宣告順序）
            var props = type.GetProperties();
            var data = new List<TParameter>();

            var lines = File.ReadAllLines(path, _csvRead).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            if (lines.Length == 0) return data;

            // 表頭偵測：第一行最後一欄 parse 不成數字 → 視為表頭（參數/解檔最後的資料欄必為數值或 USER 字串欄）
            var firstParts = SplitLine(lines[0]);
            bool hasHeader = !double.TryParse(firstParts.Last(), NumberStyles.Any, CultureInfo.InvariantCulture, out _);

            // colMap[i] = props[i] 的值在哪一欄
            int[] colMap;
            if (hasHeader)
            {
                var header = firstParts.Select(h => h.Trim().ToUpperInvariant()).ToArray();
                colMap = props.Select(p =>
                {
                    int idx = Array.IndexOf(header, p.Name.ToUpperInvariant());
                    if (idx < 0)
                        throw new InvalidDataException(
                            $"[CsvCtrl] {Path.GetFileName(path)} 表頭缺少欄位 '{p.Name}'。{type.Name} 需要：{string.Join(", ", props.Select(x => x.Name))}；檔內表頭：{string.Join(", ", header)}");
                    return idx;
                }).ToArray();
            }
            else
            {
                colMap = Enumerable.Range(0, props.Length).ToArray();
            }

            foreach (var line in lines.Skip(hasHeader ? 1 : 0))
            {
                var parts = SplitLine(line);
                var values = new object[props.Length];
                for (int i = 0; i < props.Length; i++)
                {
                    if (colMap[i] >= parts.Length)
                        throw new InvalidDataException($"[CsvCtrl] {Path.GetFileName(path)} 資料列欄數不足（需要至少 {colMap[i] + 1} 欄）：'{line}'");
                    values[i] = parts[colMap[i]].Trim();
                }
                var instance = new TParameter();
                instance.InitClassBySets(values);   // string 值由 InitClassBySets 依 property 型別轉換
                data.Add(instance);
            }
            return data;
        }

        /// <summary>讀取矩陣格式 CSV（整份皆數字、無表頭、每列欄數一致）</summary>
        public static double[,] ReadMatrixCsv(string fileName)
        {
            string path = FolderDir.Data.GetFilePath(EnsureCsv(fileName));
            var lines = File.ReadAllLines(path, _csvRead);
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

        /// <summary>
        /// 把某變數型別的解值匯出到 Solution/{型別名}.csv（表頭：VAR_TYPE,set…,QTY）。
        /// 表頭欄名 = property 名，故此檔可直接被 BuildParameter 讀回（按名對位、多餘欄自動忽略）。
        /// dataId / userId 僅供 DB sink 用；CSV 不輸出這兩欄。
        /// </summary>
        public static void WriteSolution<TVariable>(ISolverEngine engine, string dataId, string userId)
        {
            var classInfo = new ClassInfo(typeof(TVariable));
            FolderDir.Solution.TryCreateFile($"{classInfo.TypeName}.csv");
            string file = FolderDir.Solution.GetFilePath($"{classInfo.TypeName}.csv");
            var sol = engine.GetSolution(classInfo.TypeName);

            using var sw = new StreamWriter(file, append: false, _csvWrite);
            string cols = "VAR_TYPE," + string.Join(",", classInfo.SetNames.Select(s => s.ToUpper())) + ",QTY";
            sw.WriteLine(cols);

            foreach (var kv in sol)
            {
                string[] parts = kv.Key.Split('@');
                // 數值用 InvariantCulture round-trip 格式，讀回不失真
                string row = parts[0] + "," + string.Join(",", parts.Skip(1)) + "," + kv.Value.ToString("R", CultureInfo.InvariantCulture);
                sw.WriteLine(row);
            }

            Logging.Info($"[CsvCtrl] Solution exported: {file}");
        }
    }
}
