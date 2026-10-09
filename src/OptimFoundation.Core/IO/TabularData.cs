using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OptimFoundation.Core.IO
{
    /// <summary>在第一列為欄名的 CSV 資料與 <see cref="DataTable"/> 之間轉換。</summary>
    internal static class TabularData
    {
        internal static DataTable ToDataTable(IEnumerable<string[]> records, string sourceDescription)
        {
            if (records == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(records), "records 不得為 null"),
                    "表格資料不合法", null, nameof(ToDataTable), null, "資料列集合為空");
            var rows = records.Select(row => row?.ToArray()
                ?? throw Logging.ErrorOnce(
                    new InvalidDataException($"{sourceDescription}：CSV 資料列不得為 null"),
                    "表格資料不合法", null, sourceDescription, null, "資料列為空")).ToArray();
            if (rows.Length == 0)
                throw Logging.ErrorOnce(
                    new InvalidDataException($"{sourceDescription}：CSV 需要標題列"),
                    "表格資料不合法", null, sourceDescription, "<空白>", "找不到標題列");

            var headers = rows[0].Select(header => (header ?? string.Empty).Trim()).ToArray();
            if (headers.Length == 0 || headers.Any(string.IsNullOrEmpty))
                throw Logging.ErrorOnce(
                    new InvalidDataException($"{sourceDescription}：CSV 標題列不得含空的欄名"),
                    "表格資料不合法", null, sourceDescription, string.Join(",", headers), "標題列含空的欄名");
            if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
                throw Logging.ErrorOnce(
                    new InvalidDataException($"{sourceDescription}：CSV 標題列含重複的欄名"),
                    "表格資料不合法", null, sourceDescription, string.Join(",", headers), "標題列含重複的欄名");

            var table = new DataTable();
            foreach (var header in headers) table.Columns.Add(header, typeof(string));

            for (var rowIndex = 1; rowIndex < rows.Length; rowIndex++)
            {
                // 整列空白（含 CSV 空行）不是資料：記警告後略過，不當成欄數不一致。
                if (rows[rowIndex].All(string.IsNullOrWhiteSpace))
                {
                    Logging.Warn($"[資料列為空] 名稱={sourceDescription} 序號={rowIndex + 1} 原因=整列空白 結果=略過");
                    continue;
                }
                if (rows[rowIndex].Length != headers.Length)
                    throw Logging.ErrorOnce(
                        new InvalidDataException($"{sourceDescription}：第 {rowIndex + 1} 資料列有 {rows[rowIndex].Length} 欄，預期 {headers.Length} 欄"),
                        "表格資料不合法", null, sourceDescription, rowIndex + 1, "欄數不一致",
                        $"數量={rows[rowIndex].Length}/{headers.Length}");
                var row = table.NewRow();
                for (var columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                    row[columnIndex] = rows[rowIndex][columnIndex] ?? string.Empty;
                table.Rows.Add(row);
            }
            return table;
        }

        internal static IEnumerable<string[]> ToRecords(DataTable table)
        {
            if (table == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(table), "table 不得為 null"),
                    "表格資料不合法", null, nameof(ToRecords), null, "資料表為空");
            yield return table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToArray();
            foreach (DataRow row in table.Rows)
                yield return row.ItemArray.Select(ToInvariantString).ToArray();
        }

        private static string ToInvariantString(object value)
            => value == null || value == DBNull.Value
                ? string.Empty
                : value is IFormattable formattable
                    ? formattable.ToString(null, CultureInfo.InvariantCulture)
                    : value.ToString();
    }
}
