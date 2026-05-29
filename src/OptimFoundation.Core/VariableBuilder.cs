using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;

namespace OptimFoundation.Core
{
    public static class VariableBuilder
    {
        private static readonly ConcurrentDictionary<Type, Func<string[], object>> _ctorCache
            = new ConcurrentDictionary<Type, Func<string[], object>>();

        // 保留供 BuildVars<T> 使用
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
                ?? throw new InvalidOperationException(
                    $"{ty.Name} 缺少可用的建構子（無參數、object[]、string[] 三者皆無）。");
            var param = Expression.Parameter(typeof(string[]), "parts");
            return Expression.Lambda<Func<string[], object>>(Expression.New(stringArrCtor, param), param).Compile();
        });

        // yields string[] parts directly — no string concatenation or Split overhead
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
        public static IEnumerable<string> GenVarCombinations(params List<string>[] lists)
        {
            foreach (var parts in GenVarParts(lists))
                yield return "@" + string.Join("@", parts);
        }

        /// <summary>
        /// 將多個 Set 轉換為字串列表。
        /// 支援 List&lt;T&gt;、T[] 及任何 IEnumerable&lt;T&gt;，T 可為 DateTime、int、double、string。
        /// double 使用 InvariantCulture，確保與 ModelElementBase.ToString() 的格式一致。
        /// </summary>
        public static List<string>[] ConvertSetsToStringLists(params object[] lists)
        {
            var result = new List<string>[lists.Length];
            for (int i = 0; i < lists.Length; i++)
            {
                result[i] = lists[i] switch
                {
                    IEnumerable<DateTime> seq => seq.Select(d => d.ToString("yyyy-MM-dd")).ToList(),
                    IEnumerable<int>      seq => seq.Select(n => n.ToString()).ToList(),
                    IEnumerable<double>   seq => seq.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList(),
                    IEnumerable<string>   seq => seq.ToList(),
                    _ => throw new ArgumentException($"不支援的 Set 型別：{lists[i].GetType().Name}。支援 IEnumerable<DateTime/int/double/string>。")
                };
            }
            return result;
        }

        /// <summary>
        /// 產生所有變數名稱（格式：TypeName@val1@val2@...）。
        /// 直接組合字串，不建立 T 的實例，效能比舊版快 10x 以上（舊版每個名稱做 InitClassBySets + ToString 的反射）。
        /// </summary>
        public static IEnumerable<string> GetVarNames<T>(object[] sets)
        {
            string typeName = typeof(T).Name;
            var stringLists = ConvertSetsToStringLists(sets);
            foreach (var parts in GenVarParts(stringLists))
                yield return typeName + "@" + string.Join("@", parts);
        }

        /// <summary>建立變數（保留給需要逐筆回呼的舊有用法）</summary>
        public static void BuildVars<T>(Action<object> createVarMethod, object[] sets)
        {
            var create = GetCtor(typeof(T));
            var stringLists = ConvertSetsToStringLists(sets);
            foreach (var parts in GenVarParts(stringLists))
                createVarMethod(create(parts));
        }
    }
}
