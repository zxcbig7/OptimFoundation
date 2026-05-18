using System;
using System.Collections.Generic;
using System.Linq;

namespace OptimFoundation.Core
{
    public static class VariableBuilder
    {
        /// <summary>
        /// 從多個 Set 組合出所有變數名稱（格式：TypeName@set1@set2@...）
        /// </summary>
        public static IEnumerable<string> GenVarCombinations(params List<string>[] lists)
        {
            IEnumerable<string> result = new[] { "" };
            foreach (var list in lists)
                result = result.SelectMany(_ => list, (prefix, item) => $"{prefix}@{item}");
            return result;
        }
        /// <summary>
        /// 將多個 Set 轉換為字串列表，以便生成變數名稱；支援 List<DateTime>, List<int>, List<double>, List<string>
        /// </summary>
        /// <param name="lists">要轉換的 Set 列表</param>
        /// <returns>轉換後的字串Array</returns>
        public static List<string>[] ConvertSetsToStringLists(params object[] lists)
        {
            var result = new List<string>[lists.Length];
            for (int i = 0; i < lists.Length; i++)
            {
                result[i] = lists[i] switch
                {
                    List<DateTime> dtList => dtList.Select(d => d.ToString("yyyy-MM-dd")).ToList(),
                    List<int> intList => intList.Select(n => n.ToString()).ToList(),
                    List<double> doubleList => doubleList.Select(n => n.ToString("0.##########")).ToList(), // 避免科學記號(支援小數點後10位數)
                    List<string> stringList => stringList.ToList(),
                    _ => throw new ArgumentException($"Unsupported set type: {lists[i].GetType()}")
                };
            }
            return result;
        }

        public static void BuildVars<T>(Action<object> createVarMethod, object[] sets)
        {
            var stringLists = ConvertSetsToStringLists(sets);
            foreach (var combo in GenVarCombinations(stringLists))
            {
                string[] parts = combo.Split('@').Skip(1).ToArray();
                object instance = Activator.CreateInstance(typeof(T), new object[] { parts });
                createVarMethod(instance);
            }
        }
    }
}
