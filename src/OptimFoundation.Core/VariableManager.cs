using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 以 Set 笛卡兒積組成 TypeName@維度值名稱，格式同 ModelElementBase.ToString()。
    /// </summary>
    public static class VariableManager
    {
        /// <summary>組合各 Set 的資料列，回傳每組維度值的字串陣列；完整名稱由呼叫端組成。</summary>
        private static IEnumerable<string[]> CombineRows(List<string[]>[] setRows)
        {
            IEnumerable<string[]> result = new[] { Array.Empty<string>() };
            foreach (var rows in setRows)
                result = result.SelectMany(_ => rows, (prefix, row) =>
                {
                    var next = new string[prefix.Length + row.Length];
                    prefix.CopyTo(next, 0);
                    row.CopyTo(next, prefix.Length);
                    return next;
                });
            return result;
        }

        private static List<string[]>[] ConvertSetsToRows(object[] sets)
        {
            if (sets == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(sets), "sets 不得為 null"),
                    "變數維度集合不合法", null, nameof(ConvertSetsToRows), null,
                    "集合陣列為空");

            if (sets.Length > 0 && sets.All(x => x is string))
                sets = [sets.Cast<string>().ToList()];

            var result = new List<string[]>[sets.Length];
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i] == null)
                    throw Logging.ErrorOnce(
                        new ArgumentException($"集合 #{i + 1} 不得為 null", nameof(sets)),
                        "變數維度集合不合法", null, nameof(ConvertSetsToRows), null,
                        "集合為空", $"序號={i + 1}");

                if (sets[i] is System.Collections.IEnumerable rowSequence)
                {
                    var setRows = rowSequence.Cast<object>().ToList();
                    if (setRows.All(row => row is SetRowBase))
                    {
                        result[i] = setRows.Select(row => row.GetType()
                            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                            .Select(property => ModelNaming.Token(
                                $"集合資料列 #{i + 1}.{property.Name}", property.GetValue(row)))
                            .ToArray()).ToList();
                        continue;
                    }
                }

                if (GetValueTupleElementType(sets[i]) != null)
                {
                    if (sets[i] is not System.Collections.IEnumerable sequence)
                        throw Logging.ErrorOnce(
                            new ArgumentException($"集合 #{i + 1} 必須可列舉"),
                            "變數維度集合不合法", null, nameof(ConvertSetsToRows), sets[i],
                            "集合無法列舉", $"序號={i + 1}");

                    result[i] = sequence.Cast<object>().Select(item =>
                    {
                        if (item is not ITuple tuple)
                            throw Logging.ErrorOnce(
                                new ArgumentException($"集合 #{i + 1} 含有非元組的成員"),
                                "變數維度集合不合法", null, nameof(ConvertSetsToRows), item,
                                "集合成員不是元組", $"序號={i + 1}");
                        return Enumerable.Range(0, tuple.Length)
                            .Select(index => ModelNaming.Token($"集合 #{i + 1} 成員 #{index + 1}", tuple[index]))
                            .ToArray();
                    }).ToList();
                }
                else
                {
                    result[i] = ConvertSetsToTokens(sets[i]).Single()
                        .Select(value => new[] { value }).ToList();
                }
            }
            return result;
        }

        private static Type GetValueTupleElementType(object value)
        {
            foreach (var type in value.GetType().GetInterfaces())
            {
                if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(IEnumerable<>)) continue;
                var elementType = type.GetGenericArguments()[0];
                if (elementType.IsValueType && elementType.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true)
                    return elementType;
            }
            return null;
        }

        /// <summary>
        /// 檢查各集合提供的維度總數是否等於 TVariable 的可寫 property 數；有空集合時略過檢查。
        /// </summary>
        /// <param name="setRows">各集合的資料列，每列以字串陣列保存維度值。</param>
        /// <typeparam name="TVariable">要建立的變數類別。</typeparam>
        private static void ValidateVariableArity<TVariable>(List<string[]>[] setRows)
        {
            if (setRows.Any(rows => rows.Count == 0)) return;
            int actual = setRows.Sum(rows =>
            {
                int width = rows[0].Length;
                if (rows.Any(row => row.Length != width))
                    throw Logging.ErrorOnce(
                        new ArgumentException("多維集合的每個資料列維度數量必須一致"),
                        "變數維度數量不一致", null, nameof(ValidateVariableArity), typeof(TVariable).Name,
                        "多維集合的資料列維度數量不一致");
                return width;
            });
            int expected = typeof(TVariable).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Count(property => property.CanWrite && property.GetIndexParameters().Length == 0
                    && property.DeclaringType != typeof(ModelElementBase)
                    && property.DeclaringType != typeof(VariableBase));
            if (actual != expected)
                throw Logging.ErrorOnce(
                    new ArgumentException($"BuildVars 維度數量不一致：{typeof(TVariable).Name} 傳入 {actual}，變數屬性 {expected}"),
                    "變數維度數量不一致", null, nameof(ValidateVariableArity), typeof(TVariable).Name,
                    "變數屬性數量不一致", $"數量={actual}/{expected}");
        }

        /// <summary>
        /// 將 DateTime、int、long、double、decimal、string 或 enum 的 IEnumerable&lt;T&gt; 轉成字串列表。
        /// 浮點值使用 InvariantCulture，enum 使用成員名稱。
        /// </summary>
        public static List<string>[] ConvertSetsToTokens(params object[] sets)
        {
            if (sets == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(sets), "sets 不得為 null"),
                    "變數維度集合不合法", null, nameof(ConvertSetsToTokens), null,
                    "集合陣列為空");

            // 單一 string[] 可能被展開成 params object[]；重新包裝以維持單一維度。
            if (sets.Length > 0 && sets.All(x => x is string))
                sets = [sets.Cast<string>().ToList()];

            var result = new List<string>[sets.Length];
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i] == null)
                    throw Logging.ErrorOnce(
                        new ArgumentException($"集合 #{i + 1} 不得為 null", nameof(sets)),
                        "變數維度集合不合法", null, nameof(ConvertSetsToTokens), null,
                        "集合為空", $"序號={i + 1}");

                result[i] = sets[i] switch
                {
                    IEnumerable<DateTime> seq => seq.Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    IEnumerable<int> seq => seq.Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    IEnumerable<long> seq => seq.Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    IEnumerable<double> seq => seq.Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    IEnumerable<decimal> seq => seq.Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    IEnumerable<string> seq => seq.Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    string s => throw Logging.ErrorOnce(
                        new ArgumentException($"集合不得為單一 string '{s}'：集合與裸 string 混傳，請確認每個參數都是一個集合（IEnumerable）"),
                        "變數維度集合不合法", null, nameof(ConvertSetsToTokens), s,
                        "字串不是集合", $"序號={i + 1}"),
                    // enum 是值型別，不能用 IEnumerable<Enum> 判斷任意 enum 集合，因此另外檢查集合的元素型別。
                    System.Collections.IEnumerable seq when GetEnumElementType(sets[i]) != null
                        => seq.Cast<object>().Select(value => ModelNaming.Token($"集合 #{i + 1}", value)).ToList(),
                    _ => throw Logging.ErrorOnce(
                        new ArgumentException($"不支援的集合型別：{sets[i].GetType().Name}，目前僅支援 IEnumerable<DateTime/int/long/double/decimal/string/enum>"),
                        "變數維度集合不合法", null, nameof(ConvertSetsToTokens), sets[i].GetType().FullName,
                        "不支援的集合型別", $"序號={i + 1}")
                };
            }
            return result;
        }

        /// <summary>若 obj 為元素型別是 enum 的 IEnumerable&lt;T&gt;，回傳該 enum 型別，否則回傳 null。</summary>
        private static Type GetEnumElementType(object obj)
        {
            foreach (var type in obj.GetType().GetInterfaces())
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    var elementType = type.GetGenericArguments()[0];
                    if (elementType.IsEnum) return elementType;
                }
            return null;
        }

        /// <summary>
        /// 直接組成 TypeName@維度值名稱，不建立實例或逐筆反射。
        /// </summary>
        public static IEnumerable<string> ComposeNames<TVariable>(object[] sets)
        {
            string typeName = typeof(TVariable).Name;
            var setRows = ConvertSetsToRows(sets);
            ValidateVariableArity<TVariable>(setRows);
            foreach (var parts in CombineRows(setRows))
                yield return ModelNaming.Compose(typeName, parts);
        }

        /// <summary>
        /// 以指定 typeName 組成名稱；無變數類別可對照，不檢查維度數。
        /// </summary>
        public static IEnumerable<string> ComposeNames(string typeName, object[] sets)
        {
            var setRows = ConvertSetsToRows(sets);
            foreach (var parts in CombineRows(setRows))
                yield return ModelNaming.Compose(typeName, parts);
        }
    }
}
