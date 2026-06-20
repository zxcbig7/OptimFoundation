using System;
using System.Data;

namespace OptimFoundation.Core.Db
{
    public interface IDbCtrl : IDisposable
    {
        // �s�u
        void Open();
        // �����s�u
        void Close();
        DataTable Query(string sql, params (string name, object value)[] parameters);
        int Execute(string sql, params (string name, object value)[] parameters);
        TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters);
    }
}
