using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Oracle.ManagedDataAccess.Client;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;

namespace OptimFoundation.Db.Oracle
{
    /// <summary>
    /// transaction 期間（ExecuteInTransaction 執行中）本實例持有 ambient connection/transaction 狀態，
    /// 非 thread-safe，勿跨執行緒共用同一實例做交易。
    /// </summary>
    public sealed class OracleDBCtrl : DBCtrlBase
    {
        public OracleDBCtrl(string connectionString) : base(connectionString) { }

        #region IDbCtrl 基本操作

        // Open/Close 留空：每次操作自建 connection（Oracle Connection Pool）
        public override void Open() { }
        public override void Close() { }

        public override DataTable Query(string sql, params (string name, object value)[] parameters)
        {
            var conn = AcquireConnection(out bool owned);
            try
            {
                using var cmd = BuildCommand(sql, conn, AmbientTransactionOracle, parameters);
                using var adpt = new OracleDataAdapter(cmd);
                var dt = new DataTable();
                adpt.Fill(dt);
                return dt;
            }
            finally
            {
                if (owned) conn.Dispose();
            }
        }

        public override int Execute(string sql, params (string name, object value)[] parameters)
        {
            var conn = AcquireConnection(out bool owned);
            try
            {
                using var cmd = BuildCommand(sql, conn, AmbientTransactionOracle, parameters);
                int rows = cmd.ExecuteNonQuery();
                Logging.Info($"[OracleDBCtrl] Execute ({rows} row(s))");
                return rows;
            }
            finally
            {
                if (owned) conn.Dispose();
            }
        }

        public override TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters)
        {
            var conn = AcquireConnection(out bool owned);
            try
            {
                using var cmd = BuildCommand(sql, conn, AmbientTransactionOracle, parameters);
                object result = cmd.ExecuteScalar();
                return (TResult)Convert.ChangeType(result, typeof(TResult));
            }
            finally
            {
                if (owned) conn.Dispose();
            }
        }

        // 交易編排（ambient 連線/交易、巢狀參與外層、commit/rollback）與 Oracle 無關，
        // 已下沉到 DBCtrlBase.ExecuteInTransaction；這裡只提供 Oracle 專屬的連線建立方式。
        protected override IDbConnection CreateRawConnection() => CreateConnection();

        #endregion

        #region 連線工具

