using System;
using System.Collections.Generic;
using System.Data;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// IDbCtrl 的 DB-agnostic base：交易編排（ambient 連線/交易、巢狀參與外層、成功 commit /
    /// 例外 rollback 後 rethrow、finally 清理）與具體資料庫無關，集中在這裡用 BCL 的
    /// IDbConnection/IDbTransaction 承載，具體驅動（如 Oracle）只需覆寫 CreateRawConnection。
    /// </summary>
    public abstract class DBCtrlBase : IDbCtrl
    {
        /// <summary>建構時傳入的連線字串；每次操作據此開連線（transaction 期間例外，見 AmbientConnection）。</summary>
        protected readonly string ConnectionString;

        // transaction 期間的 ambient 連線/交易：非 null 時，所有操作改用這條連線、不各自建立/釋放，
        // 也不 dispose 它（歸 ExecuteInTransaction 統一管理）。
        /// <summary>transaction 期間共用的連線；非 null 代表正在交易中。由 ExecuteInTransaction 設定與清除。</summary>
        protected IDbConnection AmbientConnection { get; private set; }

        /// <summary>transaction 期間共用的交易物件；子類別執行 SQL 時 MUST 把它掛到 command 上，否則該筆操作不在交易內。</summary>
        protected IDbTransaction AmbientTransaction { get; private set; }

        /// <summary>只記下連線字串，不建立連線。</summary>
        protected DBCtrlBase(string connectionString)
        {
            ConnectionString = connectionString;
        }

        /// <summary>開啟連線（實作可為 no-op——每次操作自帶連線的驅動不需要它）。</summary>
        public abstract void Open();

        /// <summary>關閉連線（實作可為 no-op）。</summary>
        public abstract void Close();

        /// <summary>執行查詢並回傳整張結果表。parameters 為具名參數，ALWAYS 用它帶值，NEVER 字串拼 SQL。</summary>
        public abstract DataTable Query(string sql, params (string name, object value)[] parameters);

        /// <summary>執行 INSERT / UPDATE / DELETE / DDL，回傳受影響列數。</summary>
        public abstract int Execute(string sql, params (string name, object value)[] parameters);

        /// <summary>執行查詢並取第一列第一欄，轉型成 TResult。</summary>
        public abstract TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters);

        /// <summary>同一句 SQL 套多組參數批次執行（陣列綁定）；大量寫入 ALWAYS 用它，不要迴圈呼叫 Execute。</summary>
        public abstract void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows);

        /// <summary>建立並開啟一條實際資料庫連線，由具體驅動實作（如 Oracle）。</summary>
        protected abstract IDbConnection CreateRawConnection();

        /// <summary>
        /// 在單一 transaction 內執行 work：開一條連線 + BeginTransaction，設為 ambient，
        /// 使期間所有操作（Query/Execute/QueryScalar/ExecuteBatch）共用同一連線與交易；
        /// work 成功即 Commit，丟例外則 Rollback 後原樣 rethrow。
        /// 巢狀呼叫（transaction 中再次呼叫本方法）直接參與外層交易，不另開連線也不提前 commit/rollback。
        /// </summary>
        public void ExecuteInTransaction(Action<IDbCtrl> work)
        {
            if (AmbientTransaction != null)
            {
                work(this);
                return;
            }

            var conn = CreateRawConnection();
            try
            {
                var tx = conn.BeginTransaction();
                AmbientConnection = conn;
                AmbientTransaction = tx;
                try
                {
                    work(this);
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
                finally
                {
                    tx.Dispose();
                    AmbientConnection = null;
                    AmbientTransaction = null;
                }
            }
            finally
            {
                conn.Dispose();
            }
        }

        /// <summary>不需要受影響列數時的 <see cref="Execute"/> 簡寫。</summary>
        public virtual void NonQuery(string sql, params (string name, object value)[] parameters)
            => Execute(sql, parameters);

        /// <summary>釋放資源；預設只呼叫 Close()。</summary>
        public virtual void Dispose()
        {
            Close();
        }
    }
}
