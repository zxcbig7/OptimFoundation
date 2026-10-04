using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Oracle.ManagedDataAccess.Client;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;

namespace OptimFoundation.Db.Oracle
{
    /// <summary>
    /// Oracle 資料存取；交易期間共用連線，同一實例不可並行執行交易。
    /// </summary>
    public sealed class OracleDbCtrl : DbCtrlBase
    {
        /// <summary>只記下連線字串；實際連線在每次操作時才由 connection pool 取得。</summary>
        public OracleDbCtrl(string connectionString) : base(connectionString) { }

        #region IDbCtrl 基本操作


        /// <summary>不做任何事；每次操作會自行取得連線，不需預先開啟。</summary>
        public override void Open() { }

        /// <summary>不做任何事；操作或交易結束時會自行歸還連線。</summary>
        public override void Close() { }

        /// <summary>執行查詢並回傳 DataTable。若正在 ExecuteInTransaction 中，會沿用該次交易的連線。</summary>
        public override DataTable Query(string sql, params (string name, object value)[] parameters)
        {
            return AtPublicBoundary(nameof(Query), sql, () =>
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
            });
        }

        /// <summary>執行 INSERT / UPDATE / DELETE / DDL 並回傳受影響列數（會寫一行 log）。</summary>
        public override int Execute(string sql, params (string name, object value)[] parameters)
        {
            return AtPublicBoundary(nameof(Execute), sql, () =>
            {
                var conn = AcquireConnection(out bool owned);
                try
                {
                    using var cmd = BuildCommand(sql, conn, AmbientTransactionOracle, parameters);
                    int rows = cmd.ExecuteNonQuery();
                    Logging.Info($"[Oracle 執行完成] 數量={rows}");
                    return rows;
                }
                finally
                {
                    if (owned) conn.Dispose();
                }
            });
        }

