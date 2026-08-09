using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 記憶體資料來源：demo / 單元測試 / 程式生成實例用。
    /// 以 AddRows 流暢註冊中立表格或具型別資料列，Dataload 端與 CSV / DB 使用同一介面讀取。
    /// </summary>
    public sealed class InMemoryDataSource : IDataSource
    {
        private readonly Dictionary<string, List<string[]>> _rows = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>註冊某參數型別的資料列（重複註冊同型別 = 覆蓋）。回傳 this 供鏈式呼叫。</summary>
        public InMemoryDataSource AddRows<T>(IEnumerable<T> rows) where T : ModelElementBase, new()
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var properties = typeof(T).GetProperties();
            _rows[typeof(T).Name] = new[]
                {
                    properties.Select(property => property.Name).ToArray()
                }
                .Concat(rows
                .Select(row => row == null
                    ? throw new ArgumentException("A parameter row cannot be null.", nameof(rows))
                    : properties.Select(property => ModelRowMapper.ToInvariantString(property.GetValue(row))).ToArray()))
                .ToList();
            return this;
        }

        /// <summary>註冊一維 set（重複註冊同名 = 覆蓋）。來源名稱與 Set 表頭皆須明確指定，回傳 this 供鏈式呼叫。</summary>
        /// <summary>為具名來源加入原始資料列，可包含多欄資料。</summary>
        public InMemoryDataSource AddRows(string name, IEnumerable<string[]> rows)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            _rows[name] = rows
                .Select(row => row?.ToArray() ?? throw new ArgumentException("A data row cannot be null.", nameof(rows)))
                .ToList();
            return this;
        }

        /// <summary>載入註冊於 <paramref name="name"/> 的所有 Set 資料列之防禦性複本。</summary>
        private IEnumerable<string[]> LoadRows(string name)
        {
            if (_rows.TryGetValue(name, out var rows))
                return rows.Select(row => row.ToArray()).ToList();
            throw new KeyNotFoundException($"[InMemoryDataSource] No table named '{name}' has been registered. Call AddRows first.");
        }

        /// <summary>將已註冊資料列載入為中立且含 schema 的表格。</summary>
        public DataTable LoadData(string name)
            => TabularData.ToDataTable(LoadRows(name), name);

        /// <summary>依已註冊表格的表頭與 public property 對應，載入具型別的 Parameter。</summary>
    }
}
