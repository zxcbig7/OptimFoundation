using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OptimFoundation.Core.IO
{
    /// <summary>在含 schema 的 CSV 資料列與中立的 <see cref="DataTable"/> 間轉換。</summary>
    internal static class TabularData
    {
        internal static DataTable ToDataTable(IEnumerable<string[]> records, string sourceDescription)
        {
            if (records == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(records)),
                    "TABULAR_DATA_INVALID", "表格資料不合法", nameof(ToDataTable), null, "records_are_null");
            var rows = records.Select(row => row?.ToArray()
                ?? throw Logging.ErrorOnce(
                    new InvalidDataException($"[{sourceDescription}] A CSV row cannot be null."),
                    "TABULAR_DATA_INVALID", "表格資料不合法", sourceDescription, null, "row_is_null")).ToArray();
            if (rows.Length == 0)
                throw Logging.ErrorOnce(
                    new InvalidDataException($"[{sourceDescription}] CSV requires a header row."),
                    "TABULAR_DATA_INVALID", "表格資料不合法", sourceDescription, "<empty>", "header_is_missing");

            var headers = rows[0].Select(header => (header ?? string.Empty).Trim()).ToArray();
            if (headers.Length == 0 || headers.Any(string.IsNullOrEmpty))
                throw Logging.ErrorOnce(
                    new InvalidDataException($"[{sourceDescription}] CSV header cannot contain empty column names."),
                    "TABULAR_DATA_INVALID", "表格資料不合法", sourceDescription, string.Join(",", headers), "header_contains_empty_column");
            if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
                throw Logging.ErrorOnce(
                    new InvalidDataException($"[{sourceDescription}] CSV header contains duplicate column names."),
                    "TABULAR_DATA_INVALID", "表格資料不合法", sourceDescription, string.Join(",", headers), "header_contains_duplicate_column");

            var table = new DataTable();
            foreach (var header in headers) table.Columns.Add(header, typeof(string));

            for (var rowIndex = 1; rowIndex < rows.Length; rowIndex++)
            {
                if (rows[rowIndex].Length != headers.Length)
                    throw Logging.ErrorOnce(
                        new InvalidDataException($"[{sourceDescription}] Row {rowIndex + 1} has {rows[rowIndex].Length} columns; expected {headers.Length}."),
                        "TABULAR_DATA_INVALID", "表格資料不合法", sourceDescription, rowIndex + 1, "column_count_mismatch",
                        $"actual={rows[rowIndex].Length} expected={headers.Length}");
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
                    new ArgumentNullException(nameof(table)),
                    "TABULAR_DATA_INVALID", "表格資料不合法", nameof(ToRecords), null, "table_is_null");
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