        /// <summary>取第一列第一欄，再用 Convert.ChangeType 轉成 TResult；空結果或無法轉型時，回傳 null 或拋例外取決於 TResult。</summary>
        public override TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters)
        {
            return AtPublicBoundary(nameof(QueryScalar), sql, () =>
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
            });
        }

        private T AtPublicBoundary<T>(string context, object value, Func<T> action)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "Oracle 操作失敗", null, context, value,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        private void AtPublicBoundary(string context, object value, Action action)
            => AtPublicBoundary(context, value, () =>
            {
                action();
                return true;
            });

        /// <summary>建立並開啟 Oracle 連線，供基底類別的 ExecuteInTransaction 在整個交易期間共用。</summary>
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
        /// 交易內借用共用連線（owned=false）；否則新建連線（owned=true），由本次操作釋放。
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

        // OracleCommand 與 array-bind 需要 OracleTransaction。
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

        /// <summary>
        /// 組 Oracle 連線字串：給了 serviceName 走 SERVICE_NAME 格式，否則走 SID 格式。
        /// 產出的字串含明文密碼，請勿寫入 log、提交到 repo 或放進設定檔範本。
        /// </summary>
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

        /// <summary>
        /// 查 USER_TABLES 判斷表是否存在（只看目前 schema）。
        /// tableName 會轉大寫並直接拼進 SQL，只能傳入程式內部決定的表名，不可傳使用者輸入。
        /// </summary>
        public bool CheckHasTable(string tableName)
        {
            string upper = tableName.ToUpper();
            return QueryScalar<int>(
                "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = '" + upper + "'") > 0;
        }

        /// <summary>
        /// 依參數類別的 property 建參數表（欄位 = DATA_ID + 各 property + USER_ID + TIME）。
        /// 表已存在則只寫 log 直接返回，不會改既有結構。
        /// </summary>
        public void CreateParamTable<TParameter>(string tableName)
        {
            tableName = tableName.ToUpper();
            if (CheckHasTable(tableName))
            {
                Logging.Info($"[資料表已存在] 參數資料表 | 名稱={tableName}");
                return;
            }
            Execute(new ClassInfo(typeof(TParameter)).ParamTableCreateCmd(tableName));
            Logging.Info($"[資料表建立完成] 參數資料表 | 名稱={tableName}");
        }

        /// <summary>
        /// 依變數類別建解結果表（欄位 = DATA_ID + VAR_TYPE + 各維度 + QTY + USER_ID + TIME），對得上 SaveToDB 的 INSERT。
        /// 表已存在則只寫 log 直接返回。
        /// </summary>
        public void CreateResultTable<TVariable>(string tableName)
        {
            tableName = tableName.ToUpper();
            if (CheckHasTable(tableName))
            {
                Logging.Info($"[資料表已存在] 參數資料表 | 名稱={tableName}");
                return;
            }
            Execute(new ClassInfo(typeof(TVariable)).VarTableCreateCmd(tableName));
            Logging.Info($"[資料表建立完成] 結果資料表 | 名稱={tableName}");
        }

        /// <summary>
        /// 執行 DROP TABLE，刪除整張表及其結構，無法回復；DDL 不受交易保護。
        /// 表不存在時只寫 log 不報錯。
        /// </summary>
        public void DropTable(string tableName)
        {
            tableName = tableName.ToUpper();
            if (!CheckHasTable(tableName))
            {
                Logging.Info($"[資料表找不到] 名稱={tableName}");
                return;
            }
            Execute($"DROP TABLE {tableName}");
            Logging.Info($"[資料表刪除完成] 名稱={tableName}");
        }

        /// <summary>
        /// 依條件刪除資料列，保留表結構。conditions 以 AND 串接，轉大寫後直接拼入 WHERE。
        /// 未提供條件時只記錄警告並略過；要清空整表請用 <see cref="TruncateTable"/>。
        /// </summary>
        /// <param name="tableName">目標表名（自動轉大寫）。</param>
        /// <param name="conditions">例如 "DATA_ID = 'RUN1'"；須由程式內部產生，不可直接使用外部輸入。</param>
        public void DeleteTable(string tableName, params string[] conditions)
        {
            if (conditions == null || conditions.Length == 0)
            {
                Logging.Warn("[刪除條件為空] 未執行資料刪除 | 原因=沒有刪除條件 結果=略過");
                return;
            }
            string where = string.Join(" AND ", conditions.Select(c => c.ToUpper()));
            Execute($"DELETE FROM {tableName.ToUpper()} WHERE 1=1 AND {where}");
        }

        /// <summary>
        /// 執行 TRUNCATE，無條件清空整表資料且無法回復；DDL 不受交易保護。
        /// </summary>
        public void TruncateTable(string tableName)
            => Execute($"TRUNCATE TABLE {tableName.ToUpper()}");

        #endregion

        #region 解結果寫入

        /// <summary>
        /// 把某變數型別的整組解寫進結果表：解 key（TypeName@s1@s2@…）拆成各維度欄，值寫進 QTY，
        /// 全部列一次 array-bind 送出（不逐列 INSERT）。表結構需先由 <see cref="CreateResultTable{TVariable}"/> 建好。
        /// </summary>
        /// <param name="engine">已求解成功的引擎。</param>
        /// <param name="dataId">本次寫入的批次識別，用於之後查詢 / 刪除同一批資料。</param>
        /// <param name="tableName">目標結果表名（自動轉大寫）。</param>
        /// <param name="userId">寫入者識別，存進 USER_ID 欄。</param>
        /// <exception cref="FormatException">維度值無法轉成對應 property 型別；整批中止，不寫入資料。</exception>
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
            Logging.Info($"[資料表寫出完成] 型別={classInfo.TypeName} 名稱={tableName} 數量={dataIds.Count}");
        }

        /// <summary>
        /// 以 array-bind 批次送出 SQL 參數；OracleDbType 取各欄首個非 null 值的型別。
        /// </summary>
        public override void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows)
        {
            if (rows == null || rows.Count == 0) return;
            AtPublicBoundary(nameof(ExecuteBatch), sql, () =>
            {
                string[] names = rows[0].Select(p => p.name).ToArray();
                var columns = new List<(string name, OracleDbType type, object[] values)>();
                for (int col = 0; col < names.Length; col++)
                {
                    object[] values = rows.Select(r => r[col].value ?? DBNull.Value).ToArray();
                    columns.Add((names[col], InferOracleDbType(values), values));
                }

                ExecuteArrayBind(sql, rows.Count, columns);
                Logging.Info($"[Oracle 批次執行完成] 數量={rows.Count}");
            });
        }

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

        internal static object ConvertToDbType(Type t, string raw)
        {
            if (t == typeof(string)) return raw.ToUpper();
            if (t == typeof(double) && double.TryParse(raw, out double d)) return d;
            if (t == typeof(int) && int.TryParse(raw, out int n)) return n;
            if (t == typeof(DateTime))
            {
                if (DateTime.TryParseExact(raw, ModelNaming.DateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime modelDate))
                    return modelDate;
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dataDate))
                    return dataDate;
            }

            string message = $"Oracle 資料轉型失敗：無法將 '{raw}' 轉成 {t.FullName}";
            throw Logging.ErrorOnce(
                new FormatException(message),
                "Oracle 資料轉型失敗", null, nameof(ConvertToDbType), raw,
                "不支援或不合法的值", $"型別={t.FullName}");
        }

        #endregion
    }

    /// <summary>
    /// 透過 IDbCtrl 批次寫入各變數型別的解；BeginBatch 可將多型別寫入合併為同一交易。
    /// </summary>
    public sealed class OracleSolutionSink : ISolutionSink
    {
        private readonly IDbCtrl _db;
        private readonly string _tableName;

        /// <summary>指定寫入用的 IDbCtrl 與目標表名（表需已存在）。</summary>
        /// <exception cref="ArgumentNullException">db 或 tableName 為 null。</exception>
        public OracleSolutionSink(IDbCtrl db, string tableName)
        {
            _db = db ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(db), "db 不得為 null"),
                "Oracle 解寫出設定不合法", null, nameof(OracleSolutionSink), null, "資料庫為空");
            _tableName = tableName ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(tableName), "tableName 不得為 null"),
                "Oracle 解寫出設定不合法", null, nameof(OracleSolutionSink), null, "資料表名稱為空");
        }

        /// <summary>
        /// 立即寫入單一型別，不另開交易；多型別需共同提交時使用 <see cref="BeginBatch"/>。
        /// </summary>
        public void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null)
        {
            try
            {
                WriteRows<TVariableClass>(_db, engine, dataId ?? "", userId ?? "");
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "Oracle 解寫出失敗", null, nameof(WriteSolution), typeof(TVariableClass).Name,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        /// <summary>開始一批輸出。先記住要寫入哪些變數型別，Commit() 時在同一交易內讀取解值並寫入，失敗則整批回滾。</summary>
        public ISolutionBatch BeginBatch(string dataId = null, string userId = null)
            => new OracleSolutionBatch(this, dataId ?? "", userId ?? "");

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
                    parameters.Add(($":{classInfo.SetNames[i]}", OracleDbCtrl.ConvertToDbType(classInfo.PropertyTypes[i], raw)));
                }
                parameters.Add((":QTY", kv.Value));
                parameters.Add((":USER_ID", userId));

                rows.Add(parameters.ToArray());
            }

            if (rows.Count > 0)
                ctrl.ExecuteBatch(insertCmd, rows);

            Logging.Info($"[Oracle 解寫出完成] 變數類別={classInfo.TypeName} 名稱={_tableName} 數量={solution.Count}");
        }

        /// <summary>
        /// Write 只記錄待執行的寫入操作；Commit 時才在同一交易內讀取各型別解值並寫入。
        /// 未 Commit 就 Dispose 時，只捨棄待寫操作，不會寫入資料。
        /// </summary>
        private sealed class OracleSolutionBatch : ISolutionBatch
        {
            private readonly OracleSolutionSink _sink;
            private readonly string _dataId;
            private readonly string _userId;
            private readonly List<Action<IDbCtrl>> _pending = new List<Action<IDbCtrl>>();
            private bool _committed;

            /// <summary>記下 sink 與整批共用的 dataId / userId。</summary>
            public OracleSolutionBatch(OracleSolutionSink sink, string dataId, string userId)
            {
                _sink = sink;
                _dataId = dataId;
                _userId = userId;
            }

            /// <summary>記住此變數型別與引擎；Commit 時才讀取解值並寫入資料庫，因此引擎須保留到 Commit 完成。</summary>
            /// <exception cref="InvalidOperationException">批次已 Commit。</exception>
            public void Write<TVariableClass>(ISolverEngine engine)
            {
                if (_committed)
                    throw Logging.ErrorOnce(
                        new InvalidOperationException("批次已提交，不可再 Write"),
                        "Oracle 批次操作不合法", null, nameof(Write), typeof(TVariableClass).Name,
                        "批次已提交");
                _pending.Add(ctrl => _sink.WriteRows<TVariableClass>(ctrl, engine, _dataId, _userId));
            }

            /// <summary>在同一交易執行所有待寫操作；全部成功才提交，任一失敗則整批回滾。</summary>
            /// <exception cref="InvalidOperationException">重複 Commit。</exception>
            public void Commit()
            {
                if (_committed)
                    throw Logging.ErrorOnce(
                        new InvalidOperationException("批次已提交，不可重複 Commit"),
                        "Oracle 批次操作不合法", null, nameof(Commit), _pending.Count,
                        "批次已提交");
                try
                {
                    _sink._db.ExecuteInTransaction(ctrl =>
                    {
                        foreach (var write in _pending) write(ctrl);
                    });
                    _committed = true;
                }
                catch (Exception ex)
                {
                    Logging.ErrorOnce(ex, "Oracle 批次操作失敗", null, nameof(Commit), _pending.Count,
                        ex.GetBaseException().Message);
                    throw;
                }
            }

            /// <summary>清除待寫操作；未 Commit 的資料不會寫入資料庫。</summary>
            public void Dispose()
            {
                _pending.Clear();
            }
        }
    }
}
