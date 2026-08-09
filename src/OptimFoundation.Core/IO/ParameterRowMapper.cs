using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OptimFoundation.Core.IO
{
    /// <summary>集中處理 Parameter 儲存格的去除空白與不依賴地區設定的型別轉換。</summary>
    internal static class ModelRowMapper
    {
        internal static List<TParameter> MapTable<TParameter>(DataTable table, string sourceDescription)
            where TParameter : ModelElementBase, new()
            => MapRows<TParameter>(TabularData.ToRecords(table), sourceDescription);

        /// <summary>
        /// 將原始資料列映射成 Parameter 物件。第一列必須列出所有 public property，
        /// 後續資料列依該表頭對應。
        /// </summary>
        internal static List<TParameter> MapRows<TParameter>(IEnumerable<string[]> rows, string sourceDescription)
            where TParameter : ModelElementBase, new()
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var properties = typeof(TParameter).GetProperties();
            var normalizedRows = rows
                .Select(row => row?.Select(cell => (cell ?? string.Empty).Trim()).ToArray()
                    ?? throw new InvalidDataException($"[{sourceDescription}] A data row cannot be null."))
                .Where(row => row.Any(cell => cell.Length > 0))
                .ToArray();
            var result = new List<TParameter>();
            if (normalizedRows.Length == 0) return result;

            var firstRow = normalizedRows[0];
            var columnMap = properties.Select(property => FindColumn(firstRow, property.Name)).ToArray();
            var hasHeader = columnMap.All(index => index >= 0);
            if (!hasHeader)
                throw new InvalidDataException(
                    $"[{sourceDescription}] Parameter CSV requires a header containing: {string.Join(", ", properties.Select(property => property.Name))}. " +
                    $"Actual first row: {string.Join(", ", firstRow)}.");

            foreach (var row in normalizedRows.Skip(hasHeader ? 1 : 0))
            {
                var cells = new string[properties.Length];
                for (var i = 0; i < properties.Length; i++)
                {
                    if (columnMap[i] >= row.Length)
                        throw new InvalidDataException(
                            $"[{sourceDescription}] A data row is missing a value for '{properties[i].Name}'.");
                    cells[i] = row[columnMap[i]];
                }

                var item = new TParameter();
                item.InitClassBySets(ConvertCells(properties, cells, sourceDescription));
                result.Add(item);
            }
            return result;
        }

        private static int FindColumn(string[] columns, string name)
            => Array.FindIndex(columns, column => string.Equals(column, name, StringComparison.OrdinalIgnoreCase));

        internal static object[] ConvertCells(PropertyInfo[] properties, string[] cells, string sourceDescription)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            if (cells == null) throw new ArgumentNullException(nameof(cells));

            var values = new object[properties.Length];
            for (var i = 0; i < properties.Length; i++)
            {
                if (i >= cells.Length)
                    throw new InvalidDataException($"[{sourceDescription}] Row is missing a value for '{properties[i].Name}'.");
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
                throw new FormatException($"[{sourceDescription}] Value '{value}' cannot be parsed as {effectiveType.Name} for '{propertyName}' using invariant culture.", ex);
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
