using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace OptimFoundation.Core
{
    /// <summary>Generator 產生的 Set、Parameter 與 Variable 資料列共用的基底類別，提供欄位初始化與名稱組合。</summary>
    public abstract class ModelElementBase
    {
        internal const char KeySeparator = ModelNaming.Separator;
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Props = new();

        private static PropertyInfo[] GetProps(Type type) => Props.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
             .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
             .ToArray());

        /// <summary>依 public 可寫屬性的宣告順序轉型並填入維度值，同時檢查命名 token；數量不符、無法轉型或 token 不合法都會拋例外。</summary>
        public void InitClassBySets(params object[] values) => Init(values, validateTokens: true);

        // 值欄位不參與命名；維度命名檢查交由 DataContext 記警告。
        internal void InitFromDataRow(object[] values) => Init(values, validateTokens: false);

        private void Init(object[] values, bool validateTokens)
        {
            var properties = GetProps(GetType());
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
            => GetProps(GetType())
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
