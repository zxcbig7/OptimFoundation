using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 資料庫資料來源（query-only）：靠 IDbCtrl 抽象（Oracle 或其他實作皆可）。
    /// DB 讀取一律「明寫 Query SQL」——LoadParam / LoadSet 的第一引數就是 SELECT，不猜表名
    /// （真實 DB 一定要 join / 條件 / 投影 / WHERE data_id）。與 CsvDataSource / InMemoryDataSource 共用 LoadParam / LoadSet
    /// 命名，但因 SQL ≠ 名稱、語意不同，本類刻意 NOT 實作 IDataSource（避免「同名不同意」的漏抽象）。
    /// 讀回按「欄名 = property 名」對位（大小寫不敏感），多餘欄忽略；欄名不符用 AS 別名對過去。
    /// </summary>
    public sealed class DbDataSource : IDataSource
    {
        private readonly IDbCtrl _db;

        /// <param name="db">資料庫控制器（如 OracleDBCtrl）</param>
        public DbDataSource(IDbCtrl db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        /// <summary>
        /// 用 SELECT 讀參數（欄名對 property，大小寫不敏感、多餘欄忽略）——與 CsvDataSource.LoadParam 同名，第一引數是 SQL。
        /// join / 條件 / view / WHERE data_id 都行，欄名不符用 AS 別名對到 property 名。
        /// </summary>
        public List<T> LoadParam<T>(string sql, params (string name, object value)[] parameters)
            where T : ModelElementBase, new()
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentNullException(nameof(sql));
            return MapRows<T>(_db.Query(sql, parameters), sql);
        }

        List<TParamClass> IDataSource.LoadParam<TParamClass>(string file)
            => LoadParam<TParamClass>(file);

        /// <summary>用 SELECT 讀一維 set（取第一欄）——與 CsvDataSource.LoadSet 同名，第一引數是 SQL。</summary>
        public List<string> LoadSet(string sql, params (string name, object value)[] parameters)
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentNullException(nameof(sql));
            var dt = _db.Query(sql, parameters);
            return dt.Rows.Cast<DataRow>().Select(r => r.ItemArray[0]?.ToString() ?? "").ToList();
        }

        [Obsolete("Use LoadRows for new code. LoadSet is retained for one-column compatibility.")]
        List<string> IDataSource.LoadSet(string name) => LoadSet(name);

        /// <summary>Executes <paramref name="sql"/> and returns every selected column as an invariant string.</summary>
        public IEnumerable<string[]> LoadRows(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentNullException(nameof(sql));
            return _db.Query(sql).Rows.Cast<DataRow>()
                .Select(row => row.ItemArray.Select(ParameterRowMapper.ToInvariantString).ToArray())
                .ToList();
        }

        /// <summary>
        /// 用 SELECT 讀整張表 raw DataTable（set/param 以外的通用用途）——與 CsvDataSource.LoadTable 同名，第一引數是 SQL。
        /// 不做欄名對位 / 轉型，直接回 IDbCtrl.Query 的結果。
        /// </summary>
        public DataTable LoadTable(string sql, params (string name, object value)[] parameters)
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentNullException(nameof(sql));
            return _db.Query(sql, parameters);
        }

        // 與 InitClassBySets 相同的順序來源；欄位按名對位（大小寫不敏感），多餘欄忽略
        private static List<TParamClass> MapRows<TParamClass>(DataTable dt, string sourceDesc)
            where TParamClass : ModelElementBase, new()
        {
            var type = typeof(TParamClass);
            var props = type.GetProperties();
            var colMap = props.Select(p =>
            {
                int idx = -1;
                for (int c = 0; c < dt.Columns.Count; c++)
                    if (string.Equals(dt.Columns[c].ColumnName, p.Name, StringComparison.OrdinalIgnoreCase)) { idx = c; break; }
                if (idx < 0)
                    throw new InvalidDataException(
                        $"[DbDataSource] 來源 {sourceDesc} 缺少欄位 '{p.Name}'。{type.Name} 需要：{string.Join(", ", props.Select(x => x.Name))}；回傳欄位：{string.Join(", ", dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName))}");
                return idx;
            }).ToArray();

            var data = new List<TParamClass>();
            foreach (DataRow row in dt.Rows)
            {
                var cells = new string[props.Length];
                for (int i = 0; i < props.Length; i++)
                    cells[i] = ParameterRowMapper.ToInvariantString(row[colMap[i]]);
                var instance = new TParamClass();
                var values = ParameterRowMapper.ConvertCells(props, cells, $"DbDataSource {sourceDesc}");
                instance.InitClassBySets(values);   // string 值由 InitClassBySets 依 property 型別轉換
                data.Add(instance);
            }
            return data;
        }
    }
}
