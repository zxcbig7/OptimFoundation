using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 以 AddRows 登記含表頭的字串列或 Set/Parameter 物件，供 IDataSource 讀取。
    /// </summary>
    public sealed class InMemoryDataSource : IDataSource
    {
        private readonly Dictionary<string, List<string[]>> _rows = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>註冊某 Set 或 Parameter 型別的資料列（重複註冊同型別 = 覆蓋）。回傳 this 供鏈式呼叫。</summary>
        public InMemoryDataSource AddRows<T>(IEnumerable<T> rows) where T : ModelElementBase, new()
        {
            if (rows == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(rows), "rows 不得為 null"),
                    "記憶體資料來源不合法", null, nameof(AddRows), typeof(T).Name, "資料列集合為空");
            var properties = ModelElementBase.GetColumns(typeof(T));
            _rows[typeof(T).Name] = new[]
                {
                    properties.Select(property => property.Name).ToArray()
                }
                .Concat(rows
                .Select(row => row == null
                    ? throw Logging.ErrorOnce(
                        new ArgumentException("資料列不得為 null", nameof(rows)),
                        "記憶體資料來源不合法", null, nameof(AddRows), typeof(T).Name, "資料列為空")
                    : properties.Select(property => ModelRowMapper.ToInvariantString(property.GetValue(row))).ToArray()))
                .ToList();
            return this;
        }

        /// <summary>為具名來源加入原始資料列，可包含多欄資料。</summary>
        public InMemoryDataSource AddRows(string name, IEnumerable<string[]> rows)
        {
            if (name == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(name), "name 不得為 null"),
                    "記憶體資料來源不合法", null, nameof(AddRows), null, "名稱為空");
            if (rows == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(rows), "rows 不得為 null"),
                    "記憶體資料來源不合法", null, nameof(AddRows), name, "資料列集合為空");

            _rows[name] = rows
                .Select(row => row?.ToArray() ?? throw Logging.ErrorOnce(
                    new ArgumentException("資料列不得為 null", nameof(rows)),
                    "記憶體資料來源不合法", null, nameof(AddRows), name, "資料列為空"))
                .ToList();
            return this;
        }

        /// <summary>依 <paramref name="name"/> 取回資料，並複製各列，避免呼叫端修改已保存的內容。</summary>
        private IEnumerable<string[]> LoadRows(string name)
        {
            if (_rows.TryGetValue(name, out var rows))
                return rows.Select(row => row.ToArray()).ToList();
            throw Logging.ErrorOnce(
                new KeyNotFoundException($"找不到資料表：{name}，請先呼叫 AddRows 登記"),
                "記憶體資料來源找不到", null, nameof(LoadRows), name, "資料表尚未註冊");
        }

        /// <summary>將已加入的資料轉成 DataTable，第一列作為欄名，其餘列作為資料。</summary>
        public DataTable LoadData(string name)
            => TabularData.ToDataTable(LoadRows(name), name);

    }
}
