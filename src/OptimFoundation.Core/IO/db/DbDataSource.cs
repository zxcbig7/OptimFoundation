using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 資料庫資料來源（query-only）：靠 IDbCtrl 抽象（Oracle 或其他實作皆可）。
    /// DB 讀取一律「明寫 Query SQL」——LoadParam / LoadSet 的第一引數就是 SELECT，不猜表名
    /// （真實 DB 一定要 join / 條件 / 投影 / WHERE data_id）。
    /// 讀回按「欄名 = property 名」對位（大小寫不敏感），多餘欄忽略；欄名不符用 AS 別名對過去。
    /// </summary>
    public sealed class DbDataSource : IDataSource
    {
        private readonly IDbCtrl _db;

        /// <param name="db">資料庫控制器（如 OracleDBCtrl）</param>
        public DbDataSource(IDbCtrl db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        /// <summary>
        /// 用 SELECT 讀參數（欄名對 property，大小寫不敏感、多餘欄忽略）——第一引數是 SQL。
        /// join / 條件 / view / WHERE data_id 都行，欄名不符用 AS 別名對到 property 名。
        /// </summary>
        public List<T> Load<T>(string sql, params (string name, object value)[] parameters)
            where T : ModelElementBase, new()
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentNullException(nameof(sql));
            return ModelRowMapper.MapTable<T>(LoadData(sql, parameters), sql);
        }

        /// <summary>執行 SQL，回傳欄名與所有結果列；儲存格統一轉為不依賴地區設定的字串。</summary>
        /// <summary>執行含參數 SQL，回傳欄名與所有結果列；儲存格統一轉為不依賴地區設定的字串。</summary>
        /// <summary>執行 SQL 並回傳原始結果表，不映射至模型積木。</summary>
        public DataTable LoadData(string sql)
            => LoadData(sql, Array.Empty<(string name, object value)>());

        /// <summary>執行含參數 SQL 並回傳原始結果表，不映射至模型積木。</summary>
        public DataTable LoadData(string sql, params (string name, object value)[] parameters)
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentNullException(nameof(sql));
            return _db.Query(sql, parameters);
        }
    }
}
