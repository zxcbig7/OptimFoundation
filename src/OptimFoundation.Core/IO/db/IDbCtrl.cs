using System;
using System.Collections.Generic;
using System.Data;

namespace OptimFoundation.Core.IO
{

    /// <summary>
    /// 資料庫查詢、寫入與交易介面；輸入值須透過具名 tuple 參數傳入。
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

        /// <summary>在同一交易內執行 work；任一步失敗時回滾整個交易，避免只寫入部分資料。</summary>
        void ExecuteInTransaction(Action<IDbCtrl> work);

        /// <summary>
        /// 同一 SQL 批次套用多列參數；優先使用 driver 批次功能，交易內須沿用共用連線與交易。
        /// </summary>
        /// <param name="sql">SQL 語句，bind variable 用 :name 佔位</param>
        /// <param name="rows">每列一組 (name, value) 參數，各列的 name 集合須一致</param>
        void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows);
    }
}
