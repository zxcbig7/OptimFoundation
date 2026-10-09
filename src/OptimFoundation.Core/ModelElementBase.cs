using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace OptimFoundation.Core
{
    /// <summary>Set、Parameter 與 Variable 資料列的共用基底類別。</summary>
    public abstract class ModelElementBase
    {
        internal const char KeySeparator = ModelNaming.Separator;
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Dimensions = new();
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Columns = new();

        /// <summary>取得維度；generator 類別依 OptDim 順序，手寫類別依 public 可讀寫 property。</summary>
        internal static PropertyInfo[] GetDimensions(Type type) => Dimensions.GetOrAdd(type, t =>
        {
            var attribute = t.GetCustomAttribute<DimensionNamesAttribute>();
            if (attribute == null) return GetWritableProperties(t);

            return attribute.Names
                .Select(name => t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                    ?? throw Logging.ErrorOnce(
                        new InvalidOperationException($"{t.Name} 標記的維度 {name} 找不到對應的 public property"),
                        "模型元素維度不合法", null, nameof(GetDimensions), t.Name,
                        "找不到維度屬性", $"維度={name}"))
                .ToArray();
        });

        /// <summary>取得 CSV、DB 與初始化使用的資料欄；Parameter 包含 QTY 和其他可寫 property。</summary>
        internal static PropertyInfo[] GetColumns(Type type) => Columns.GetOrAdd(type, t =>
        {
            if (t.GetCustomAttribute<DimensionNamesAttribute>() == null) return GetWritableProperties(t);

            var dimensions = GetDimensions(t);
            if (!typeof(ParameterBase).IsAssignableFrom(t)) return dimensions;

            return dimensions
                .Concat(GetWritableProperties(t).Where(p => dimensions.All(d => d.Name != p.Name)))
                .ToArray();
        });

        private static PropertyInfo[] GetWritableProperties(Type type)
            => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                .ToArray();

        /// <summary>依資料欄順序初始化，並驗證維度 token。</summary>
        public void InitClassBySets(params object[] values) => Init(values, validateTokens: true);

        // 資料載入的維度 token 問題由 DataContext 記錄。
        internal void InitFromDataRow(object[] values) => Init(values, validateTokens: false);

        private void Init(object[] values, bool validateTokens)
        {
            var properties = GetColumns(GetType());
            if (values.Length != properties.Length)
            {
                string message = $"{GetType().Name} 需要 {properties.Length} 個值，但收到 {values.Length} 個";
                throw Logging.ErrorOnce(
                    new ArgumentException(message),
                    "模型元素初始化失敗", null, nameof(InitClassBySets), GetType().Name,
                    "值的數量與屬性數量不一致", $"型別={GetType().Name} 數量={values.Length}/{properties.Length}");
            }

            for (var index = 0; index < properties.Length; index++)
            {
                string context = $"{GetType().Name}.{properties[index].Name}";
                try
                {
                    object converted = ConvertValue(values[index], properties[index].PropertyType);
                    if (validateTokens)
                        ModelNaming.Token(context, converted);
                    properties[index].SetValue(this, converted);
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException)
                {
                    throw Logging.ErrorOnce(
                        new InvalidCastException($"無法轉型：{context}", ex),
                        "模型元素初始化失敗", null, context, values[index],
                        "轉型失敗", $"細節={ex.GetBaseException().Message}");
                }
            }
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;
            if (targetType == typeof(DateTime) && value is string text
                && DateTime.TryParseExact(text, ModelNaming.DateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime modelDate))
                return modelDate;
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        internal static void ValidateKeyToken(string context, string value)
            => ModelNaming.ValidateToken(context, value);

        private protected string[] KeyParts()
            => GetDimensions(GetType())
                .Select(property => ModelNaming.Token($"{GetType().Name}.{property.Name}", property.GetValue(this)))
                .ToArray();

        /// <summary>回傳類別名加維度值組成的完整名稱（如 <c>VariableB_Pick@A</c>），供變數、限制式查找與輸出使用。</summary>
        public override string ToString()
            => ModelNaming.Compose(GetType().Name, KeyParts());
    }

    /// <summary>一筆 Set 資料代表一組有效的維度值，不包含 QTY。</summary>
    public abstract class SetRowBase : ModelElementBase
    {
        // Set 名稱須與 BuildVars 的維度部分一致，否則查不到解。
        /// <summary>只用 @ 串接維度值，不含類別名。</summary>
        public override string ToString() => string.Join(KeySeparator, KeyParts());
    }

    /// <summary>一筆 Parameter 資料包含維度值，以及 Generator 產生的數值欄位 QTY。</summary>
    public abstract class ParameterBase : ModelElementBase { }

    /// <summary>Generator 產生的 Variable 類別基底；屬性就是維度，名稱由 <see cref="ModelElementBase.ToString"/> 組出。</summary>
    public abstract class VariableBase : ModelElementBase { }

    /// <summary>限制式類別基底。</summary>
    public abstract class ConstraintBase : ModelElementBase
    {
        /// <summary>限制式群組名，即類別名。</summary>
        protected string ConstraintName => GetType().Name;
    }
}
