using System;
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
    }
}
