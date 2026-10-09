using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OptimFoundation.Core.IO
{
    /// <summary>依表頭把資料填入 Set/Parameter 的 property，去除欄位前後空白，並以 InvariantCulture 轉換數值與日期。</summary>
    internal static class ModelRowMapper
    {
        internal static List<TRow> MapTable<TRow>(DataTable table, string sourceDescription)
            where TRow : ModelElementBase, new()
            => MapRows<TRow>(TabularData.ToRecords(table), sourceDescription);

        /// <summary>依表頭建立資料列；欄名不分大小寫，且須完整覆蓋資料欄。</summary>
        internal static List<TRow> MapRows<TRow>(IEnumerable<string[]> rows, string sourceDescription)
            where TRow : ModelElementBase, new()
        {
            if (rows == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(rows), "rows 不得為 null"),
                    "資料列映射失敗", null, sourceDescription, null, "資料列集合為空");

            var properties = ModelElementBase.GetColumns(typeof(TRow));
            var normalizedRows = rows
                .Select(row => row?.Select(cell => (cell ?? string.Empty).Trim()).ToArray()
                    ?? throw Logging.ErrorOnce(
                        new InvalidDataException($"{sourceDescription}：資料列不得為 null"),
                        "資料列映射失敗", null, sourceDescription, null, "資料列為空"))
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
                        $"{sourceDescription}：資料列來源需要標題列，須包含欄名：{string.Join(", ", properties.Select(property => property.Name))}；" +
                        $"實際第一行：{string.Join(", ", firstRow)}"),
                    "資料列映射失敗", null, sourceDescription, string.Join(",", firstRow),
                    "找不到必要的欄名");

            foreach (var row in normalizedRows.Skip(hasHeader ? 1 : 0))
            {
                var cells = new string[properties.Length];
                for (var i = 0; i < properties.Length; i++)
                {
                    if (columnMap[i] >= row.Length)
                        throw Logging.ErrorOnce(
                            new InvalidDataException($"{sourceDescription}：資料列找不到 '{properties[i].Name}' 的值"),
                            "資料列映射失敗", null, sourceDescription, properties[i].Name,
                            "資料列找不到值");
                    cells[i] = row[columnMap[i]];
                }

                var item = new TRow();
                item.InitFromDataRow(ConvertCells(properties, cells, sourceDescription));
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
                    new ArgumentNullException(nameof(properties), "properties 不得為 null"),
                    "資料列映射失敗", null, nameof(ConvertCells), null, "屬性集合為空");
            if (cells == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(cells), "cells 不得為 null"),
                    "資料列映射失敗", null, nameof(ConvertCells), null, "資料列的值為空");

            var values = new object[properties.Length];
            for (var i = 0; i < properties.Length; i++)
            {
                if (i >= cells.Length)
                    throw Logging.ErrorOnce(
                        new InvalidDataException($"{sourceDescription}：資料列找不到 '{properties[i].Name}' 的值"),
                        "資料列映射失敗", null, sourceDescription, properties[i].Name,
                        "資料列找不到值");
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
                    new FormatException($"{sourceDescription}：無法以不變文化將值 '{value}' 轉成 {effectiveType.Name}（屬性：{propertyName}）", ex),
                    "資料值轉型失敗", null, $"{sourceDescription}.{propertyName}", value,
                    "以不變文化轉型失敗", $"型別={effectiveType.Name}");
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
