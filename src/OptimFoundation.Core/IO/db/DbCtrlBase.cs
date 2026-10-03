using System;
using System.Collections.Generic;
using System.Data;

namespace OptimFoundation.Core.IO
{
    /// <summary>
    /// IDbCtrl 的共用基底類別，負責交易中的連線共用、成功時 Commit、失敗時 Rollback，
    /// 並在結束後釋放資源。巢狀呼叫會沿用外層交易。這些操作使用 .NET 的
    /// IDbConnection/IDbTransaction；子類別（如 Oracle）提供 CreateRawConnection 與資料存取方法。
    /// </summary>
    public abstract class DbCtrlBase : IDbCtrl
    {
        /// <summary>建構時傳入的連線字串；每次操作據此開連線（transaction 期間例外，見 AmbientConnection）。</summary>
        protected readonly string ConnectionString;

        // 交易進行期間，各資料存取方法應共用這裡的連線與交易，不另建連線，
        // 也不自行釋放；由 ExecuteInTransaction 在交易結束後統一處理。
        /// <summary>transaction 期間共用的連線；非 null 代表正在交易中。由 ExecuteInTransaction 設定與清除。</summary>
        protected IDbConnection AmbientConnection { get; private set; }

        /// <summary>交易期間共用的交易物件；子類別執行 SQL 時必須指定為 command 的 Transaction，才能納入同一交易。</summary>
        protected IDbTransaction AmbientTransaction { get; private set; }

        /// <summary>只記下連線字串，不建立連線。</summary>
        protected DbCtrlBase(string connectionString)
        {
            ConnectionString = connectionString;
        }

        /// <summary>開啟連線；若子類別在每次操作時自行取得連線，此方法可不做任何事。</summary>
        public abstract void Open();

        /// <summary>關閉連線；若子類別自行管理每次操作的連線，此方法可不做任何事。</summary>
        public abstract void Close();

        /// <summary>執行查詢並回傳 DataTable。值應透過具名參數 parameters 傳入，避免直接拼進 SQL。</summary>
        public abstract DataTable Query(string sql, params (string name, object value)[] parameters);

        /// <summary>執行 INSERT / UPDATE / DELETE / DDL，回傳受影響列數。</summary>
        public abstract int Execute(string sql, params (string name, object value)[] parameters);

        /// <summary>執行查詢並取第一列第一欄，轉型成 TResult。</summary>
        public abstract TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters);

        /// <summary>用多組參數批次執行同一句 SQL；大量寫入時可減少逐筆呼叫 Execute 的連線往返成本。</summary>
        public abstract void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows);

        /// <summary>建立並開啟一條實際資料庫連線，由具體驅動實作（如 Oracle）。</summary>
        protected abstract IDbConnection CreateRawConnection();

        /// <summary>
        /// 開啟連線與交易執行 work，存入 AmbientConnection / AmbientTransaction，供期間的
        /// Query / Execute / QueryScalar / ExecuteBatch 共用。成功則 Commit，例外則 Rollback 後原樣拋出。
        /// 巢狀呼叫沿用外層交易，不另開連線，也不提前 Commit 或 Rollback。
        /// </summary>
        public void ExecuteInTransaction(Action<IDbCtrl> work)
        {
            if (work == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(work)),
                    "DB_TRANSACTION_INVALID", "資料庫交易不合法", nameof(ExecuteInTransaction), null, "work_is_null");

            if (AmbientTransaction != null)
            {
                try
                {
                    work(this);
                    return;
                }
                catch (Exception ex)
                {
                    Logging.ErrorOnce(
                        ex, "DB_TRANSACTION_FAILED", "公開 API 執行失敗", nameof(ExecuteInTransaction), GetType().FullName,
                        ex.GetBaseException().Message);
                    throw;
                }
            }

            IDbConnection conn;
            try
            {
                conn = CreateRawConnection();
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(
                    ex, "DB_TRANSACTION_FAILED", "公開 API 執行失敗", nameof(ExecuteInTransaction), GetType().FullName,
                    ex.GetBaseException().Message);
                throw;
            }
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
                catch (Exception ex)
                {
                    try
                    {
                        tx.Rollback();
                    }
                    catch (Exception rollbackException)
                    {
                        Logging.ErrorOnce(
                            rollbackException,
                            "DB_ROLLBACK_FAILED", "資料庫回滾失敗", nameof(ExecuteInTransaction), GetType().FullName,
                            rollbackException.GetBaseException().Message);
                    }
                    Logging.ErrorOnce(
                        ex, "DB_TRANSACTION_FAILED", "公開 API 執行失敗", nameof(ExecuteInTransaction), GetType().FullName,
                        ex.GetBaseException().Message);
                    throw;
                }
                finally
                {
                    tx.Dispose();
                    AmbientConnection = null;
                    AmbientTransaction = null;
                }
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(
                    ex, "DB_TRANSACTION_FAILED", "公開 API 執行失敗", nameof(ExecuteInTransaction), GetType().FullName,
                    ex.GetBaseException().Message);
                throw;
            }
            finally
            {
                try
                {
                    conn.Dispose();
                }
                catch (Exception ex)
                {
                    Logging.ErrorOnce(
                        ex, "DB_DISPOSE_FAILED", "資料庫連線釋放失敗", nameof(ExecuteInTransaction), GetType().FullName,
                        ex.GetBaseException().Message);
                    throw;
                }
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
