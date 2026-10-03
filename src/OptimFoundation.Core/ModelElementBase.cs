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

        // 載入資料時先轉換欄位值；只有維度欄位會組成名稱，命名檢查交由 DataContext 記錄警告，
        // 值欄位（QTY 等）本來就不進名稱，負數或科學記號不該被當成命名錯誤擋下
        internal void InitFromDataRow(object[] values) => Init(values, validateTokens: false);

        private void Init(object[] values, bool validateTokens)
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
                    object converted = ConvertValue(values[index], properties[index].PropertyType);
                    if (validateTokens)
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
        // Set 只用 @ 串接維度值，供限制式命名與查解。加上類別名會與 BuildVars 的名稱不符，
        // 導致 TryGetValue 查不到；呼叫端若將缺值當成 0，便會誤讀解答。
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
