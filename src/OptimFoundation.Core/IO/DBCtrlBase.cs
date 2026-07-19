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
        protected readonly string ConnectionString;

        // transaction 期間的 ambient 連線/交易：非 null 時，所有操作改用這條連線、不各自建立/釋放，
        // 也不 dispose 它（歸 ExecuteInTransaction 統一管理）。
        protected IDbConnection AmbientConnection { get; private set; }
        protected IDbTransaction AmbientTransaction { get; private set; }

        protected DBCtrlBase(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public abstract void Open();
        public abstract void Close();
        public abstract DataTable Query(string sql, params (string name, object value)[] parameters);
        public abstract int Execute(string sql, params (string name, object value)[] parameters);
        public abstract TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters);
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

        public virtual void NonQuery(string sql, params (string name, object value)[] parameters)
            => Execute(sql, parameters);

        public virtual void Dispose()
        {
            Close();
        }
    }
}
