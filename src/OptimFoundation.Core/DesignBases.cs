using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace OptimFoundation.Core
{
    /// <summary>Guards numeric values used to derive model constants.</summary>
    public static class Numeric
    {
        public static double SafeRatio(double numerator, double denominator, double magnitudeCeiling = 1e12, string context = null)
        {
            if (denominator == 0)
                throw Logging.ErrorOnce(
                    new InvalidOperationException($"{context ?? "Ratio"}: 除零 is not allowed."),
                    "NUMERIC_RATIO_INVALID", "數值比例計算失敗", context ?? "Ratio", denominator, "division_by_zero");

            var value = numerator / denominator;
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw Logging.ErrorOnce(
                    new InvalidOperationException($"{context ?? "Ratio"}: result must be finite."),
                    "NUMERIC_RATIO_INVALID", "數值比例計算失敗", context ?? "Ratio", value, "result_not_finite");
            if (Math.Abs(value) > magnitudeCeiling)
                throw Logging.ErrorOnce(
                    new InvalidOperationException($"{context ?? "Ratio"}: result 過大; 門檻 {magnitudeCeiling}."),
                    "NUMERIC_RATIO_INVALID", "數值比例計算失敗", context ?? "Ratio", value, "magnitude_ceiling_exceeded",
                    $"ceiling={magnitudeCeiling}");
            return value;
        }
    }

    /// <summary>Common base for generated Set, Parameter, and Variable rows.</summary>
    public abstract class ModelElementBase
    {
        internal const char KeySeparator = ModelNaming.Separator;
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Props = new();

        private static PropertyInfo[] GetProps(Type type) => Props.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
             .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
             .ToArray());

        public void InitClassBySets(params object[] values)
        {
            var properties = GetProps(GetType());
            if (values.Length != properties.Length)
            {
                string message = $"{GetType().Name} expects {properties.Length} values but received {values.Length}.";
                throw Logging.ErrorOnce(
                    new ArgumentException(message),
                    "MODEL_ELEMENT_INIT_FAILED", "模型元素初始化失敗", nameof(InitClassBySets), GetType().Name,
                    "arity_mismatch", $"type={GetType().Name} expected={properties.Length} actual={values.Length}");
            }

            for (var index = 0; index < properties.Length; index++)
            {
                string context = $"{GetType().Name}.{properties[index].Name}";
                try
                {
                    object? converted = ConvertValue(values[index], properties[index].PropertyType);
                    ModelNaming.Token(context, converted);
                    properties[index].SetValue(this, converted);
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException)
                {
                    throw Logging.ErrorOnce(
                        new InvalidCastException($"Cannot convert {context}.", ex),
                        "MODEL_ELEMENT_INIT_FAILED", "模型元素初始化失敗", context, values[index],
                        "conversion_failed", $"detail={ex.GetBaseException().Message}");
                }
            }
        }

        private static object? ConvertValue(object? value, Type targetType)
        {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;
            if (targetType == typeof(DateTime) && value is string text
                && DateTime.TryParseExact(text, ModelNaming.DateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime modelDate))
                return modelDate;
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        internal static void ValidateKeyToken(string context, string value)
            => ModelNaming.ValidateToken(context, value);

        private protected string[] KeyParts()
            => GetProps(GetType())
                .Select(property => ModelNaming.Token($"{GetType().Name}.{property.Name}", property.GetValue(this)))
                .ToArray();

        public override string ToString()
            => ModelNaming.Compose(GetType().Name, KeyParts());
    }

    /// <summary>A Set row: an existing dimensional combination with no QTY.</summary>
    public abstract class SetRowBase : ModelElementBase
    {
        // Set row 的字串形式＝它在變數 key 裡佔的那幾段，不含類別名 —— Why: 使用者會把 set row 直接內插進
        // constraint 名與解答查詢 key，帶上類別名就與 BuildVars 反射屬性組出來的變數名對不起來，而那種錯只會
        // 讓 TryGetValue 回 false、解答靜默變 0，不會報錯。
        public override string ToString() => string.Join(KeySeparator, KeyParts());
    }

    /// <summary>A Parameter row: a dimensional combination with generated QTY.</summary>
    public abstract class ParameterBase : ModelElementBase { }

    public abstract class VariableBase : ModelElementBase { }
    public abstract class ConstraintBase : ModelElementBase
    {
        protected string ConstraintName => GetType().Name;
    }
}
