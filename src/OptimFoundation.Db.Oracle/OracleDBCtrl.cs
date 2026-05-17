using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Oracle.ManagedDataAccess.Client;
using OptimFoundation.Core;
using OptimFoundation.Core.Db;

namespace OptimFoundation.Db.Oracle
{
    public sealed class OracleDBCtrl : DBCtrlBase
    {
        public OracleDBCtrl(string connectionString) : base(connectionString) { }

        #region IDbCtrl 基本操作

        // Open/Close 留空：每次操作自建 connection（Oracle Connection Pool）
        public override void Open() { }
        public override void Close() { }

        public override DataTable Query(string sql, params (string name, object value)[] parameters)
        {
            using var conn = CreateConnection();
            using var cmd = BuildCommand(sql, conn, parameters);
            using var adpt = new OracleDataAdapter(cmd);
            var dt = new DataTable();
            adpt.Fill(dt);
            return dt;
        }

        public override int Execute(string sql, params (string name, object value)[] parameters)
        {
            using var conn = CreateConnection();
            using var cmd = BuildCommand(sql, conn, parameters);
            int rows = cmd.ExecuteNonQuery();
            Logging.Info($"[OracleDBCtrl] Execute ({rows} row(s))");
            return rows;
        }

        public override T QueryScalar<T>(string sql, params (string name, object value)[] parameters)
        {
            using var conn = CreateConnection();
            using var cmd = BuildCommand(sql, conn, parameters);
            object result = cmd.ExecuteScalar();
            return (T)Convert.ChangeType(result, typeof(T));
        }

        #endregion

        #region 連線工具

        private OracleConnection CreateConnection()
        {
            var conn = new OracleConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        private static OracleCommand BuildCommand(string sql, OracleConnection conn,
            (string name, object value)[] parameters)
        {
            var cmd = new OracleCommand(sql, conn) { BindByName = true };
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

        public void CreateParamTable<T>(string tableName)
        {
            tableName = tableName.ToUpper();
            if (CheckHasTable(tableName))
            {
                Logging.Info($"[OracleDBCtrl] Table {tableName} already exists.");
                return;
            }
            Execute(new ClassInfo(typeof(T)).ParamTableCreateCmd(tableName));
            Logging.Info($"[OracleDBCtrl] Created param table: {tableName}");
        }

        public void CreateResultTable<T>(string tableName)
        {
            tableName = tableName.ToUpper();
            if (CheckHasTable(tableName))
            {
                Logging.Info($"[OracleDBCtrl] Table {tableName} already exists.");
                return;
            }
            Execute(new ClassInfo(typeof(T)).VarTableCreateCmd(tableName));
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

        private List<T> ReadColumn<T>(string sql, Func<DataRow, T> selector)
            => Query(sql).Rows.Cast<DataRow>().Select(selector).ToList();

        public List<T> BuildParameter<T>(string sql)
        {
            return Query(sql).Rows.Cast<DataRow>().Select(row =>
            {
                string combined = "@" + string.Join("@", row.ItemArray.Select(o => o.ToString()));
                string[] parts = combined.Split('@').Skip(1).ToArray();
                return (T)Activator.CreateInstance(typeof(T), new object[] { parts });
            }).ToList();
        }

        public List<T> BuildParameter<T>(string[] columnNames, string tableName)
            => BuildParameter<T>($"SELECT {string.Join(",", columnNames)} FROM {tableName.ToUpper()}");

        #endregion

        #region 解結果寫入

        public void SaveToDB<T>(ISolverEngine engine, string dataId, string tableName, string userId)
        {
            tableName = tableName.ToUpper();
            var classInfo = new ClassInfo(typeof(T));
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

            using var conn = CreateConnection();
            using var cmd = new OracleCommand(insertCmd, conn)
            {
                BindByName = true,
                ArrayBindCount = dataIds.Count
            };
            cmd.Parameters.Add(":DATA_ID", OracleDbType.Varchar2, dataIds.ToArray(), ParameterDirection.Input);
            cmd.Parameters.Add(":VAR_TYPE", OracleDbType.Varchar2, varTypes.ToArray(), ParameterDirection.Input);
            cmd.Parameters.Add(":QTY", OracleDbType.Double, qtys.ToArray(), ParameterDirection.Input);
            cmd.Parameters.Add(":USER_ID", OracleDbType.Varchar2, userIds.ToArray(), ParameterDirection.Input);

            for (int i = 0; i < classInfo.SetNames.Length; i++)
            {
                OracleDbType dbType =
                    classInfo.PropertyTypes[i] == typeof(string) ? OracleDbType.Varchar2 :
                    classInfo.PropertyTypes[i] == typeof(DateTime) ? OracleDbType.Date : OracleDbType.Double;
                cmd.Parameters.Add(
                    $":{classInfo.SetNames[i]}", dbType, setCols[i].ToArray(), ParameterDirection.Input);
            }

            cmd.Prepare();
            cmd.ExecuteNonQuery();
            Logging.Info($"[OracleDBCtrl] SaveToDB {classInfo.TypeName} -> {tableName} ({dataIds.Count} rows)");
        }

        private static object ConvertToDbType(Type t, string raw)
        {
            if (t == typeof(string)) return raw.ToUpper();
            if (t == typeof(double)) return double.TryParse(raw, out double d) ? (object)d : DBNull.Value;
            if (t == typeof(int)) return int.TryParse(raw, out int n) ? (object)n : DBNull.Value;
            if (t == typeof(DateTime)) return DateTime.TryParse(raw, out DateTime dt) ? (object)dt : DBNull.Value;
            return DBNull.Value;
        }

        #endregion
    }
}
