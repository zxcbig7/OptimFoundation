using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 透過 IDbCtrl 讀取完整 SELECT SQL，不自動補查詢語句。
    /// 欄名依 property 對位且不分大小寫，多餘欄忽略；可用 AS 指定別名。
    /// </summary>
    public sealed class DbDataSource : IDataSource
    {
        private readonly IDbCtrl _db;

        /// <param name="db">資料庫控制器（如 OracleDbCtrl）</param>
        public DbDataSource(IDbCtrl db)
            => _db = db ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(db)),
                "DB_SOURCE_INVALID", "資料庫來源不合法", nameof(DbDataSource), null, "db_is_null");

        /// <summary>
        /// 執行完整 SELECT SQL，依欄名映射 property；不分大小寫，多餘欄略過。
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
