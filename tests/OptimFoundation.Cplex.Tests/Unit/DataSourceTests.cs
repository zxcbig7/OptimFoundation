using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // 測試用參數類（QTY 最後，對齊 canonical schema）
    public class DsParam : ParameterBase
    {
        public string Lot { get; set; } = string.Empty;
        public string Eqp { get; set; } = string.Empty;
        public double QTY { get; set; }
    }

    // ── InMemoryDataSource ─────────────────────────────────────────────

    public class InMemoryDataSourceTests
    {
        [Fact]
        public void LoadParam_ReturnsRegisteredRows()
        {
            var src = new InMemoryDataSource()
                .AddParameters(new[] { new DsParam { Lot = "L1", Eqp = "E1", QTY = 3 } });

            var rows = src.LoadParam<DsParam>();

            Assert.Single(rows);
            Assert.Equal("L1", rows[0].Lot);
            Assert.Equal(3, rows[0].QTY);
        }

        [Fact]
        public void LoadParam_Unregistered_ThrowsWithTypeName()
        {
            var src = new InMemoryDataSource();
            var ex = Assert.Throws<KeyNotFoundException>(() => src.LoadParam<DsParam>());
            Assert.Contains("DsParam", ex.Message);
        }

        [Fact]
        public void LoadSet_RegisteredAndCaseInsensitive()
        {
            var src = new InMemoryDataSource().AddSet("Employee", new[] { "A", "B" });

            Assert.Equal(new[] { "A", "B" }, src.LoadSet("employee"));
        }

        [Fact]
        public void LoadRows_ReturnsEveryRegisteredColumn()
        {
            var src = new InMemoryDataSource()
                .AddRows("Arc", new[] { new[] { "A", "B" }, new[] { "B", "C" } });

            var rows = src.LoadRows("arc").ToArray();

            Assert.Equal(new[] { "A", "B" }, rows[0]);
            Assert.Equal(new[] { "B", "C" }, rows[1]);
        }
    }

    // ── CsvDataSource ──────────────────────────────────────────────────

    public class CsvDataSourceTests : IDisposable
    {
        private readonly List<string> _files = new List<string>();

        private void WriteDataFile(string fileName, string content)
        {
            FolderDir.Data.CreateFolder();
            string path = FolderDir.Data.GetFilePath(fileName);
            File.WriteAllText(path, content, new UTF8Encoding(true));
            _files.Add(path);
        }

        public void Dispose()
        {
            foreach (var f in _files) if (File.Exists(f)) File.Delete(f);
        }

        [Fact]
        public void Ctor_EnsuresDataFolderExists()
        {
            // 引用 CSV 來源即備好 Data/（即使空的）——輸入側與輸出側對稱
            new CsvDataSource();
            Assert.True(Directory.Exists(FolderDir.Data.GetPath()));
        }

        [Fact]
        public void LoadParam_UsesTypeNameFile_HeaderAware()
        {
            WriteDataFile("DsParam.csv", "QTY,EQP,LOT\n5,E2,L2\n");

            var rows = new CsvDataSource().LoadParam<DsParam>();

            Assert.Single(rows);
            Assert.Equal("L2", rows[0].Lot);
            Assert.Equal("E2", rows[0].Eqp);
            Assert.Equal(5, rows[0].QTY);
        }

        [Fact]
        public void LoadSet_AddsSetPrefixConvention()
        {
            WriteDataFile("Set_Machine.csv", "M1\nM2\n");

            // 邏輯名稱與完整檔名兩種呼叫皆可
            Assert.Equal(new[] { "M1", "M2" }, new CsvDataSource().LoadSet("Machine"));
            Assert.Equal(new[] { "M1", "M2" }, new CsvDataSource().LoadSet("Set_Machine"));
        }

        [Fact]
        public void LoadRows_ParsesQuotedMultilineFields()
        {
            WriteDataFile("Arc.csv", "A,B\n\"North\nDepot\",C\n");

            var rows = new CsvDataSource().LoadRows("Arc").ToArray();

            Assert.Equal(new[] { "A", "B" }, rows[0]);
            Assert.Equal(new[] { "North\nDepot", "C" }, rows[1]);
        }
    }

    // ── CsvSolutionSink batch（無交易語意，Write 直接寫檔、Commit/Dispose 皆 no-op）──
    // 驗證需求 5：batch 產生的檔案內容須與直接呼叫 WriteSolution 逐字相同（CSV 端零行為變更）。
    public class CsvSolutionSinkTests : IDisposable
    {
        private readonly string _file;

        public CsvSolutionSinkTests()
        {
            FolderDir.Solution.CreateFolder();
            _file = FolderDir.Solution.GetFilePath($"{typeof(VarS).Name}.csv");
        }

        public void Dispose()
        {
            if (File.Exists(_file)) File.Delete(_file);
        }

        [Fact]
        public void Batch_ProducesSameFileContent_AsDirectWriteSolution()
        {
            var engineDirect = new MockEngine();
            engineDirect.BuildBVs<VarS>(new[] { "E1", "E2" });
            new CsvSolutionSink().WriteSolution<VarS>(engineDirect, "D1", "U1");
            string direct = File.ReadAllText(_file);
            File.Delete(_file);

            var engineBatch = new MockEngine();
            engineBatch.BuildBVs<VarS>(new[] { "E1", "E2" });
            using (var batch = new CsvSolutionSink().BeginBatch("D1", "U1"))
            {
                batch.Write<VarS>(engineBatch);
                batch.Commit();
            }
            string viaBatch = File.ReadAllText(_file);

            Assert.Equal(direct, viaBatch);
        }
    }

    // ── DbDataSource（FakeDbCtrl 回手工 DataTable，不需真 DB）────────────

    public class FakeDbCtrl : IDbCtrl
    {
        public string LastSql = string.Empty;
        public (string name, object value)[] LastParameters = Array.Empty<(string, object)>();
        public DataTable NextResult = new DataTable();

        // ── ExecuteInTransaction / Execute / ExecuteBatch 的假交易語意（供 OracleSolutionSink 批次測試）──
        // 記錄實際送出的每筆 Execute 呼叫（sql + bind 參數），供斷言「幾筆寫入」。
        public List<(string sql, (string name, object value)[] parameters)> ExecutedCommands = new();
        // 記錄實際送出的每次 ExecuteBatch 呼叫（sql + 多列參數），供斷言「批次寫入而非逐列」。
        public List<(string sql, (string name, object value)[][] rows)> ExecutedBatches = new();
        public int ExecuteInTransactionCallCount;
        public bool Committed;
        public bool RolledBack;
        // 設定 >=1 時，第 N 次 Execute 呼叫丟例外，模擬「批次中途失敗」。-1（預設）= 永不丟。
        public int ThrowOnExecuteCallNumber = -1;
        // 設定 >=1 時，第 N 次 ExecuteBatch 呼叫丟例外，模擬「多變數型別批次中途失敗」。-1（預設）= 永不丟。
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
                throw new InvalidOperationException($"[FakeDbCtrl] 模擬第 {ExecutedCommands.Count} 筆寫入失敗。");
            return 0;
        }

        public TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters) => default;

        public void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows)
        {
            ExecutedBatches.Add((sql, rows.ToArray()));
            if (ThrowOnBatchCallNumber == ExecutedBatches.Count)
                throw new InvalidOperationException($"[FakeDbCtrl] 模擬第 {ExecutedBatches.Count} 批次寫入失敗。");
        }

        public void ExecuteInTransaction(Action<IDbCtrl> work)
        {
            ExecuteInTransactionCallCount++;
            try
            {
                work(this);
                Committed = true;
            }
            catch
            {
                RolledBack = true;
                throw;
            }
        }

        public void Dispose() { }
    }

    public class DbDataSourceTests
    {
        private static DataTable ParamTable()
        {
            // 模擬 DB 表：多 DATA_ID/USER_ID 欄、欄序與宣告不同 → 驗證按名對位 + 忽略多餘欄
            var dt = new DataTable();
            dt.Columns.Add("DATA_ID");
            dt.Columns.Add("QTY");
            dt.Columns.Add("EQP");
            dt.Columns.Add("LOT");
            dt.Columns.Add("USER_ID");
            dt.Rows.Add("D1", "7", "E9", "L9", "VIC");
            return dt;
        }

        // DB 是 query-only：LoadParam / LoadSet 第一引數是 SQL，欄名對 property（大小寫不敏感、多餘欄忽略）
        [Fact]
        public void LoadParam_MapsByColumnName_IgnoresExtras()
        {
            var fake = new FakeDbCtrl { NextResult = ParamTable() };

            var rows = new DbDataSource(fake).LoadParam<DsParam>(
                "SELECT lot, eqp, qty FROM demand WHERE data_id = :id", (":id", "D1"));

            Assert.Single(rows);
            Assert.Equal("L9", rows[0].Lot);
            Assert.Equal("E9", rows[0].Eqp);
            Assert.Equal(7, rows[0].QTY);
            Assert.Equal("SELECT lot, eqp, qty FROM demand WHERE data_id = :id", fake.LastSql);
            Assert.Equal("D1", fake.LastParameters.Single().value);
        }

        [Fact]
        public void LoadSet_ReturnsFirstColumn()
        {
            var dt = new DataTable();
            dt.Columns.Add("EQP");
            dt.Rows.Add("E1");
            dt.Rows.Add("E2");
            var fake = new FakeDbCtrl { NextResult = dt };

            var members = new DbDataSource(fake).LoadSet("SELECT DISTINCT eqp FROM t ORDER BY eqp");

            Assert.Equal(new[] { "E1", "E2" }, members);
        }

        [Fact]
        public void LoadRows_ReturnsAllColumnsAsStrings()
        {
            var dt = new DataTable();
            dt.Columns.Add("FROM");
            dt.Columns.Add("TO");
            dt.Rows.Add("A", "B");
            var fake = new FakeDbCtrl { NextResult = dt };

            var rows = new DbDataSource(fake).LoadRows("SELECT from_col, to_col FROM arc").ToArray();

            Assert.Single(rows);
            Assert.Equal(new[] { "A", "B" }, rows[0]);
        }

        [Fact]
        public void LoadParam_MapsTrimmedNumbersUsingInvariantCulture()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var dt = new DataTable();
                dt.Columns.Add("LOT");
                dt.Columns.Add("EQP");
                dt.Columns.Add("QTY");
                dt.Rows.Add(" L1 ", " E1 ", " 1.5 ");

                var rows = new DbDataSource(new FakeDbCtrl { NextResult = dt }).LoadParam<DsParam>("SELECT * FROM demand");

                Assert.Equal("L1", rows[0].Lot);
                Assert.Equal("E1", rows[0].Eqp);
                Assert.Equal(1.5, rows[0].QTY);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
