using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OptimFoundation.Core
{
    /// <summary>
    /// Set 積木的 marker interface。泛型 attribute（OptVar&lt;T&gt;/OptParam&lt;T&gt;）以
    /// <c>where T : ISetBrick</c> 約束型別參數，讓「引用非積木」變 CS0311 原生 compile error。
    /// </summary>
    public interface ISetBrick { }

    /// <summary>
    /// 索引集合積木基底：一個 Set 一顆積木，宣告名稱與元素型別。實作 <see cref="IEnumerable{T}"/>，
    /// 可直接餵進 <c>BuildBVs/BuildIVs/BuildCVs(params object[])</c>（經 VariableBuilder.ConvertSetsToStringLists）。
    /// 內部 List 保序（變數 key 組成順序穩定）+ HashSet 供 O(1) Contains。
    ///
    /// 四道防呆（全丟明確例外，錯誤左移到載入當下）：
    ///   1. 未載入就列舉 → InvalidOperationException（NEVER 空集合靜默解出退化解）
    ///   2. 載入後為空 → InvalidOperationException
    ///   3. 二次載入 → InvalidOperationException（載入即封存）
    ///   4. 重複成員 → ArgumentException（重複會生出重複變數 key）
    /// </summary>
    public abstract class SetBase<T> : ISetBrick, IReadOnlyList<T>
    {
        private readonly List<T> _items = new List<T>();
        private readonly HashSet<T> _index = new HashSet<T>();
        private bool _loaded;

        /// <summary>去 "Set_" 前綴的積木名（= 生成到 Var/Param 的 property 名 / CSV 欄名來源）。</summary>
        public string SetName
        {
            get
            {
                string n = GetType().Name;
                return n.StartsWith("Set_", StringComparison.Ordinal) ? n.Substring(4) : n;
            }
        }

        public int Count
        {
            get { EnsureLoaded(); return _items.Count; }
        }

        /// <summary>索引存取（保序）。積木即唯讀 List：支援 [i] / Count / foreach / LINQ，consumer 免另存 List 視圖。</summary>
        public T this[int index]
        {
            get { EnsureLoaded(); return _items[index]; }
        }

        public bool Contains(T item) => _index.Contains(item);

        /// <summary>
        /// 從名稱可定址來源載入（paved path，CSV / InMemory）：set 名預設 = SetName（類名去 Set_ 前綴），
        /// name 可就地覆寫（檔名形式 Set_{X}）。字串自動依 T 轉型（見 ParseElement）。
        /// DB set 因需明寫 SQL，改用 LoadFrom(db.LoadSet("SELECT ..."))。
        /// </summary>
        public void Load(IO.IDataSource source, string name = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            LoadFrom(source.LoadSet(name ?? SetName).Select(ParseElement));
        }

        /// <summary>
        /// 就地載入：把「一個個列出來的字面值」當成員。用於手寫死的小集合、測試資料、範例。
        /// <code>
        /// EMPLOYEE.LoadInline("Alice", "Bob", "Carol");
        /// DATE.LoadInline(new DateTime(2026, 8, 1), new DateTime(2026, 8, 2));
        /// SCOPE.LoadInline("Total");   // 單一固定成員
        /// </code>
        /// </summary>
        public void LoadInline(params T[] items) => LoadFrom(items);

        /// <summary>
        /// 從「已有的序列 / 程式生成的集合」載入（loop、LINQ、Enum、List）。
        /// <code>
        /// GROUP.LoadFrom(Enum.GetNames&lt;GroupE&gt;());                         // 從 enum
        /// EMPLOYEE.LoadFrom(Enumerable.Range(1, n).Select(i =&gt; $"E{i}"));    // 從迴圈
        /// DATE.LoadFrom(someDateList);                                        // 從既有 List
        /// </code>
        /// 判斷：手打字面值 → LoadInline；已有一坨/生成的 → LoadFrom；讀檔 → LoadCsv。
        /// </summary>
        public void LoadFrom(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (_loaded)
                throw new InvalidOperationException($"【{GetType().Name}】已載入，禁止二次載入（載入即封存）。");

            foreach (var it in items)
            {
                if (!_index.Add(it))
                    throw new ArgumentException($"【{GetType().Name}】重複成員 '{it}'——重複會生出重複變數 key。");
                _items.Add(it);
            }

            _loaded = true;
            if (_items.Count == 0)
                throw new InvalidOperationException($"【{GetType().Name}】載入後為空集合——空 set 會讓模型退化，請確認資料來源。");
        }

        /// <summary>
        /// 從 CSV 載入（`Data/{fileName}.csv`，一欄一列、無表頭）。字串自動依 T 轉型（見 ParseElement）——CSV 專案的預設寫法。
        /// <code>
        /// LOT.LoadCsv("Set_Lot");     // string set：原樣
        /// DATE.LoadCsv("Set_Date");   // DateTime set：字串自動 parse 成 DateTime
        /// </code>
        /// </summary>
        public void LoadCsv(string fileName)
        {
            var raw = IO.CsvCtrl.ReadStrSet(fileName);
            LoadFrom(raw.Select(ParseElement));
        }

        private void EnsureLoaded()
        {
            if (!_loaded)
                throw new InvalidOperationException($"【{GetType().Name}】尚未載入就被使用——請先呼叫 LoadInline/LoadFrom/LoadCsv/Load。");
        }

        // string → T：支援 string/DateTime/int/long/double/decimal（與 VariableBuilder 支援域一致）。
        private static T ParseElement(string s)
        {
            var t = typeof(T);
            object v;
            if (t == typeof(string)) v = s;
            else if (t == typeof(DateTime)) v = DateTime.Parse(s, CultureInfo.InvariantCulture);
            else if (t == typeof(int)) v = int.Parse(s, CultureInfo.InvariantCulture);
            else if (t == typeof(long)) v = long.Parse(s, CultureInfo.InvariantCulture);
            else if (t == typeof(double)) v = double.Parse(s, CultureInfo.InvariantCulture);
            else if (t == typeof(decimal)) v = decimal.Parse(s, CultureInfo.InvariantCulture);
            else throw new NotSupportedException($"SetBase<{t.Name}> 不支援從字串解析；請改用 LoadInline/LoadFrom。");
            return (T)v;
        }

        public IEnumerator<T> GetEnumerator()
        {
            EnsureLoaded();
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
