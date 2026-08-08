using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// CSV 讀寫的單一入口：輸入一律從 Data/ 讀、解輸出寫到 Solution/。
    /// 讀寫都走同一套 RFC4180 解析（逗號分隔、雙引號包住可含逗號、"" 跳脫），不支援欄位內換行。
    /// 檔名帶不帶 .csv 皆可，入口會自動補齊。
    /// </summary>
    public static class CsvCtrl
    {
        // CSV 給人 / Excel 開啟：UTF-8 with BOM，避免 zh-TW Excel 以 Big5(950) 誤判中文成亂碼
        private static readonly Encoding _csvWrite = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        // CSV 讀入：固定 UTF-8（StreamReader/ReadAllLines 會自動偵測並去除 BOM），來源檔請一律存成 UTF-8
        private static readonly Encoding _csvRead = Encoding.UTF8;

        // 檔名慣例統一：帶不帶 .csv 皆可（統一在入口補齊，呼叫端不必記哪個 API 要帶副檔名）
        private static string EnsureCsv(string fileName)
            => fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".csv";

        /// <summary>
        /// Parses RFC4180 records from a reader. Newlines inside quoted fields are
        /// kept as <c>\n</c>, escaped quotes are decoded, and each yielded value is
        /// one complete logical row rather than one physical line.
        /// </summary>
        internal static IEnumerable<string[]> ParseCsv(TextReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var fields = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            var recordStarted = false;
            var recordNumber = 1;

            int codePoint;
            while ((codePoint = reader.Read()) >= 0)
            {
                var c = (char)codePoint;
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (reader.Peek() == '"')
                        {
                            reader.Read();
                            field.Append('"');
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else if (c == '\r')
                    {
                        if (reader.Peek() == '\n') reader.Read();
                        field.Append('\n');
                    }
                    else
                    {
                        field.Append(c);
                    }
                    recordStarted = true;
                    continue;
                }

                switch (c)
                {
                    case '"' when field.Length == 0:
                        inQuotes = true;
                        recordStarted = true;
                        break;
                    case ',':
                        fields.Add(field.ToString());
                        field.Clear();
                        recordStarted = true;
                        break;
                    case '\r':
                        if (reader.Peek() == '\n') reader.Read();
                        fields.Add(field.ToString());
                        yield return fields.ToArray();
                        fields.Clear();
                        field.Clear();
                        recordStarted = false;
                        recordNumber++;
                        break;
                    case '\n':
                        fields.Add(field.ToString());
                        yield return fields.ToArray();
                        fields.Clear();
                        field.Clear();
                        recordStarted = false;
                        recordNumber++;
                        break;
                    default:
                        field.Append(c);
                        recordStarted = true;
                        break;
                }
            }

            if (inQuotes)
                throw new InvalidDataException($"[CsvCtrl] 第 {recordNumber} 行引號未閉合，不支援欄位內換行。");
            if (recordStarted)
            {
                fields.Add(field.ToString());
                yield return fields.ToArray();
            }
        }

        // 統一的行切割：RFC4180 逐字元解析——逗號分隔，"…" 包住的欄位允許內含逗號，"" 跳脫成一個字面 "。
        // 未加引號的欄位照字面取用，不 trim（維持現況行為）。lineNumber 僅供例外訊息使用（預設 0＝不明）。
        // 不支援欄位內換行：行結束時引號未閉合 → InvalidDataException，訊息含行號與該行內容。
        private static string[] SplitLine(string line, int lineNumber = 0)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c != '"') { sb.Append(c); continue; }
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }

            if (inQuotes)
                throw new InvalidDataException(
                    $"[CsvCtrl] 第 {lineNumber} 行引號未閉合，不支援欄位內換行：'{line}'");

            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        // Set 檔（單欄）沿用同一套逐字元解析，只是不切欄——整行視為一個欄位，引號 / "" 跳脫規則與 SplitLine 相同。
        private static string UnquoteLine(string line, int lineNumber) => string.Join(",", SplitLine(line, lineNumber));

        /// <summary>把 Data/ 下的檔案內容清成一行空白（檔案保留，資料全失）。無法復原，確認過再叫。</summary>
        public static void ClearData(string fileName)
        {
            using var sw = new StreamWriter(FolderDir.Data.GetFilePath(EnsureCsv(fileName)), append: false, _csvWrite);
            sw.WriteLine("");
        }

        /// <summary>
        /// 在 Data/ 產一份只有表頭的參數 CSV 範本（欄位 = DATA_ID + 該類別各 property 大寫），供人工填資料。
        /// 檔名為型別名；檔案已存在會被覆寫成空表頭。
        /// </summary>
        public static void CreateParamTable<TParameter>()
        {
            string name = typeof(TParameter).Name;
            FolderDir.Data.TryCreateFile($"{name}.csv");
            using var sw = new StreamWriter(FolderDir.Data.GetFilePath($"{name}.csv"), append: false, _csvWrite);
            string cols = "DATA_ID," + string.Join(",", ReflectionHelper.GetMemberNames(typeof(TParameter)).Select(s => s.ToUpper()));
            sw.WriteLine(cols);
        }

        // 以下四個讀 Data/ 下的單欄檔（每列一個成員），差別只在轉型；格式不符會由 Parse 丟 FormatException

        /// <summary>讀單欄 CSV 成 int 清單。</summary>
        public static List<int> ReadIntSet(string fileName) => ReadLines(FolderDir.Data.GetFilePath(EnsureCsv(fileName)), s => int.Parse(s, CultureInfo.InvariantCulture));

        /// <summary>讀單欄 CSV 成 double 清單。</summary>
        public static List<double> ReadDoubleSet(string fileName) => ReadLines(FolderDir.Data.GetFilePath(EnsureCsv(fileName)), s => double.Parse(s, CultureInfo.InvariantCulture));

        /// <summary>讀單欄 CSV 成 string 清單（不轉型，set 載入的預設路徑）。</summary>
        public static List<string> ReadStrSet(string fileName) => ReadLines(FolderDir.Data.GetFilePath(EnsureCsv(fileName)), s => s);

        /// <summary>讀單欄 CSV 成 DateTime 清單（依當前 culture 解析）。</summary>
        public static List<DateTime> ReadDateSet(string fileName) => ReadLines(FolderDir.Data.GetFilePath(EnsureCsv(fileName)), s => DateTime.Parse(s, CultureInfo.InvariantCulture));

        private static List<TValue> ReadLines<TValue>(string path, Func<string, TValue> parser)
        {
            var list = new List<TValue>();
            foreach (var row in ReadRecords(path))
                list.Add(parser(string.Join(",", row).Trim()));
            return list;
        }

        private static List<string[]> ReadRecords(string path)
        {
            using var reader = new StreamReader(path, _csvRead);
            return ParseCsv(reader).ToList();
        }

        /// <summary>
        /// 讀整張 CSV 為 DataTable（供 set/param 以外的通用用途）：第一列 = 欄名，其餘 = 資料列，全欄型別 string。
        /// 與 typed 的 BuildParameter 不同——不對 class 對位、不轉型，回原始表格。fileName 帶不帶 .csv 皆可。
        /// </summary>
        public static DataTable ReadTable(string fileName)
        {
            string path = FolderDir.Data.GetFilePath(EnsureCsv(fileName));
            var table = new DataTable();
            var rows = ReadRecords(path)
                .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                .ToArray();
            if (rows.Length == 0) return table;

            foreach (var col in rows[0])
                table.Columns.Add(col.Trim());

            foreach (var parts in rows.Skip(1))
            {
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
            foreach (var parts in ReadRecords(path))
            {
                if (parts.Length >= 2 && double.TryParse(parts.Last(), NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    data["@" + string.Join("@", parts.Take(parts.Length - 1).Select(part => part.Trim()))] = val;
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

            var rows = ReadRecords(path)
                .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                .ToArray();
            if (rows.Length == 0) return data;

            // 表頭偵測：第一行最後一欄 parse 不成數字 → 視為表頭（參數/解檔最後的資料欄必為數值或 USER 字串欄）
            var firstParts = rows[0];
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

            foreach (var parts in rows.Skip(hasHeader ? 1 : 0))
            {
                var cells = new string[props.Length];
                for (int i = 0; i < props.Length; i++)
                {
                    if (colMap[i] >= parts.Length)
                        throw new InvalidDataException($"[CsvCtrl] {Path.GetFileName(path)} 資料列欄數不足（需要至少 {colMap[i] + 1} 欄）。");
                    cells[i] = parts[colMap[i]];
                }
                var instance = new TParameter();
                var values = ParameterRowMapper.ConvertCells(props, cells, $"CsvCtrl {Path.GetFileName(path)}");
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

        /// <summary>
        /// 把一維 set 的成員寫成 Data/{fileName}.csv（單欄、無表頭、保序）——即 ReadStrSet / SetBase.Load 讀得回的格式。
        /// 輸出位置就是既有的讀取位置：第一階段解析完不規則來源後寫回 Data/，第二階段 new CsvDataSource() 原封不動就讀得到。
        /// fileName 省略時用 set.GetType().Name（"Set_Row" 這個檔名形式）——ISetBrick 上沒有 SetName，故走 GetType()。
        /// </summary>
        public static void WriteSet(ISetBrick set, string fileName = null)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));

            // 先取 Count 觸發 SetBase 的 EnsureLoaded：未載入時在開檔前就丟例外，不留半截檔
            int count = set.Count;

            string path = FolderDir.Data.GetFilePath(EnsureCsv(SetNaming.File(fileName ?? set.GetType().Name)));
            bool overwritten = File.Exists(path);
            FolderDir.Data.CreateFolder();

            using (var sw = new StreamWriter(path, append: false, _csvWrite))
            {
                foreach (var member in set.MembersAsObjects())
                    sw.WriteLine(Quote(FormatValue(member)));
            }

            Logging.Info($"[CsvCtrl] set written: {path}（rows={count}, overwritten={overwritten}）");
        }

        /// <summary>
        /// 把 parameter 列寫成 Data/{fileName}.csv（第一列表頭 = property 名大寫，其後每列一筆）——即 BuildParameter 讀得回的格式。
        /// fileName 省略時用型別名，與 BuildParameter 的預設一致。
        /// 欄位來源 MUST 是 typeof(TParameter).GetProperties()（與 BuildParameter / InitClassBySets 同一來源）——
        /// NEVER 用 ReflectionHelper.GetMemberNames，它會撈進 field 與 static member，round-trip 會對不上欄。
        /// </summary>
        public static void WriteParam<TParameter>(IReadOnlyList<TParameter> rows, string fileName = null)
            where TParameter : ModelElementBase
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var props = typeof(TParameter).GetProperties();
            if (props.Length == 0)
                throw new InvalidOperationException($"[CsvCtrl] {typeof(TParameter).Name} 沒有任何 public property，無法輸出。");

            string path = FolderDir.Data.GetFilePath(EnsureCsv(fileName ?? typeof(TParameter).Name));
            bool overwritten = File.Exists(path);
            FolderDir.Data.CreateFolder();

            using (var sw = new StreamWriter(path, append: false, _csvWrite))
            {
                // 表頭欄名大寫：與 CreateParamTable / WriteSolution 一致；BuildParameter 按名對位時大小寫不敏感
                sw.WriteLine(string.Join(",", props.Select(p => p.Name.ToUpperInvariant())));

                foreach (var row in rows)
                    sw.WriteLine(string.Join(",", props.Select(p => Quote(FormatValue(p.GetValue(row))))));
            }

            Logging.Info($"[CsvCtrl] param written: {path}（rows={rows.Count}, overwritten={overwritten}）");
        }

        // 值 → CSV 欄位字串。數值一律 InvariantCulture（double 用 "R" round-trip 格式，與 WriteSolution 同）；
        // DateTime 固定 yyyy-MM-dd，與 ModelElementBase.ToString() 的變數 key 格式一致。
        private static string FormatValue(object value)
        {
            switch (value)
            {
                case null:
                    return "";
                case DateTime d when d.TimeOfDay != TimeSpan.Zero:
                    // index set 的粒度只到日（變數 key 就是 yyyy-MM-dd），靜默截掉時分秒會讓 round-trip 悄悄失真
                    throw new NotSupportedException($"[CsvCtrl] 不支援帶時分秒的 DateTime：{d:O}——index 粒度只到日。");
                case DateTime d:
                    return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case double n:
                    return n.ToString("R", CultureInfo.InvariantCulture);
                case float f:
                    return f.ToString("R", CultureInfo.InvariantCulture);
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return value.ToString();
            }
        }

        // 與 SplitLine 對稱的 quoting：含逗號 / 引號 / 前後空白才包引號，內含的 " 跳脫成 ""
        private static string Quote(string field)
            => field.IndexOf(',') >= 0 || field.IndexOf('"') >= 0 || field != field.Trim()
                ? "\"" + field.Replace("\"", "\"\"") + "\""
                : field;
    }
}