        private OracleConnection CreateConnection()
        {
            var conn = new OracleConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        /// <summary>
        /// 取得本次操作要用的連線：ambient 連線存在（transaction 進行中）→ 回傳它、owned=false
        /// （呼叫端不可 dispose，歸 ExecuteInTransaction 管）；否則照舊自建一條新連線、owned=true。
        /// </summary>
        private OracleConnection AcquireConnection(out bool owned)
        {
            if (AmbientConnection != null)
            {
                owned = false;
                return (OracleConnection)AmbientConnection;
            }
            owned = true;
            return CreateConnection();
        }

        // DBCtrlBase 的 ambient 交易用 BCL IDbTransaction 承載；Oracle 專屬操作（掛 cmd.Transaction、
        // array-bind）需要具體型別，這裡統一轉型一次，避免各方法重複 cast。
        private OracleTransaction AmbientTransactionOracle => (OracleTransaction)AmbientTransaction;

        private static OracleCommand BuildCommand(string sql, OracleConnection conn, OracleTransaction transaction,
            (string name, object value)[] parameters)
        {
            var cmd = new OracleCommand(sql, conn) { BindByName = true };
            if (transaction != null) cmd.Transaction = transaction;
            foreach (var (name, value) in parameters)
                cmd.Parameters.Add(name, value ?? DBNull.Value);
            return cmd;
        }

        #endregion

        #region 連線字串建構

        public static string BuildConnectionString(
            string host, string port,
            string sid = "", string serviceName = "",
            string userId = "", string password = "")
        {
            string dataSource = !string.IsNullOrEmpty(serviceName)
                ? $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={serviceName})))"
                : $"(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port})))(CONNECT_DATA=(SID={sid})))";
            return $"DATA SOURCE={dataSource};PERSIST SECURITY INFO=True;USER ID={userId};PASSWORD={password};";
        }
        #endregion

        #region 資料表操作

        public bool CheckHasTable(string tableName)
        {
            string upper = tableName.ToUpper();
            return QueryScalar<int>(
                "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = '" + upper + "'") > 0;
        }

        public void CreateParamTable<TParameter>(string tableName)
        {
            tableName = tableName.ToUpper();
            if (CheckHasTable(tableName))
            {
                Logging.Info($"[OracleDBCtrl] Table {tableName} already exists.");
                return;
            }
            Execute(new ClassInfo(typeof(TParameter)).ParamTableCreateCmd(tableName));
            Logging.Info($"[OracleDBCtrl] Created param table: {tableName}");
        }

        public void CreateResultTable<TVariable>(string tableName)
        {
            tableName = tableName.ToUpper();
            if (CheckHasTable(tableName))
            {
                Logging.Info($"[OracleDBCtrl] Table {tableName} already exists.");
                return;
            }
            Execute(new ClassInfo(typeof(TVariable)).VarTableCreateCmd(tableName));
            Logging.Info($"[OracleDBCtrl] Created result table: {tableName}");
        }

        public void DropTable(string tableName)
        {
            tableName = tableName.ToUpper();
            if (!CheckHasTable(tableName))
            {
                Logging.Info($"[OracleDBCtrl] Table not found: {tableName}");
                return;
            }
            Execute($"DROP TABLE {tableName}");
            Logging.Info($"[OracleDBCtrl] Dropped: {tableName}");
        }

        public void DeleteTable(string tableName, params string[] conditions)
        {
            if (conditions == null || conditions.Length == 0)
            {
                Logging.Warn("[OracleDBCtrl] DeleteTable requires at least one condition.");
                return;
            }
            string where = string.Join(" AND ", conditions.Select(c => c.ToUpper()));
            Execute($"DELETE FROM {tableName.ToUpper()} WHERE 1=1 AND {where}");
        }

        public void TruncateTable(string tableName)
            => Execute($"TRUNCATE TABLE {tableName.ToUpper()}");

        #endregion

        #region 資料讀取

        public List<string> ReadStrSet(string sql) => ReadColumn(sql, r => r.ItemArray[0].ToString());
        public List<double> ReadDoubleSet(string sql) => ReadColumn(sql, r => double.Parse(r.ItemArray[0].ToString()));
        public List<int> ReadIntSet(string sql) => ReadColumn(sql, r => int.Parse(r.ItemArray[0].ToString()));
        public List<DateTime> ReadDateSet(string sql) => ReadColumn(sql, r => DateTime.Parse(r.ItemArray[0].ToString()));

        public List<string> ReadSet(string columnName, string tableName)
            => ReadStrSet($"SELECT DISTINCT {columnName.ToUpper()} FROM {tableName.ToUpper()} ORDER BY 1");

        private List<TValue> ReadColumn<TValue>(string sql, Func<DataRow, TValue> selector)
            => Query(sql).Rows.Cast<DataRow>().Select(selector).ToList();

        public List<TParameter> BuildParameter<TParameter>(string sql)
        {
            return Query(sql).Rows.Cast<DataRow>().Select(row =>
            {
                string combined = "@" + string.Join("@", row.ItemArray.Select(o => o.ToString()));
                string[] parts = combined.Split('@').Skip(1).ToArray();
                return (TParameter)Activator.CreateInstance(typeof(TParameter), new object[] { parts });
            }).ToList();
        }

        public List<TParameter> BuildParameter<TParameter>(string[] columnNames, string tableName)
            => BuildParameter<TParameter>($"SELECT {string.Join(",", columnNames)} FROM {tableName.ToUpper()}");

        #endregion

        #region 解結果寫入

        public void SaveToDB<TVariable>(ISolverEngine engine, string dataId, string tableName, string userId)
        {
            tableName = tableName.ToUpper();
            var classInfo = new ClassInfo(typeof(TVariable));
            var solution = engine.GetSolution(classInfo.TypeName);
            string insertCmd = classInfo.VarInsertCmd(tableName);

            var dataIds = new List<string>();
            var varTypes = new List<string>();
            var qtys = new List<double>();
            var userIds = new List<string>();
            var setCols = classInfo.SetNames.Select(_ => new List<object>()).ToList();

            foreach (var kv in solution)
            {
                string[] parts = kv.Key.Split('@');
                dataIds.Add(dataId);
                varTypes.Add(parts[0].ToUpper());
                qtys.Add(kv.Value);
                userIds.Add(userId);
                for (int i = 0; i < classInfo.SetNames.Length; i++)
                {
                    string raw = i + 1 < parts.Length ? parts[i + 1] : "";
                    setCols[i].Add(ConvertToDbType(classInfo.PropertyTypes[i], raw));
                }
            }

            var columns = new List<(string name, OracleDbType type, object[] values)>
            {
                (":DATA_ID", OracleDbType.Varchar2, dataIds.Cast<object>().ToArray()),
                (":VAR_TYPE", OracleDbType.Varchar2, varTypes.Cast<object>().ToArray()),
                (":QTY", OracleDbType.Double, qtys.Cast<object>().ToArray()),
                (":USER_ID", OracleDbType.Varchar2, userIds.Cast<object>().ToArray())
            };
            for (int i = 0; i < classInfo.SetNames.Length; i++)
            {
                OracleDbType dbType =
                    classInfo.PropertyTypes[i] == typeof(string) ? OracleDbType.Varchar2 :
                    classInfo.PropertyTypes[i] == typeof(DateTime) ? OracleDbType.Date : OracleDbType.Double;
                columns.Add(($":{classInfo.SetNames[i]}", dbType, setCols[i].ToArray()));
            }

            ExecuteArrayBind(insertCmd, dataIds.Count, columns);
            Logging.Info($"[OracleDBCtrl] SaveToDB {classInfo.TypeName} -> {tableName} ({dataIds.Count} rows)");
        }

        /// <summary>
        /// 同一句 SQL 套用多列參數，一次用 array-bind 送出——OracleSolutionSink 批次寫入的實作路徑。
        /// 與 SaveToDB 共用下方 ExecuteArrayBind，差別只在型別來源：SaveToDB 型別已知（走 ClassInfo），
        /// 這裡沒有型別資訊，用每欄首個非 null 值推斷 OracleDbType。
        /// </summary>
        public override void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows)
        {
            if (rows == null || rows.Count == 0) return;

            string[] names = rows[0].Select(p => p.name).ToArray();
            var columns = new List<(string name, OracleDbType type, object[] values)>();
            for (int col = 0; col < names.Length; col++)
            {
                object[] values = rows.Select(r => r[col].value ?? DBNull.Value).ToArray();
                columns.Add((names[col], InferOracleDbType(values), values));
            }

            ExecuteArrayBind(sql, rows.Count, columns);
            Logging.Info($"[OracleDBCtrl] ExecuteBatch ({rows.Count} row(s))");
        }

        // SaveToDB 與 ExecuteBatch 共用的 array-bind 送出邏輯：建 command、設 ArrayBindCount、
        // 掛 ambient transaction（若有）、逐欄加參數、送出。避免兩套 array-bind 各自維護一份。
        private void ExecuteArrayBind(string sql, int rowCount,
            IEnumerable<(string name, OracleDbType type, object[] values)> columns)
        {
            var conn = AcquireConnection(out bool owned);
            try
            {
                using var cmd = new OracleCommand(sql, conn)
                {
                    BindByName = true,
                    ArrayBindCount = rowCount
                };
                if (AmbientTransactionOracle != null) cmd.Transaction = AmbientTransactionOracle;
                foreach (var (name, type, values) in columns)
                    cmd.Parameters.Add(name, type, values, ParameterDirection.Input);

                cmd.Prepare();
                cmd.ExecuteNonQuery();
            }
            finally
            {
                if (owned) conn.Dispose();
            }
        }

        private static OracleDbType InferOracleDbType(object[] values)
        {
            object sample = values.FirstOrDefault(v => v != null && v != DBNull.Value);
            return sample switch
            {
                DateTime _ => OracleDbType.Date,
                int _ => OracleDbType.Int32,
                double _ => OracleDbType.Double,
                _ => OracleDbType.Varchar2
            };
        }

        // internal：OracleSolutionSink 的批次寫入路徑（走 IDbCtrl.ExecuteBatch）共用同一套型別轉換規則。
        internal static object ConvertToDbType(Type t, string raw)
        {
            if (t == typeof(string)) return raw.ToUpper();
            if (t == typeof(double)) return double.TryParse(raw, out double d) ? (object)d : DBNull.Value;
            if (t == typeof(int)) return int.TryParse(raw, out int n) ? (object)n : DBNull.Value;
            if (t == typeof(DateTime)) return DateTime.TryParse(raw, out DateTime dt) ? (object)dt : DBNull.Value;
            return DBNull.Value;
        }

        #endregion
    }

    /// <summary>
    /// Oracle 解輸出：依賴 IDbCtrl 介面（非具體型別 OracleDBCtrl），可注入假物件單元測試交易/批次語意。
    /// 單一變數型別的寫入走 IDbCtrl.ExecuteBatch，一次把該型別的所有列 array-bind 送出——
    /// MILP 解動輒數十萬～百萬列，逐列 INSERT 會讓輸出從秒級退化成分鐘級，故不走 Execute 逐列。
    /// 讓 transaction 期間可共用 ambient 連線，batch 的 commit/rollback 語意也能用假 IDbCtrl 驗證。
    /// 與 CsvSolutionSink 同介面，換輸出目的地不動求解端 code。
    /// </summary>
    public sealed class OracleSolutionSink : ISolutionSink
    {
        private readonly IDbCtrl _db;
        private readonly string _tableName;

        public OracleSolutionSink(IDbCtrl db, string tableName)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        }

        public void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null)
            => WriteRows<TVariableClass>(_db, engine, dataId ?? "", userId ?? "");

        /// <summary>開一個批次：多變數型別的寫入先緩衝，Commit() 時把全部緩衝包進單一 ExecuteInTransaction 原子寫入。</summary>
        public ISolutionBatch BeginBatch(string dataId = null, string userId = null)
            => new OracleSolutionBatch(this, dataId ?? "", userId ?? "");

        // 單一變數型別的實際寫入：把所有列組好參數後一次呼叫 ctrl.ExecuteBatch（array-bind），
        // 與既有 SaveToDB 產出相同的 INSERT 語意，但不再逐列往返。
        private void WriteRows<TVariableClass>(IDbCtrl ctrl, ISolverEngine engine, string dataId, string userId)
        {
            var classInfo = new ClassInfo(typeof(TVariableClass));
            var solution = engine.GetSolution(classInfo.TypeName);
            string insertCmd = classInfo.VarInsertCmd(_tableName.ToUpper());

            var rows = new List<(string name, object value)[]>();
            foreach (var kv in solution)
            {
                string[] parts = kv.Key.Split('@');
                var parameters = new List<(string name, object value)>
                {
                    (":DATA_ID", dataId),
                    (":VAR_TYPE", parts[0].ToUpper())
                };
                for (int i = 0; i < classInfo.SetNames.Length; i++)
                {
                    string raw = i + 1 < parts.Length ? parts[i + 1] : "";
                    parameters.Add(($":{classInfo.SetNames[i]}", OracleDBCtrl.ConvertToDbType(classInfo.PropertyTypes[i], raw)));
                }
                parameters.Add((":QTY", kv.Value));
                parameters.Add((":USER_ID", userId));

                rows.Add(parameters.ToArray());
            }

            if (rows.Count > 0)
                ctrl.ExecuteBatch(insertCmd, rows);

            Logging.Info($"[OracleSolutionSink] Write {classInfo.TypeName} -> {_tableName} ({solution.Count} rows)");
        }

        /// <summary>
        /// 單一輸出 transaction 的批次：Write 只緩衝、Commit 時才把全部緩衝放進單一 ExecuteInTransaction 執行。
        /// 未 Commit 即 Dispose → 捨棄緩衝、完全不寫（不是寫了再回滾）。
        /// </summary>
        private sealed class OracleSolutionBatch : ISolutionBatch
        {
            private readonly OracleSolutionSink _sink;
            private readonly string _dataId;
            private readonly string _userId;
            private readonly List<Action<IDbCtrl>> _pending = new List<Action<IDbCtrl>>();
            private bool _committed;

            public OracleSolutionBatch(OracleSolutionSink sink, string dataId, string userId)
            {
                _sink = sink;
                _dataId = dataId;
                _userId = userId;
            }

            public void Write<TVariableClass>(ISolverEngine engine)
            {
                if (_committed)
                    throw new InvalidOperationException("[OracleSolutionSink] 批次已 Commit，不可再 Write。");
                _pending.Add(ctrl => _sink.WriteRows<TVariableClass>(ctrl, engine, _dataId, _userId));
            }

            public void Commit()
            {
                if (_committed)
                    throw new InvalidOperationException("[OracleSolutionSink] 批次已 Commit，不可重複 Commit。");
                _sink._db.ExecuteInTransaction(ctrl =>
                {
                    foreach (var write in _pending) write(ctrl);
                });
                _committed = true;
            }

            public void Dispose()
            {
                _pending.Clear();
            }
        }
    }
}
