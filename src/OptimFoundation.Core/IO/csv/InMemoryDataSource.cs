using System;
using System.Collections.Generic;
using System.Linq;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 記憶體資料來源：demo / 單元測試 / 程式生成實例用。
    /// 以 AddParameters / AddSet 流暢註冊，Dataload 端與 CSV / DB 來源同一介面讀取。
    /// </summary>
    public sealed class InMemoryDataSource : IDataSource
    {
        private readonly Dictionary<Type, object> _parameters = new Dictionary<Type, object>();
        private readonly Dictionary<string, List<string>> _sets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string[]>> _rows = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>註冊某參數型別的資料列（重複註冊同型別 = 覆蓋）。回傳 this 供鏈式呼叫。</summary>
        public InMemoryDataSource AddParameters<TParamClass>(IEnumerable<TParamClass> rows) where TParamClass : ModelElementBase, new()
        {
            _parameters[typeof(TParamClass)] = rows.ToList();
            return this;
        }

        /// <summary>註冊一維 set（重複註冊同名 = 覆蓋）。回傳 this 供鏈式呼叫。</summary>
        public InMemoryDataSource AddSet(string name, IEnumerable<string> members)
        {
            var values = members.ToList();
            var logicalName = SetNaming.Logical(name);
            _sets[logicalName] = values;
            _rows[logicalName] = values.Select(value => new[] { value }).ToList();
            return this;
        }

        /// <summary>Adds raw records, including multi-column rows, for a named source.</summary>
        public InMemoryDataSource AddRows(string name, IEnumerable<string[]> rows)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            _rows[SetNaming.Logical(name)] = rows
                .Select(row => row?.ToArray() ?? throw new ArgumentException("A data row cannot be null.", nameof(rows)))
                .ToList();
            return this;
        }

        /// <summary>取先前以 AddParameters 註冊的參數列。file 參數對記憶體來源無意義（以型別為 key），僅為介面相容而保留。</summary>
        /// <exception cref="KeyNotFoundException">該參數型別尚未註冊。</exception>
        public List<TParamClass> LoadParam<TParamClass>(string file = null) where TParamClass : ModelElementBase, new()
        {
            if (_parameters.TryGetValue(typeof(TParamClass), out var rows))
                return (List<TParamClass>)rows;
            throw new KeyNotFoundException($"[InMemoryDataSource] 未註冊參數型別 {typeof(TParamClass).Name}，請先 AddParameters。");
        }

        /// <summary>取先前以 AddSet 註冊的 set 成員。名稱去 Set_ 前綴後比對，故 "Product" 與 "Set_Product" 等價。</summary>
        /// <exception cref="KeyNotFoundException">該 set 尚未註冊。</exception>
        [Obsolete("Use LoadRows for new code. LoadSet is retained for one-column compatibility.")]
        public List<string> LoadSet(string name)
        {
            if (_sets.TryGetValue(SetNaming.Logical(name), out var members))
                return members;
            throw new KeyNotFoundException($"[InMemoryDataSource] 未註冊 set '{name}'，請先 AddSet。");
        }
        /// <summary>Loads a defensive copy of every raw record registered under <paramref name="name"/>.</summary>
        public IEnumerable<string[]> LoadRows(string name)
        {
            if (_rows.TryGetValue(SetNaming.Logical(name), out var rows))
                return rows.Select(row => row.ToArray()).ToList();
            throw new KeyNotFoundException($"[InMemoryDataSource] No rows named '{name}' have been registered. Call AddRows or AddSet first.");
        }
    }
}
