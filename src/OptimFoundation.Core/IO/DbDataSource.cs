using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 資料庫資料來源：靠 IDbCtrl 抽象（Oracle 或其他實作皆可）。
    /// 慣例：參數表名 = {tablePrefix}{型別名}（可用 sqlOverride 換成任意 SELECT）；
    /// 讀回按「欄名 = property 名」對位（大小寫不敏感），DATA_ID / USER_ID 等多餘欄自動忽略——
    /// 與 CsvCtrl.BuildParameter 的 header-aware 同一語意，DB 與 CSV 讀法一致。
    /// </summary>
    public sealed class DbDataSource : IDataSource
    {
        private readonly IDbCtrl _db;
        private readonly string _tablePrefix;
        private readonly string _dataId;
        private readonly Func<string, string> _setSqlResolver;

        /// <param name="db">資料庫控制器（如 OracleDBCtrl）</param>
        /// <param name="tablePrefix">參數表名前綴（表名 = 前綴 + 型別名，一律轉大寫）</param>
        /// <param name="dataId">非 null 時自動加 WHERE DATA_ID = :id（多情境同表）</param>
        /// <param name="setSqlResolver">ReadSet 的 SQL 解析器（name → SELECT 單欄語句）；未提供時 ReadSet 丟例外</param>
        public DbDataSource(IDbCtrl db, string tablePrefix = "", string dataId = null, Func<string, string> setSqlResolver = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _tablePrefix = tablePrefix ?? "";
            _dataId = dataId;
            _setSqlResolver = setSqlResolver;
        }

        public List<TParamClass> ReadParameters<TParamClass>() where TParamClass : ModelElementBase, new()
        {
            var type = typeof(TParamClass);
            string table = (_tablePrefix + type.Name).ToUpperInvariant();

            DataTable dt = _dataId == null
                ? _db.Query($"SELECT * FROM {table}")
                : _db.Query($"SELECT * FROM {table} WHERE DATA_ID = :DATA_ID", (":DATA_ID", _dataId));

            // 與 InitClassBySets 相同的順序來源；欄位按名對位（大小寫不敏感），多餘欄忽略
            var props = type.GetProperties();
            var colMap = props.Select(p =>
            {
                int idx = -1;
                for (int c = 0; c < dt.Columns.Count; c++)
                    if (string.Equals(dt.Columns[c].ColumnName, p.Name, StringComparison.OrdinalIgnoreCase)) { idx = c; break; }
                if (idx < 0)
                    throw new InvalidDataException(
                        $"[DbDataSource] 資料表 {table} 缺少欄位 '{p.Name}'。{type.Name} 需要：{string.Join(", ", props.Select(x => x.Name))}；表內欄位：{string.Join(", ", dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName))}");
                return idx;
            }).ToArray();

            var data = new List<TParamClass>();
            foreach (DataRow row in dt.Rows)
            {
                var values = new object[props.Length];
                for (int i = 0; i < props.Length; i++)
                    values[i] = row[colMap[i]]?.ToString() ?? "";
                var instance = new TParamClass();
                instance.InitClassBySets(values);   // string 值由 InitClassBySets 依 property 型別轉換
                data.Add(instance);
            }
            return data;
        }

        public List<string> ReadSet(string name)
        {
            if (_setSqlResolver == null)
                throw new NotSupportedException(
                    $"[DbDataSource] ReadSet('{name}') 需要 setSqlResolver（name → SELECT 單欄 SQL），或改由 Parameter distinct 衍生 set。");

            var dt = _db.Query(_setSqlResolver(name));
            return dt.Rows.Cast<DataRow>().Select(r => r.ItemArray[0]?.ToString() ?? "").ToList();
        }
    }
}
