using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OptimFoundation.Core.IO
{
    /// <summary>
        /// CSV 格式與寫出控制器：讀取端只負責 RFC4180 文字解析，位址與 schema 由資料來源層處理。
    /// </summary>
    public static class CsvCtrl
    {
        // CSV 給人 / Excel 開啟：UTF-8 with BOM，避免 zh-TW Excel 以 Big5(950) 誤判中文成亂碼
        private static readonly Encoding _csvWrite = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        // 檔名慣例統一：帶不帶 .csv 皆可（寫出端補齊，呼叫端不必記哪個 API 要帶副檔名）
        private static string EnsureCsv(string fileName)
            => fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".csv";

        /// <summary>
        /// 依 RFC4180 規則解析輸入資料。引號內換行保留為 <c>\n</c>、逸出引號會解碼，
        /// 每次產出的是完整的邏輯資料列，而非實體文字行。
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

        /// <summary>
        /// 把某變數型別的解值匯出到 Solution/{型別名}.csv（表頭：VAR_TYPE,set…,QTY）。
        /// 表頭欄名 = property 名，故此檔可直接被 LoadParam 讀回（按名對位、多餘欄自動忽略）。
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
        /// 把 set 的成員寫成 Data/{fileName}.csv（表頭 + 成員列、保序），可由 SetBase.Load 讀回。
        /// 輸出位置就是既有的讀取位置：第一階段解析完不規則來源後寫回 Data/，第二階段 new CsvDataSource() 原封不動就讀得到。
        /// fileName 省略時用 set.GetType().Name（例如 "Set_Row"）；傳入名稱時完全照用，不補 Set_ 前綴。
        /// </summary>
#if false
        private static void WriteSet(dynamic set, string fileName = null)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));

            // 先取 Count 觸發 SetBase 的 EnsureLoaded：未載入時在開檔前就丟例外，不留半截檔
            int count = set.Count;

            string path = FolderDir.Data.GetFilePath(EnsureCsv(fileName ?? set.GetType().Name));
            bool overwritten = File.Exists(path);
            FolderDir.Data.CreateFolder();

            using (var sw = new StreamWriter(path, append: false, _csvWrite))
            {
                var setName = set.SetName;
                var header = set.Arity == 1
                    ? new[] { setName }
                    : Enumerable.Range(1, set.Arity).Select(index => $"{setName}_{index}").ToArray();
                sw.WriteLine(string.Join(",", header.Select(Quote)));
                foreach (var member in set.MembersAsObjects())
                {
                    string[] values;
                    if (set.Arity == 1)
                    {
                        values = new[] { FormatValue(member) };
                    }
                    else
                    {
                        var tuple = member as System.Runtime.CompilerServices.ITuple
                            ?? throw new InvalidOperationException("A multi-column set member must be a ValueTuple.");
                        values = Enumerable.Range(0, tuple.Length)
                            .Select(index => FormatValue(tuple[index]))
                            .ToArray();
                    }
                    sw.WriteLine(string.Join(",", values.Select(Quote)));
                }
            }

            Logging.Info($"[CsvCtrl] set written: {path}（rows={count}, overwritten={overwritten}）");
        }

        /// <summary>
        /// 把 parameter 列寫成 Data/{fileName}.csv（第一列表頭 = property 名大寫，其後每列一筆）——即 LoadParam 讀得回的格式。
        /// fileName 省略時用型別名，與 LoadParam 的預設一致。
        /// 欄位來源 MUST 是 typeof(TParameter).GetProperties()（與 LoadParam / InitClassBySets 同一來源）——
        /// NEVER 用 ReflectionHelper.GetMemberNames，它會撈進 field 與 static member，round-trip 會對不上欄。
        /// </summary>
#endif
        public static void WriteRows<T>(IReadOnlyList<T> rows, string fileName = null)
            where T : ModelElementBase
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var props = typeof(T).GetProperties();
            if (props.Length == 0)
                throw new InvalidOperationException($"[CsvCtrl] {typeof(T).Name} 沒有任何 public property，無法輸出。");

            string path = FolderDir.Data.GetFilePath(EnsureCsv(fileName ?? typeof(T).Name));
            bool overwritten = File.Exists(path);
            FolderDir.Data.CreateFolder();

            using (var sw = new StreamWriter(path, append: false, _csvWrite))
            {
                // 表頭欄名大寫；LoadParam 按名對位時大小寫不敏感
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
