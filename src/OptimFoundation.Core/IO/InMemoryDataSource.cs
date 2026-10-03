using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 把程式提供的資料列存放在記憶體，供範例、單元測試或程式產生的模型資料使用。
    /// 用 AddRows 加入含表頭的字串列，或 Set/Parameter 物件；Dataload 可透過 IDataSource 讀取。
    /// </summary>
    public sealed class InMemoryDataSource : IDataSource
    {
        private readonly Dictionary<string, List<string[]>> _rows = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>註冊某 Set 或 Parameter 型別的資料列（重複註冊同型別 = 覆蓋）。回傳 this 供鏈式呼叫。</summary>
        public InMemoryDataSource AddRows<T>(IEnumerable<T> rows) where T : ModelElementBase, new()
        {
            if (rows == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(rows)),
                    "DATA_SOURCE_INVALID", "記憶體資料來源不合法", nameof(AddRows), typeof(T).Name, "rows_are_null");
            var properties = typeof(T).GetProperties();
            _rows[typeof(T).Name] = new[]
                {
                    properties.Select(property => property.Name).ToArray()
                }
                .Concat(rows
                .Select(row => row == null
                    ? throw Logging.ErrorOnce(
                        new ArgumentException("A model row cannot be null.", nameof(rows)),
                        "DATA_SOURCE_INVALID", "記憶體資料來源不合法", nameof(AddRows), typeof(T).Name, "row_is_null")
                    : properties.Select(property => ModelRowMapper.ToInvariantString(property.GetValue(row))).ToArray()))
                .ToList();
            return this;
        }

        /// <summary>為具名來源加入原始資料列，可包含多欄資料。</summary>
        public InMemoryDataSource AddRows(string name, IEnumerable<string[]> rows)
        {
            if (name == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(name)),
                    "DATA_SOURCE_INVALID", "記憶體資料來源不合法", nameof(AddRows), null, "name_is_null");
            if (rows == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(rows)),
                    "DATA_SOURCE_INVALID", "記憶體資料來源不合法", nameof(AddRows), name, "rows_are_null");

            _rows[name] = rows
                .Select(row => row?.ToArray() ?? throw Logging.ErrorOnce(
                    new ArgumentException("A data row cannot be null.", nameof(rows)),
                    "DATA_SOURCE_INVALID", "記憶體資料來源不合法", nameof(AddRows), name, "row_is_null"))
                .ToList();
            return this;
        }

        /// <summary>依 <paramref name="name"/> 取回資料，並複製各列，避免呼叫端修改已保存的內容。</summary>
        private IEnumerable<string[]> LoadRows(string name)
        {
            if (_rows.TryGetValue(name, out var rows))
                return rows.Select(row => row.ToArray()).ToList();
            throw Logging.ErrorOnce(
                new KeyNotFoundException($"[InMemoryDataSource] No table named '{name}' has been registered. Call AddRows first."),
                "DATA_SOURCE_NOT_FOUND", "找不到記憶體資料來源", nameof(LoadRows), name, "table_not_registered");
        }

        /// <summary>將已加入的資料轉成 DataTable，第一列作為欄名，其餘列作為資料。</summary>
        public DataTable LoadData(string name)
            => TabularData.ToDataTable(LoadRows(name), name);

    }
}
