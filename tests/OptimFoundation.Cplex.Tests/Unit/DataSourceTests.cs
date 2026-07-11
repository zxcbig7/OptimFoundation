using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
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
        public void ReadParameters_ReturnsRegisteredRows()
        {
            var src = new InMemoryDataSource()
                .AddParameters(new[] { new DsParam { Lot = "L1", Eqp = "E1", QTY = 3 } });

            var rows = src.ReadParameters<DsParam>();

            Assert.Single(rows);
            Assert.Equal("L1", rows[0].Lot);
            Assert.Equal(3, rows[0].QTY);
        }

        [Fact]
        public void ReadParameters_Unregistered_ThrowsWithTypeName()
        {
            var src = new InMemoryDataSource();
            var ex = Assert.Throws<KeyNotFoundException>(() => src.ReadParameters<DsParam>());
            Assert.Contains("DsParam", ex.Message);
        }

        [Fact]
        public void ReadSet_RegisteredAndCaseInsensitive()
        {
            var src = new InMemoryDataSource().AddSet("Employee", new[] { "A", "B" });

            Assert.Equal(new[] { "A", "B" }, src.ReadSet("employee"));
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
        public void ReadParameters_UsesTypeNameFile_HeaderAware()
        {
            WriteDataFile("DsParam.csv", "QTY,EQP,LOT\n5,E2,L2\n");

            var rows = new CsvDataSource().ReadParameters<DsParam>();

            Assert.Single(rows);
            Assert.Equal("L2", rows[0].Lot);
            Assert.Equal("E2", rows[0].Eqp);
            Assert.Equal(5, rows[0].QTY);
        }

        [Fact]
        public void ReadSet_AddsSetPrefixConvention()
        {
            WriteDataFile("Set_Machine.csv", "M1\nM2\n");

            // 邏輯名稱與完整檔名兩種呼叫皆可
            Assert.Equal(new[] { "M1", "M2" }, new CsvDataSource().ReadSet("Machine"));
            Assert.Equal(new[] { "M1", "M2" }, new CsvDataSource().ReadSet("Set_Machine"));
        }
    }

    // ── DbDataSource（FakeDbCtrl 回手工 DataTable，不需真 DB）────────────

    public class FakeDbCtrl : IDbCtrl
    {
        public string LastSql = string.Empty;
        public (string name, object value)[] LastParameters = Array.Empty<(string, object)>();
        public DataTable NextResult = new DataTable();

        public DataTable Query(string sql, params (string name, object value)[] parameters)
        {
            LastSql = sql;
            LastParameters = parameters;
            return NextResult;
        }

        public void Open() { }
        public void Close() { }
        public void NonQuery(string sql, params (string name, object value)[] parameters) { }
        public int Execute(string sql, params (string name, object value)[] parameters) => 0;
        public TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters) => default;
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

        [Fact]
        public void ReadParameters_MapsByColumnName_IgnoresExtras()
        {
            var fake = new FakeDbCtrl { NextResult = ParamTable() };

            var rows = new DbDataSource(fake).ReadParameters<DsParam>();

            Assert.Single(rows);
            Assert.Equal("L9", rows[0].Lot);
            Assert.Equal("E9", rows[0].Eqp);
            Assert.Equal(7, rows[0].QTY);
            Assert.Equal("SELECT * FROM DSPARAM", fake.LastSql);
        }

        [Fact]
        public void ReadParameters_WithDataId_AddsWhereAndBind()
        {
            var fake = new FakeDbCtrl { NextResult = ParamTable() };

            new DbDataSource(fake, tablePrefix: "OPT_", dataId: "D1").ReadParameters<DsParam>();

            Assert.Equal("SELECT * FROM OPT_DSPARAM WHERE DATA_ID = :DATA_ID", fake.LastSql);
            Assert.Equal("D1", fake.LastParameters.Single().value);
        }

        [Fact]
        public void ReadParameters_MissingColumn_Throws()
        {
            var dt = new DataTable();
            dt.Columns.Add("LOT");
            dt.Columns.Add("QTY"); // 缺 EQP
            var fake = new FakeDbCtrl { NextResult = dt };

            var ex = Assert.Throws<InvalidDataException>(() => new DbDataSource(fake).ReadParameters<DsParam>());
            Assert.Contains("Eqp", ex.Message);
        }

        [Fact]
        public void ReadSet_WithoutResolver_ThrowsNotSupported()
        {
            var ex = Assert.Throws<NotSupportedException>(() => new DbDataSource(new FakeDbCtrl()).ReadSet("Machine"));
            Assert.Contains("setSqlResolver", ex.Message);
        }

        [Fact]
        public void ReadSet_WithResolver_ReturnsColumn()
        {
            var dt = new DataTable();
            dt.Columns.Add("EQP");
            dt.Rows.Add("E1");
            dt.Rows.Add("E2");
            var fake = new FakeDbCtrl { NextResult = dt };

            var src = new DbDataSource(fake, setSqlResolver: n => $"SELECT DISTINCT {n} FROM T");

            Assert.Equal(new[] { "E1", "E2" }, src.ReadSet("EQP"));
        }
    }
}
