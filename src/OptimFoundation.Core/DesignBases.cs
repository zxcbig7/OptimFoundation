using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace OptimFoundation.Core
{
    public abstract class ModelElementBase
    {
        // PropertyInfo[] 快取：GetProperties() 是反射呼叫，每次 ~100ns，快取後降為字典查找 ~10ns
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propsCache
            = new ConcurrentDictionary<Type, PropertyInfo[]>();

        protected Type ElemType => GetType();
        protected string ElemName => ElemType.Name;

        private static PropertyInfo[] GetCachedProps(Type t)
            => _propsCache.GetOrAdd(t, type => type.GetProperties());

        protected ModelElementBase() { }

        protected ModelElementBase(params object[] sets)
        {
            InitClassBySets(sets);
        }

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

    public abstract class ConstraintBase : ModelElementBase
    {
        protected int ConstraintCount { get; set; }
        protected string ConstraintName => ElemName;
    }

    public abstract class ParameterBase : ModelElementBase
    {
        protected string ParameterName => ElemName;
    }

    public abstract class VariableBase : ModelElementBase
    {
        protected string VariableName => ElemName;
    }
}
