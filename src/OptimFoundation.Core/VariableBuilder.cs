using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 變數名稱工具：把多個 Set 做笛卡兒積，組出變數 key（TypeName@v1@v2@…），與 ModelElementBase.ToString() 格式一致。
    /// 直接組字串、不建 element 實例（比反射快 10x+）；建構子以編譯後 lambda 快取（compiled ctor cache）。
    /// </summary>
    public static class VariableBuilder
    {
        // 型別 → 編譯後建構委派 的快取（無參 / object[] / string[] 三種建構子擇一），避免每次反射
        private static readonly ConcurrentDictionary<Type, Func<string[], object>> _ctorCache
            = new ConcurrentDictionary<Type, Func<string[], object>>();

        // 保留供 BuildVars<TVariable> 使用
        private static Func<string[], object> GetCtor(Type t) => _ctorCache.GetOrAdd(t, ty =>
        {
            var defaultCtor = ty.GetConstructor(Type.EmptyTypes);
            if (defaultCtor != null)
            {
                var compiledNew = Expression.Lambda<Func<object>>(Expression.New(defaultCtor)).Compile();
                return parts =>
                {
                    var obj = (ModelElementBase)compiledNew();
                    obj.InitClassBySets(parts);
                    return obj;
                };
            }

            var objArrCtor = ty.GetConstructor([typeof(object[])]);
            if (objArrCtor != null)
            {
                var p2 = Expression.Parameter(typeof(string[]), "parts");
                return Expression.Lambda<Func<string[], object>>(
                    Expression.New(objArrCtor, Expression.Convert(p2, typeof(object[]))),
                    p2).Compile();
            }

            var stringArrCtor = ty.GetConstructor([typeof(string[])])
                ?? throw Logging.ErrorOnce(
                    new InvalidOperationException($"{ty.Name} 缺少可用的建構子（無參數、object[]、string[] 三者皆無）。"),
                    "VARIABLE_CONSTRUCTOR_MISSING", "變數建構子不存在", nameof(GetCtor), ty.FullName,
                    "supported_constructor_not_found");
            var param = Expression.Parameter(typeof(string[]), "parts");
            return Expression.Lambda<Func<string[], object>>(Expression.New(stringArrCtor, param), param).Compile();
        });

        // 0 維（scalar）→ 回空陣列；≥1 維 → 回每個組合的 string[]，供 GetVarNames/BuildVars 組 key
        private static IEnumerable<string[]> GenVarParts(List<string>[] lists)
        {
            IEnumerable<string[]> result = new[] { Array.Empty<string>() };
            foreach (var list in lists)
                result = result.SelectMany(_ => list, (prefix, item) =>
                {
                    var next = new string[prefix.Length + 1];
                    prefix.CopyTo(next, 0);
                    next[prefix.Length] = item;
                    return next;
                });
            return result;
        }

        /// <summary>從多個 Set 組合出所有變數名稱（格式：TypeName@set1@set2@...）</summary>
        private static IEnumerable<string[]> GenVarParts(List<string[]>[] domains)
        {
            IEnumerable<string[]> result = new[] { Array.Empty<string>() };
            foreach (var domain in domains)
                result = result.SelectMany(_ => domain, (prefix, row) =>
                {
                    var next = new string[prefix.Length + row.Length];
                    prefix.CopyTo(next, 0);
                    row.CopyTo(next, prefix.Length);
                    return next;
                });
            return result;
        }

        private static List<string[]>[] ConvertSetsToVarPartLists(object[] sets)
        {
            if (sets == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(sets)),
                    "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToVarPartLists), null,
                    "sets_array_is_null");

            if (sets.Length > 0 && sets.All(x => x is string))
                sets = [sets.Cast<string>().ToList()];

            var result = new List<string[]>[sets.Length];
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i] == null)
                    throw Logging.ErrorOnce(
                        new ArgumentException($"Set #{i + 1} cannot be null.", nameof(sets)),
                        "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToVarPartLists), null,
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
                            "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToVarPartLists), sets[i],
                            "set_not_enumerable", $"index={i + 1}");

                    result[i] = sequence.Cast<object>().Select(item =>
                    {
                        if (item is not ITuple tuple)
                            throw Logging.ErrorOnce(
                                new ArgumentException($"Set #{i + 1} contains a non-tuple member."),
                                "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToVarPartLists), item,
                                "non_tuple_member", $"index={i + 1}");
                        return Enumerable.Range(0, tuple.Length)
                            .Select(index => ModelNaming.Token($"Set #{i + 1} member #{index + 1}", tuple[index]))
                            .ToArray();
                    }).ToList();
                }
                else
                {
                    result[i] = ConvertSetsToStringLists(sets[i]).Single()
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

        private static void ValidateVariableArity<TVariable>(List<string[]>[] domains)
        {
            if (domains.Any(domain => domain.Count == 0)) return;
            int actual = domains.Sum(domain =>
            {
                int width = domain[0].Length;
                if (domain.Any(row => row.Length != width))
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

        public static IEnumerable<string> GenVarCombinations(params List<string>[] lists)
        {
            // 0 維（scalar 變數）：無 index，回空字串（呼叫端組出 TypeName，與 ModelElementBase.ToString 一致，不留 trailing @）
            foreach (var parts in GenVarParts(lists))
                yield return parts.Length == 0
                    ? string.Empty
                    : ModelNaming.Separator + string.Join(ModelNaming.Separator, parts);
        }

        /// <summary>
        /// 將多個 Set 轉換為字串列表。
        /// 支援 List&lt;T&gt;、T[] 及任何 IEnumerable&lt;T&gt;，T 可為 DateTime、int、long、double、decimal、string 或 enum。
        /// 整數型用 ToString()；浮點型（double/decimal）用 InvariantCulture，確保與 ModelElementBase.ToString() 的格式一致；
        /// enum 以成員名稱（ToString()）作為 Set 成員字串。
        /// </summary>
        public static List<string>[] ConvertSetsToStringLists(params object[] lists)
        {
            if (lists == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(lists)),
                    "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToStringLists), null,
                    "sets_array_is_null");

            // 單獨傳一個 string[] 時，C# 陣列共變會把它直接 bind 成 params object[] 本身，
            // 元素散成一條條 string；裸 string 不是合法 set，全為 string 必為此誤 bind，還原成單一 set
            if (lists.Length > 0 && lists.All(x => x is string))
                lists = [lists.Cast<string>().ToList()];

            var result = new List<string>[lists.Length];
            for (int i = 0; i < lists.Length; i++)
            {
                if (lists[i] == null)
                    throw Logging.ErrorOnce(
                        new ArgumentException($"Set #{i + 1} cannot be null.", nameof(lists)),
                        "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToStringLists), null,
                        "set_is_null", $"index={i + 1}");

                result[i] = lists[i] switch
                {
                    IEnumerable<DateTime> seq => seq.Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    IEnumerable<int> seq => seq.Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    IEnumerable<long> seq => seq.Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    IEnumerable<double> seq => seq.Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    IEnumerable<decimal> seq => seq.Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    IEnumerable<string> seq => seq.Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    string s => throw Logging.ErrorOnce(
                        new ArgumentException($"Set 不可為單一 string '{s}'——集合與裸 string 混傳，請確認每個參數都是一個 Set（IEnumerable）。"),
                        "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToStringLists), s,
                        "bare_string_is_not_a_set", $"index={i + 1}"),
                    // enum 為 value type，無法靠 IEnumerable<Enum> 共變比對，改用非泛型 IEnumerable + 元素型別偵測
                    System.Collections.IEnumerable seq when GetEnumElementType(lists[i]) != null
                        => seq.Cast<object>().Select(value => ModelNaming.Token($"Set #{i + 1}", value)).ToList(),
                    _ => throw Logging.ErrorOnce(
                        new ArgumentException($"不支援的 Set 型別：{lists[i].GetType().Name}。目前僅支援 IEnumerable<DateTime/int/long/double/decimal/string/enum>。"),
                        "VARIABLE_SET_INVALID", "變數維度集合不合法", nameof(ConvertSetsToStringLists), lists[i].GetType().FullName,
                        "unsupported_set_type", $"index={i + 1}")
                };
            }
            return result;
        }

        /// <summary>若 obj 為元素型別是 enum 的 IEnumerable&lt;T&gt;，回傳該 enum 型別，否則回傳 null。</summary>
        private static Type GetEnumElementType(object obj)
        {
            foreach (var it in obj.GetType().GetInterfaces())
                if (it.IsGenericType && it.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    var elem = it.GetGenericArguments()[0];
                    if (elem.IsEnum) return elem;
                }
            return null;
        }

        /// <summary>
        /// 產生所有變數名稱（格式：TypeName@val1@val2@...）。
        /// 直接組合字串，不建立 TVariable 的實例，避免每個名稱都做 InitClassBySets + ToString 的反射。
        /// </summary>
        public static IEnumerable<string> GetVarNames<TVariable>(object[] sets)
        {
            string typeName = typeof(TVariable).Name;
            var domains = ConvertSetsToVarPartLists(sets);
            ValidateVariableArity<TVariable>(domains);
            // 0 維（scalar）→ 純 TypeName（與 ModelElementBase.ToString 一致）；≥1 維 → TypeName@v1@v2...
            foreach (var parts in GenVarParts(domains))
                yield return ModelNaming.Compose(typeName, parts);
        }

        /// <summary>以逐筆 callback 建立變數。</summary>
        public static void BuildVars<TVariable>(Action<object> createVarMethod, object[] sets)
        {
            var create = GetCtor(typeof(TVariable));
            var domains = ConvertSetsToVarPartLists(sets);
            ValidateVariableArity<TVariable>(domains);
            foreach (var parts in GenVarParts(domains))
                createVarMethod(create(parts));
        }
    }
}
