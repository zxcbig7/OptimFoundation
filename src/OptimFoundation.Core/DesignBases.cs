using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>Guards numeric values used to derive model constants.</summary>
    public static class Numeric
    {
        public static double SafeRatio(double numerator, double denominator, double magnitudeCeiling = 1e12, string context = null)
        {
            if (denominator == 0)
                throw new InvalidOperationException($"{context ?? "Ratio"}: 除零 is not allowed.");

            var value = numerator / denominator;
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException($"{context ?? "Ratio"}: result must be finite.");
            if (Math.Abs(value) > magnitudeCeiling)
                throw new InvalidOperationException($"{context ?? "Ratio"}: result 過大; 門檻 {magnitudeCeiling}.");
            return value;
        }
    }

    /// <summary>Common base for generated Set, Parameter, and Variable rows.</summary>
    public abstract class ModelElementBase
    {
        internal const char KeySeparator = '@';
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Props = new();

        private static PropertyInfo[] GetProps(Type type) => Props.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
             .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
             .ToArray());

        public void InitClassBySets(params object[] values)
        {
            var properties = GetProps(GetType());
            if (values.Length != properties.Length)
                throw new ArgumentException($"{GetType().Name} expects {properties.Length} values but received {values.Length}.");

            for (var index = 0; index < properties.Length; index++)
            {
                var value = values[index];
                if (value is string text) ValidateKeyToken($"{GetType().Name}.{properties[index].Name}", text);
                try
                {
                    properties[index].SetValue(this, ConvertValue(value, properties[index].PropertyType));
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException)
                {
                    throw new InvalidCastException($"Cannot convert {GetType().Name}.{properties[index].Name}.", ex);
                }
            }
        }

        private static object? ConvertValue(object? value, Type targetType)
        {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        internal static void ValidateKeyToken(string context, string value)
        {
            if (value?.Contains(KeySeparator) == true)
                throw new ArgumentException($"{context} value '{value}' cannot contain reserved key separator '{KeySeparator}'.");
        }

        public override string ToString()
        {
            var values = GetProps(GetType()).Select(p => FormatKeyPart(p.GetValue(this))).ToArray();
            return values.Length == 0
                ? GetType().Name
                : GetType().Name + KeySeparator + string.Join(KeySeparator, values);
        }

        private static string FormatKeyPart(object? value) => value switch
        {
            DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            null => string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>A Set row: an existing dimensional combination with no QTY.</summary>
    public abstract class SetRowBase : ModelElementBase { }

    /// <summary>A Parameter row: a dimensional combination with generated QTY.</summary>
    public abstract class ParameterBase : ModelElementBase { }

    public abstract class VariableBase : ModelElementBase { }
    public abstract class ConstraintBase : ModelElementBase
    {
        protected string ConstraintName => GetType().Name;
    }
}
