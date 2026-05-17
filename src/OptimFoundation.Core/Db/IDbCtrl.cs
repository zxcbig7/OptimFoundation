using System;
using System.Data;

namespace OptimFoundation.Core.Db
{
    public interface IDbCtrl : IDisposable
    {
        // 連線
        void Open();
        // 取消連線
        void Close();
        DataTable Query(string sql, params (string name, object value)[] parameters);
        int Execute(string sql, params (string name, object value)[] parameters);
        T QueryScalar<T>(string sql, params (string name, object value)[] parameters);
    }
}
