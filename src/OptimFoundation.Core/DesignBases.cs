using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 變數 / 參數 / 限制式的共同基底：只宣告 properties、不寫建構子，靠反射把 set 值依「property 宣告順序」填入。
    /// 每個 element 的唯一 key 由 <see cref="ToString"/> 組成（TypeName@val1@val2@…），全框架以此字串索引變數。
    /// </summary>
    public abstract class ModelElementBase
    {
        /// <summary>變數 key 的維度分隔符。資料值不得含此字元，見 <see cref="ValidateKeyToken"/>。</summary>
        internal const char KeySeparator = '@';

        // PropertyInfo[] 快取：GetProperties() 是反射呼叫，每次 ~100ns，快取後降為字典查找 ~10ns
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propsCache
            = new ConcurrentDictionary<Type, PropertyInfo[]>();

        /// <summary>實際子類別的型別（用它取 property 清單，不是 base 的型別）。</summary>
        protected Type ElemType => GetType();

        /// <summary>實際子類別的名稱，也是變數 key 的前綴與 VariableSets 的分組 key。</summary>
        protected string ElemName => ElemType.Name;

        private static PropertyInfo[] GetCachedProps(Type t)
            => _propsCache.GetOrAdd(t, type => type.GetProperties());

        /// <summary>空建構：各 property 以物件初始式個別指定（查變數時最常用的寫法）。</summary>
        protected ModelElementBase() { }

        /// <summary>依 property 宣告順序填值的建構；等同 new + <see cref="InitClassBySets"/>。</summary>
        protected ModelElementBase(params object[] sets)
        {
            InitClassBySets(sets);
        }

        /// <summary>
        /// 依「property 宣告順序」把 sets 的值逐一填進本物件；型別不符時嘗試 Convert.ChangeType。
        /// 參數個數或型別對不上即丟例外——順序錯是最常見的 bug 來源。
        /// </summary>
        public void InitClassBySets(params object[] sets)
        {
            var props = GetCachedProps(ElemType);
            if (sets.Length != props.Length)
                throw new ArgumentException($"【{ElemName}】期望 {props.Length} 個參數，收到 {sets.Length} 個。");

            for (int i = 0; i < props.Length; i++)
            {
                var targetType = props[i].PropertyType;
                var inputValue = sets[i];

                if (inputValue is string raw)
                    ValidateKeyToken($"{ElemName}.{props[i].Name}", raw);

                if (targetType == inputValue?.GetType())
                {
                    props[i].SetValue(this, inputValue);
                }
                else
                {
                    try { props[i].SetValue(this, Convert.ChangeType(inputValue, targetType)); }
                    catch { throw new InvalidCastException($"【{ElemName}】第 {i + 1} 個參數型別不符，期望 {targetType}，收到 {inputValue?.GetType()}。"); }
                }
            }
        }

        /// <summary>
        /// 資料值含 <see cref="KeySeparator"/> 即丟例外。
        /// Why: 分隔符沒有 escape，值裡混進它會讓不同 element 組出同一把 key（變數被覆寫、同名限制式被當重複略過），
        /// 且 WriteSolution / SaveToDB 是靠 Split 把 key 拆回各維度欄，多切一刀就整列錯位——兩者都不會丟例外。
        /// </summary>
        internal static void ValidateKeyToken(string context, string value)
        {
            if (value != null && value.Contains(KeySeparator))
                throw new ArgumentException(
                    $"【{context}】值 '{value}' 含保留字元 '{KeySeparator}'——它是變數 key（TypeName{KeySeparator}v1{KeySeparator}v2…）的維度分隔符，資料不得使用。");
        }

        /// <summary>組出唯一 key：TypeName@val1@val2@…（DateTime 固定 yyyy-MM-dd）。全框架以此字串索引變數。</summary>
        public override string ToString()
        {
            var props = GetCachedProps(ElemType);
            var sb = new StringBuilder(ElemName);
            foreach (var p in props)
            {
                // 先加 @ 再加值，避免空字串或 null 造成的 key 重複
                sb.Append(KeySeparator);
                if (p.PropertyType == typeof(DateTime))
                    sb.Append(((DateTime)p.GetValue(this)).ToString("yyyy-MM-dd"));
                else
                    sb.Append(p.GetValue(this));
            }
            return sb.ToString();
        }
    }

    /// <summary>限制式基底（前綴慣例 Constraint_）。ConstraintName = 類別名，供組限制式名稱用。</summary>
    public abstract class ConstraintBase : ModelElementBase
    {
        /// <summary>限制式名稱 = 類別名；建限制式時常用它當名稱前綴再串索引。</summary>
        protected string ConstraintName => ElemName;
    }

    /// <summary>參數基底（前綴慣例 Parameter_，值放 QTY 欄）。</summary>
    public abstract class ParameterBase : ModelElementBase
    {
        /// <summary>參數名稱 = 類別名。</summary>
        protected string ParameterName => ElemName;
    }

    /// <summary>變數基底（前綴慣例 VariableB_/X_/I_ 對應 Binary/Continuous/Integer）。</summary>
    public abstract class VariableBase : ModelElementBase
    {
        /// <summary>變數名稱 = 類別名；前綴決定變數型別（見 BuildVars 的命名天條）。</summary>
        protected string VariableName => ElemName;
    }


    /// <summary>
    /// Set 積木的 marker interface。泛型 attribute（OptVar&lt;T&gt;/OptParam&lt;T&gt;）以
    /// <c>where T : ISetBrick</c> 約束型別參數，讓「引用非積木」變 CS0311 原生 compile error。
    /// 另兼型別化萃取器的非泛型讀值面：供驗證器在零 runtime reflection 前提下讀 index 值
    /// （見框架資料防護規格——型別安全的邊界只在這裡做一次型別抹除，之後全走 object[]）。
    /// </summary>
    public interface ISetBrick
    {
        /// <summary>成員數；尚未載入就存取會丟 InvalidOperationException。</summary>
        int Count { get; }

        /// <summary>本集合的元素型別（SetBase&lt;T&gt; 的 T）。供驗證器辨別 dangling 與 type mismatch（見框架資料防護規格）。</summary>
        Type ElementType { get; }

        /// <summary>value 是否為本集合成員（先型別檢查再比對，非本集合元素型別一律回 false）。</summary>
        bool ContainsObject(object value);

        /// <summary>逐一 box 回傳成員（保序），供驗證器組 dangling 檢查用的比對來源。</summary>
        IEnumerable<object> MembersAsObjects();
    }

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

        /// <summary>成員數。尚未載入就存取會丟 InvalidOperationException（防止用到空 set 而不自知）。</summary>
        public int Count
        {
            get { EnsureLoaded(); return _items.Count; }
        }

        /// <summary>成員的 CLR 型別（string / DateTime / int / long / double / decimal）。</summary>
        public Type ElementType => typeof(T);

        /// <summary>索引存取（保序）。積木即唯讀 List：支援 [i] / Count / foreach / LINQ，consumer 免另存 List 視圖。</summary>
        public T this[int index]
        {
            get { EnsureLoaded(); return _items[index]; }
        }

        /// <summary>是否含此成員（HashSet 查找，O(1)）。未載入時回 false 而不丟例外。</summary>
        public bool Contains(T item) => _index.Contains(item);

        /// <summary>型別抹除版的 <see cref="Contains"/>：型別不符直接回 false。供驗證器對 object 索引值檢查用。</summary>
        public bool ContainsObject(object value) => value is T t && Contains(t);

        /// <summary>依載入順序列舉全部成員（box 成 object）。供驗證器 / 摘要等不知道 T 的地方使用。</summary>
        public IEnumerable<object> MembersAsObjects()
        {
            EnsureLoaded();
            foreach (var it in _items)
                yield return it!;
        }

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

        /// <summary>依載入順序列舉成員（保序，可直接 foreach / LINQ）。未載入即丟例外。</summary>
        public IEnumerator<T> GetEnumerator()
        {
            EnsureLoaded();
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
