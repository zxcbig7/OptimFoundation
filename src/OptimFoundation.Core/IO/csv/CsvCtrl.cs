using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 解析與寫出 CSV 文字；讀取檔案路徑由 CsvDataSource 處理，欄名與模型 property 的對應由 ModelRowMapper 處理。
    /// </summary>
    public static class CsvCtrl
    {
        // BOM 供 Excel 正確辨識 UTF-8 中文。
        private static readonly Encoding _csvWrite = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        // 寫出時補上 .csv 副檔名。
        private static string EnsureCsv(string fileName)
            => fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".csv";

        /// <summary>
        /// 依 RFC4180 解析完整資料列；引號內換行保留為 <c>\n</c>，雙引號逸出會解碼。
        /// </summary>
        public static IEnumerable<string[]> ParseCsv(TextReader reader)
        {
            if (reader == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(reader), "reader 不得為 null"),
                    "CSV 解析不合法", null, nameof(ParseCsv), null, "輸入串流為空");

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
                throw Logging.ErrorOnce(
                    new InvalidDataException($"CSV 引號未閉合：第 {recordNumber} 行，不支援欄內換行"),
                    "CSV 解析不合法", null, nameof(ParseCsv), recordNumber, "引號未閉合");
            if (recordStarted)
            {
                fields.Add(field.ToString());
                yield return fields.ToArray();
            }
        }

        /// <summary>
        /// 把某變數型別的解值寫到 FolderDir.Output 下的 {型別名}.csv（表頭：VAR_TYPE,set…,QTY；零維變數為 VAR_TYPE,QTY）。
        /// 表頭欄名 = property 名；欄位相容時可被 IDataSource.Load&lt;T&gt; 讀回（按名對位、多餘欄自動忽略）。
        /// dataId / userId 僅供 DB sink 用；CSV 不輸出這兩欄。
        /// </summary>
        public static void WriteSolution<TVariable>(ISolverEngine engine, string dataId, string userId)
        {
            if (engine == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(engine), "engine 不得為 null"),
                    "CSV 寫出不合法", "寫出解", nameof(WriteSolution), typeof(TVariable).Name, "引擎為空");

            try
            {
                var classInfo = new ClassInfo(typeof(TVariable));
                FolderDir.Output.TryCreateFile($"{classInfo.TypeName}.csv");
                string file = FolderDir.Output.GetPathFile($"{classInfo.TypeName}.csv");
                var sol = engine.GetSolution(classInfo.TypeName);

                using var sw = new StreamWriter(file, append: false, _csvWrite);
                // 逐欄 Join：零維變數沒有 set 欄，不能在 VAR_TYPE 與 QTY 之間留空欄
                var cols = new[] { "VAR_TYPE" }
                    .Concat(classInfo.SetNames.Select(s => s.ToUpper()))
                    .Append("QTY");
                sw.WriteLine(string.Join(",", cols));

                foreach (var kv in sol)
                {
                    string[] parts = kv.Key.Split('@');
                    // 數值用 InvariantCulture round-trip 格式，讀回不失真
                    string row = string.Join(",", parts.Append(kv.Value.ToString("R", CultureInfo.InvariantCulture)));
                    sw.WriteLine(row);
                }

                Logging.Info($"[CSV 解寫出完成] 路徑={file}");
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "CSV 寫出失敗", null, nameof(WriteSolution), typeof(TVariable).Name,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        /// <summary>
        /// 將 Set/Parameter 寫至 FolderDir.Input/{fileName}.csv，預設檔名為型別名，表頭為大寫 property 名。
        /// 僅使用 GetProperties，與 Load&lt;T&gt; 一致；不可加入 field 或 static member。
        /// </summary>
        public static void WriteRows<T>(IReadOnlyList<T> rows, string fileName = null)
            where T : ModelElementBase
        {
            if (rows == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(rows), "rows 不得為 null"),
                    "CSV 寫出不合法", "寫出資料列", nameof(WriteRows), typeof(T).Name, "資料列集合為空");

            try
            {
                var props = typeof(T).GetProperties();
                if (props.Length == 0)
                    throw Logging.ErrorOnce(
                        new InvalidOperationException($"找不到公開屬性：{typeof(T).Name} 沒有任何 public 屬性，無法寫出"),
                        "CSV 寫出不合法", "寫出資料列", nameof(WriteRows), typeof(T).Name, "找不到公開屬性");

                string path = FolderDir.Input.GetPathFile(EnsureCsv(fileName ?? typeof(T).Name));
                bool overwritten = File.Exists(path);
                FolderDir.Input.CreateFolder();

                using (var sw = new StreamWriter(path, append: false, _csvWrite))
                {
                    sw.WriteLine(string.Join(",", props.Select(p => p.Name.ToUpperInvariant())));

                    foreach (var row in rows)
                        sw.WriteLine(string.Join(",", props.Select(p => Quote(FormatValue(p.GetValue(row))))));
                }

                Logging.Info($"[CSV 資料列寫出完成] 路徑={path} 數量={rows.Count} 覆寫={(overwritten ? "是" : "否")}");
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "CSV 寫出失敗", null, nameof(WriteRows), typeof(T).Name,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        // 數值用 InvariantCulture，double 用 R；日期用 yyyy-MM-dd[ HH:mm:ss]，與模型命名格式不同。
        private static string FormatValue(object value)
        {
            switch (value)
            {
                case null:
                    return "";
                case DateTime d when d.Ticks % TimeSpan.TicksPerSecond != 0:
                    // 捨去秒以下精度會使 CSV 讀回值失真。
                    throw Logging.ErrorOnce(
                        new NotSupportedException($"不支援秒以下精度的 DateTime：{d:O}，索引粒度只到秒"),
                        "CSV 日期寫出不合法", null, nameof(FormatValue), d.ToString("O", CultureInfo.InvariantCulture),
                        "日期含秒以下精度");
                case DateTime d:
                    return d.ToString(
                        d.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture);
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

        // 欄位含逗號、雙引號或前後空白時加上雙引號，並把欄位內的 " 寫成 ""。
        private static string Quote(string field)
            => field.IndexOf(',') >= 0 || field.IndexOf('"') >= 0 || field != field.Trim()
                ? "\"" + field.Replace("\"", "\"\"") + "\""
                : field;
    }
}
