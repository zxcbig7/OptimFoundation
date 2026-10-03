using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// 透過 IDbCtrl 查詢資料庫，只提供資料讀取；可使用 Oracle 或其他 IDbCtrl 實作。
    /// Load&lt;T&gt; 的第一個參數須傳入完整 SELECT SQL，不會把名稱自動補成查詢語句。
    /// 呼叫端可自行加入 JOIN、WHERE、選取欄位或 data_id 篩選條件。
    /// 讀回按「欄名 = property 名」對位（大小寫不敏感），多餘欄忽略；欄名不符用 AS 別名對過去。
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
        /// 第一個引數是完整 SELECT SQL，可包含 JOIN、條件或 view。
        /// 依欄名填入 Set 或 Parameter 的 property，不分大小寫，多餘欄位略過；欄名不同時可用 AS 指定別名。
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
