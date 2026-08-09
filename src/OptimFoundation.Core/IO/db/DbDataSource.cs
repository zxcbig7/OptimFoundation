using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 資料庫資料來源（query-only）：靠 IDbCtrl 抽象（Oracle 或其他實作皆可）。
    /// DB 讀取一律「明寫 Query SQL」——Load&lt;T&gt; 的第一引數就是 SELECT，不猜表名
    /// （真實 DB 一定要 join / 條件 / 投影 / WHERE data_id）。
    /// 讀回按「欄名 = property 名」對位（大小寫不敏感），多餘欄忽略；欄名不符用 AS 別名對過去。
    /// </summary>
    public sealed class DbDataSource : IDataSource
    {
        private readonly IDbCtrl _db;

        /// <param name="db">資料庫控制器（如 OracleDBCtrl）</param>
        public DbDataSource(IDbCtrl db)
            => _db = db ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(db)),
                "DB_SOURCE_INVALID", "資料庫來源不合法", nameof(DbDataSource), null, "db_is_null");

        /// <summary>
        /// 用 SELECT 讀 Set 或 Parameter（欄名對 property，大小寫不敏感、多餘欄忽略）——第一引數是 SQL。
        /// join / 條件 / view / WHERE data_id 都行，欄名不符用 AS 別名對到 property 名。
        /// </summary>
        public List<T> Load<T>(string sql, params (string name, object value)[] parameters)
            where T : ModelElementBase, new()
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(sql)),
                    "DB_QUERY_INVALID", "資料庫查詢不合法", nameof(Load), sql, "sql_is_empty");
            return ModelRowMapper.MapTable<T>(LoadData(sql, parameters), sql);
        }

        /// <summary>執行 SQL 並回傳原始結果表，不映射至模型資料列。</summary>
        public DataTable LoadData(string sql)
            => LoadData(sql, Array.Empty<(string name, object value)>());

        /// <summary>執行含參數 SQL 並回傳原始結果表，不映射至模型資料列。</summary>
        public DataTable LoadData(string sql, params (string name, object value)[] parameters)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(sql)),
                    "DB_QUERY_INVALID", "資料庫查詢不合法", nameof(LoadData), sql, "sql_is_empty");
            try
            {
                return _db.Query(sql, parameters);
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "DB_QUERY_FAILED", "公開 API 執行失敗", nameof(LoadData), sql,
                    ex.GetBaseException().Message);
                throw;
            }
        }
    }
}
