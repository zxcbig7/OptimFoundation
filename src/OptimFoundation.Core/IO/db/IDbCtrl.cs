using System;
using System.Collections.Generic;
using System.Data;

namespace OptimFoundation.Core.IO
{

    /// <summary>
    /// OptimFoundation.Core 的資料庫存取抽象層。
    /// 定義 DB-agnostic 操作合約，具體驅動（如 OracleDBCtrl）隔離在外部專案。
    /// 參數一律用 (name, value) tuple 傳 bind variable，避免 SQL injection。
    /// </summary>
    public interface IDbCtrl : IDisposable
    {
        /// <summary>開啟連線。實作可留空（如 Oracle 靠 connection pool 每次操作自建連線）。</summary>
        void Open();

        /// <summary>關閉連線。Dispose 預設會呼叫此方法。</summary>
        void Close();

        /// <summary>執行 SELECT，回傳結果集。</summary>
        /// <param name="sql">SELECT 語句，bind variable 用 :name 佔位</param>
        /// <param name="parameters">bind variable 名稱與值</param>
        /// <returns>查詢結果 DataTable</returns>
        DataTable Query(string sql, params (string name, object value)[] parameters);

        /// <summary>執行不回傳結果的語句（DDL 或不需筆數的 DML）。</summary>
        void NonQuery(string sql, params (string name, object value)[] parameters);

        /// <summary>執行 INSERT / UPDATE / DELETE。</summary>
        /// <returns>受影響筆數</returns>
        int Execute(string sql, params (string name, object value)[] parameters);

        /// <summary>查詢單一值（如 COUNT、MAX），轉型為 TResult 回傳。</summary>
        /// <typeparam name="TResult">回傳值型別</typeparam>
        TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters);

        /// <summary>在單一 transaction 內執行 work；任一步失敗須全 rollback（見框架資料防護規格輸出 transaction）。</summary>
        void ExecuteInTransaction(Action<IDbCtrl> work);

        /// <summary>
        /// 同一句 SQL 套用多列參數，一次送出（批次寫入）。實作應優先使用底層 driver 的批次能力
        /// （如 Oracle array-bind），避免逐列往返造成效能退化。遵循 ambient transaction（於
        /// ExecuteInTransaction 期間呼叫時，須併入外層交易）。
        /// </summary>
        /// <param name="sql">SQL 語句，bind variable 用 :name 佔位</param>
        /// <param name="rows">每列一組 (name, value) 參數，各列的 name 集合須一致</param>
        void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows);
    }
}
