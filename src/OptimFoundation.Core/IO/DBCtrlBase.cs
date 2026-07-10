using System;
using System.Data;

namespace OptimFoundation.Core.IO
{
    public abstract class DBCtrlBase : IDbCtrl
    {
        protected readonly string ConnectionString;

        protected DBCtrlBase(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public abstract void Open();
        public abstract void Close();
        public abstract DataTable Query(string sql, params (string name, object value)[] parameters);
        public abstract int Execute(string sql, params (string name, object value)[] parameters);
        public abstract TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters);

        public virtual void NonQuery(string sql, params (string name, object value)[] parameters)
            => Execute(sql, parameters);

        public virtual void Dispose()
        {
            Close();
        }
    }
}
