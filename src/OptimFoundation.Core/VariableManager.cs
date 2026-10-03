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
                    new ArgumentNullException(nameof(sets)),
                    "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToRows), null,
                    "sets_array_is_null");

            if (sets.Length > 0 && sets.All(x => x is string))
                sets = [sets.Cast<string>().ToList()];

            var result = new List<string[]>[sets.Length];
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i] == null)
                    throw Logging.ErrorOnce(
                        new ArgumentException($"Set #{i + 1} cannot be null.", nameof(sets)),
                        "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToRows), null,
                        "set_is_null", $"index={i + 1}");

                if (sets[i] is System.Collections.IEnumerable rowSequence)
                {
                    var setRows = rowSequence.Cast<object>().ToList();
                    if (setRows.All(row => row is SetRowBase))
                    {
                        result[i] = setRows.Select(row => row.GetType()
                            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                            .Select(property => ModelNaming.Token(
                                $"Set row #{i + 1}.{property.Name}", property.GetValue(row)))
                            .ToArray()).ToList();
                        continue;
                    }
                }

                if (GetValueTupleElementType(sets[i]) != null)
                {
                    if (sets[i] is not System.Collections.IEnumerable sequence)
                        throw Logging.ErrorOnce(
                            new ArgumentException($"Set #{i + 1} must be enumerable."),
                            "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToRows), sets[i],
                            "set_not_enumerable", $"index={i + 1}");

                    result[i] = sequence.Cast<object>().Select(item =>
                    {
                        if (item is not ITuple tuple)
                            throw Logging.ErrorOnce(
                                new ArgumentException($"Set #{i + 1} contains a non-tuple member."),
                                "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToRows), item,
                                "non_tuple_member", $"index={i + 1}");
                        return Enumerable.Range(0, tuple.Length)
                            .Select(index => ModelNaming.Token($"Set #{i + 1} member #{index + 1}", tuple[index]))
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
                        new ArgumentException("Each row in a multidimensional set must have the same arity."),
                        "VARIABLE_ARITY_MISMATCH", "變數維度數量不一致", nameof(ValidateVariableArity), typeof(TVariable).Name,
                        "multidimensional_rows_have_different_arity");
                return width;
            });
            int expected = typeof(TVariable).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Count(property => property.CanWrite && property.GetIndexParameters().Length == 0
                    && property.DeclaringType != typeof(ModelElementBase)
                    && property.DeclaringType != typeof(VariableBase));
            if (actual != expected)
                throw Logging.ErrorOnce(
                    new ArgumentException($"BuildVars arity mismatch for {typeof(TVariable).Name}: supplied {actual}, variable properties {expected}."),
                    "VARIABLE_ARITY_MISMATCH", "變數維度數量不一致", nameof(ValidateVariableArity), typeof(TVariable).Name,
                    "variable_property_count_mismatch", $"actual={actual} expected={expected}");
        }

        /// <summary>
        /// 將 DateTime、int、long、double、decimal、string 或 enum 的 IEnumerable&lt;T&gt; 轉成字串列表。
        /// 浮點值使用 InvariantCulture，enum 使用成員名稱。
        /// </summary>
        public static List<string>[] ConvertSetsToTokens(params object[] sets)
        {
            if (sets == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(sets)),
                    "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToTokens), null,
                    "sets_array_is_null");

            // 單一 string[] 可能被展開成 params object[]；重新包裝以維持單一維度。
            if (sets.Length > 0 && sets.All(x => x is string))
                sets = [sets.Cast<string>().ToList()];

            var result = new List<string>[sets.Length];
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i] == null)
                    throw Logging.ErrorOnce(
                        new ArgumentException($"Set #{i + 1} cannot be null.", nameof(sets)),
                        "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToTokens), null,
                        "set_is_null", $"index={i + 1}");

                result[i] = sets[i] switch
                {
                    IEnumerable<DateTime> seq => seq.Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    IEnumerable<int> seq => seq.Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    IEnumerable<long> seq => seq.Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    IEnumerable<double> seq => seq.Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    IEnumerable<decimal> seq => seq.Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    IEnumerable<string> seq => seq.Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    string s => throw Logging.ErrorOnce(
                        new ArgumentException($"Set 不可為單一 string '{s}'——集合與裸 string 混傳，請確認每個參數都是一個 Set（IEnumerable）。"),
                        "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToTokens), s,
                        "bare_string_is_not_a_set", $"index={i + 1}"),
                    // enum 是值型別，不能用 IEnumerable<Enum> 判斷任意 enum 集合，因此另外檢查集合的元素型別。
                    System.Collections.IEnumerable seq when GetEnumElementType(sets[i]) != null
                        => seq.Cast<object>().Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    _ => throw Logging.ErrorOnce(
                        new ArgumentException($"不支援的 Set 型別：{sets[i].GetType().Name}。目前僅支援 IEnumerable<DateTime/int/long/double/decimal/string/enum>。"),
                        "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToTokens), sets[i].GetType().FullName,
                        "unsupported_set_type", $"index={i + 1}")
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
