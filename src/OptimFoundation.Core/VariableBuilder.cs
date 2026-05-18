using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace OptimFoundation.Core
{
    public static class VariableBuilder
    {
        private static readonly ConcurrentDictionary<Type, Func<string[], object>> _ctorCache
            = new ConcurrentDictionary<Type, Func<string[], object>>();

        private static Func<string[], object> GetCtor(Type t) => _ctorCache.GetOrAdd(t, ty =>
        {
            // 優先使用無參數建構子（開發者不需要寫任何建構子）
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

            // params object[] 建構子（最常見的舊寫法：public Foo(params object[] Sets) : base(Sets){}）
            var objArrCtor = ty.GetConstructor([typeof(object[])]);
            if (objArrCtor != null)
            {
                var p2 = Expression.Parameter(typeof(string[]), "parts");
                return Expression.Lambda<Func<string[], object>>(
                    Expression.New(objArrCtor, Expression.Convert(p2, typeof(object[]))),
                    p2).Compile();
            }

            // 向下相容：沿用舊的 string[] 建構子寫法
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

        /// <summary>將多個 Set 轉換為字串列表；支援 List&lt;DateTime&gt;、List&lt;int&gt;、List&lt;double&gt;、List&lt;string&gt;</summary>
        public static List<string>[] ConvertSetsToStringLists(params object[] lists)
        {
            var result = new List<string>[lists.Length];
            for (int i = 0; i < lists.Length; i++)
            {
                result[i] = lists[i] switch
                {
                    List<DateTime> dtList     => dtList.Select(d => d.ToString("yyyy-MM-dd")).ToList(),
                    List<int>      intList    => intList.Select(n => n.ToString()).ToList(),
                    List<double>   doubleList => doubleList.Select(n => n.ToString("0.##########")).ToList(),
                    List<string>   stringList => stringList.ToList(),
                    _ => throw new ArgumentException($"Unsupported set type: {lists[i].GetType()}")
                };
            }
            return result;
        }

        /// <summary>產生所有變數名稱（使用 compiled ctor，比 Activator.CreateInstance 快 10–100x）</summary>
        public static IEnumerable<string> GetVarNames<T>(object[] sets)
        {
            var create = GetCtor(typeof(T));
            var stringLists = ConvertSetsToStringLists(sets);
            foreach (var parts in GenVarParts(stringLists))
                yield return create(parts).ToString();
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
