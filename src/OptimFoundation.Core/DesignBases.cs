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

        /// <summary>組出唯一 key：TypeName@val1@val2@…（DateTime 固定 yyyy-MM-dd）。全框架以此字串索引變數。</summary>
        public override string ToString()
        {
            var props = GetCachedProps(ElemType);
            var sb = new StringBuilder(ElemName);
            foreach (var p in props)
            {
                sb.Append('@');
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
        /// <summary>已廢棄：限制式計數改由 EngineBase 自動統計（見 ConstraintBuildCounts）。</summary>
        [Obsolete("Constraint counts are tracked automatically by EngineBase.")]
        protected int ConstraintCount { get; set; }

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
}
