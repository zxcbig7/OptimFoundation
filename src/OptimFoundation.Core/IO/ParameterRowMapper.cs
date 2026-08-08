using System;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace OptimFoundation.Core.IO
{
    /// <summary>Shared trim and invariant-culture conversion for parameter cells.</summary>
    internal static class ParameterRowMapper
    {
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
