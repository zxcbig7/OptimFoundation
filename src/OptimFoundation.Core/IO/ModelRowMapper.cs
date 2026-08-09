using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OptimFoundation.Core.IO
{
    /// <summary>集中處理 Set／Parameter model row 的表頭對位、去除空白與 invariant 型別轉換。</summary>
    internal static class ModelRowMapper
    {
        internal static List<TRow> MapTable<TRow>(DataTable table, string sourceDescription)
            where TRow : ModelElementBase, new()
            => MapRows<TRow>(TabularData.ToRecords(table), sourceDescription);

        /// <summary>
        /// 將原始資料列映射成 Set 或 Parameter model row。第一列必須列出所有 public property，
        /// 後續資料列依該表頭對應。
        /// </summary>
        internal static List<TRow> MapRows<TRow>(IEnumerable<string[]> rows, string sourceDescription)
            where TRow : ModelElementBase, new()
        {
            if (rows == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(rows)),
                    "MODEL_ROW_MAPPING_FAILED", "資料列映射失敗", sourceDescription, null, "rows_are_null");

            var properties = typeof(TRow).GetProperties();
            var normalizedRows = rows
                .Select(row => row?.Select(cell => (cell ?? string.Empty).Trim()).ToArray()
                    ?? throw Logging.ErrorOnce(
                        new InvalidDataException($"[{sourceDescription}] A data row cannot be null."),
                        "MODEL_ROW_MAPPING_FAILED", "資料列映射失敗", sourceDescription, null, "row_is_null"))
                .Where(row => row.Any(cell => cell.Length > 0))
                .ToArray();
            var result = new List<TRow>();
            if (normalizedRows.Length == 0) return result;

            var firstRow = normalizedRows[0];
            var columnMap = properties.Select(property => FindColumn(firstRow, property.Name)).ToArray();
            var hasHeader = columnMap.All(index => index >= 0);
            if (!hasHeader)
                throw Logging.ErrorOnce(
                    new InvalidDataException(
                        $"[{sourceDescription}] Model row source requires a header containing: {string.Join(", ", properties.Select(property => property.Name))}. " +
                        $"Actual first row: {string.Join(", ", firstRow)}."),
                    "MODEL_ROW_MAPPING_FAILED", "資料列映射失敗", sourceDescription, string.Join(",", firstRow),
                    "required_header_missing");

            foreach (var row in normalizedRows.Skip(hasHeader ? 1 : 0))
            {
                var cells = new string[properties.Length];
                for (var i = 0; i < properties.Length; i++)
                {
                    if (columnMap[i] >= row.Length)
                        throw Logging.ErrorOnce(
                            new InvalidDataException($"[{sourceDescription}] A data row is missing a value for '{properties[i].Name}'."),
                            "MODEL_ROW_MAPPING_FAILED", "資料列映射失敗", sourceDescription, properties[i].Name,
                            "row_value_missing");
                    cells[i] = row[columnMap[i]];
                }

                var item = new TRow();
                item.InitClassBySets(ConvertCells(properties, cells, sourceDescription));
                result.Add(item);
            }
            return result;
        }

        private static int FindColumn(string[] columns, string name)
            => Array.FindIndex(columns, column => string.Equals(column, name, StringComparison.OrdinalIgnoreCase));

        internal static object[] ConvertCells(PropertyInfo[] properties, string[] cells, string sourceDescription)
        {
            if (properties == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(properties)),
                    "MODEL_ROW_MAPPING_FAILED", "資料列映射失敗", nameof(ConvertCells), null, "properties_are_null");
            if (cells == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(cells)),
                    "MODEL_ROW_MAPPING_FAILED", "資料列映射失敗", nameof(ConvertCells), null, "cells_are_null");

            var values = new object[properties.Length];
            for (var i = 0; i < properties.Length; i++)
            {
                if (i >= cells.Length)
                    throw Logging.ErrorOnce(
                        new InvalidDataException($"[{sourceDescription}] Row is missing a value for '{properties[i].Name}'."),
                        "MODEL_ROW_MAPPING_FAILED", "資料列映射失敗", sourceDescription, properties[i].Name,
                        "row_value_missing");
                values[i] = ConvertCell(cells[i], properties[i].PropertyType, properties[i].Name, sourceDescription);
            }
            return values;
        }

        internal static object ConvertCell(string raw, Type targetType, string propertyName, string sourceDescription)
        {
            var value = (raw ?? string.Empty).Trim();
            if (targetType == typeof(string)) return value;

            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null && value.Length == 0) return null;
            var effectiveType = nullableType ?? targetType;

            try
            {
                if (effectiveType == typeof(DateTime))
                    return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
                if (effectiveType == typeof(DateOnly))
                    return DateOnly.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
                if (effectiveType == typeof(TimeOnly))
                    return TimeOnly.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
                if (effectiveType.IsEnum)
                    return Enum.Parse(effectiveType, value, ignoreCase: true);
                return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException || ex is ArgumentException)
            {
                throw Logging.ErrorOnce(
                    new FormatException($"[{sourceDescription}] Value '{value}' cannot be parsed as {effectiveType.Name} for '{propertyName}' using invariant culture.", ex),
                    "MODEL_VALUE_CONVERSION_FAILED", "資料值轉型失敗", $"{sourceDescription}.{propertyName}", value,
                    "invariant_conversion_failed", $"targetType={effectiveType.Name}");
            }
        }

        internal static string ToInvariantString(object value)
            => value == null || value == DBNull.Value
                ? string.Empty
                : value is IFormattable formattable
                    ? formattable.ToString(null, CultureInfo.InvariantCulture)
                    : value.ToString();
    }
}
