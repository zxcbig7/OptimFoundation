using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using OptimFoundation.Core.IO;

namespace OptimFoundation.Cplex.Tests.Unit
{
    internal sealed class FakeDbCtrl : IDbCtrl
    {
        public string LastSql = string.Empty;
        public (string name, object value)[] LastParameters = Array.Empty<(string, object)>();
        public DataTable NextResult = new();
        public List<(string sql, (string name, object value)[] parameters)> ExecutedCommands = new();
        public List<(string sql, (string name, object value)[][] rows)> ExecutedBatches = new();
        public int ExecuteInTransactionCallCount;
        public bool Committed;
        public bool RolledBack;
        public int ThrowOnExecuteCallNumber = -1;
        public int ThrowOnBatchCallNumber = -1;

        public DataTable Query(string sql, params (string name, object value)[] parameters)
        {
            LastSql = sql;
            LastParameters = parameters;
            return NextResult;
        }

        public void Open() { }
        public void Close() { }
        public void NonQuery(string sql, params (string name, object value)[] parameters) { }
        public int Execute(string sql, params (string name, object value)[] parameters)
        {
            ExecutedCommands.Add((sql, parameters));
            if (ThrowOnExecuteCallNumber == ExecutedCommands.Count)
                throw new InvalidOperationException($"[FakeDbCtrl] Simulated failure on write {ExecutedCommands.Count}.");
            return 0;
        }
        public TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters) => default!;
        public void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows)
        {
            ExecutedBatches.Add((sql, rows.ToArray()));
            if (ThrowOnBatchCallNumber == ExecutedBatches.Count)
                throw new InvalidOperationException($"[FakeDbCtrl] Simulated failure on batch {ExecutedBatches.Count}.");
        }
        public void ExecuteInTransaction(Action<IDbCtrl> work)
        {
            ExecuteInTransactionCallCount++;
            try { work(this); Committed = true; }
            catch { RolledBack = true; throw; }
        }
        public void Dispose() { }
    }
}
