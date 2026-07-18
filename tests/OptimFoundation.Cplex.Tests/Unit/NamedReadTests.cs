using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // 測試用積木（模擬 [OptSet] 生成結果：直接繼承 SetBase）
    public class Set_City : SetBase<string> { }

    // Dataload ctor 顯式讀檔：Load(source, name)（set）/ LoadParam(file)（CSV/InMemory 參數）/ DbDataSource.LoadParam(sql)（DB 參數，query-only）
    public class NamedReadTests
    {
        [Fact]
        public void SetLoad_Convention_UsesSetName()
        {
            var src = new InMemoryDataSource().AddSet("City", new[] { "TPE", "KHH" });

            var city = new Set_City();
            city.Load(src);

            Assert.Equal(new[] { "TPE", "KHH" }, city.ToList());
        }

        [Fact]
        public void SetLoad_NameOverride_ReadsSpecifiedSet()
        {
            var src = new InMemoryDataSource().AddSet("Region", new[] { "North" });

            var city = new Set_City();
            city.Load(src, "Region");   // 就地指定：檔 / 邏輯名 ≠ 類名慣例

            Assert.Equal(new[] { "North" }, city.ToList());
        }

        [Theory]  // set 名統一為檔名形式：三來源「Product」與「Set_Product」等價
        [InlineData("Product", "Set_Product")]
        [InlineData("Set_Product", "Product")]
        [InlineData("Set_Product", "Set_Product")]
        [InlineData("Product", "Product")]
        public void InMemorySet_PrefixFormsEquivalent(string registerName, string readName)
        {
            var src = new InMemoryDataSource().AddSet(registerName, new[] { "Desk", "Chair" });

            Assert.Equal(new[] { "Desk", "Chair" }, src.LoadSet(readName));
        }

        [Fact]
        public void DbLoadParam_ArbitrarySelect_MapsByColumnName()
        {
            var dt = new DataTable();
            dt.Columns.Add("QTY"); dt.Columns.Add("LOT"); dt.Columns.Add("EQP"); dt.Columns.Add("EXTRA");
            dt.Rows.Add("7", "L9", "E9", "ignored");
            var fake = new FakeDbCtrl { NextResult = dt };

            var rows = new DbDataSource(fake).LoadParam<DsParam>(
                "SELECT * FROM V_DEMAND WHERE DATA_ID = :id", (":id", "V1"));

            Assert.Equal("SELECT * FROM V_DEMAND WHERE DATA_ID = :id", fake.LastSql);
            Assert.Equal("V1", fake.LastParameters.Single().value);
            Assert.Single(rows);
            Assert.Equal("L9", rows[0].Lot);
            Assert.Equal(7, rows[0].QTY);
        }

        [Fact]
        public void DbLoadParam_MissingColumn_ThrowsWithSql()
        {
            var dt = new DataTable();
            dt.Columns.Add("LOT"); dt.Columns.Add("QTY"); // 缺 EQP
            var fake = new FakeDbCtrl { NextResult = dt };

            var ex = Assert.Throws<InvalidDataException>(
                () => new DbDataSource(fake).LoadParam<DsParam>("SELECT LOT, QTY FROM T"));
            Assert.Contains("Eqp", ex.Message);
        }

        [Fact]
        public void DbLoadSet_ReturnsFirstColumn()
        {
            var dt = new DataTable();
            dt.Columns.Add("EQP");
            dt.Rows.Add("E1");
            dt.Rows.Add("E2");
            var fake = new FakeDbCtrl { NextResult = dt };

            var members = new DbDataSource(fake).LoadSet("SELECT DISTINCT EQP FROM T ORDER BY EQP");

            Assert.Equal(new[] { "E1", "E2" }, members);
        }

        [Fact]
        public void DbLoadTable_PassesThroughRawDataTable()
        {
            var dt = new DataTable();
            dt.Columns.Add("LOT"); dt.Columns.Add("QTY");
            dt.Rows.Add("L1", "3");
            var fake = new FakeDbCtrl { NextResult = dt };

            var table = new DbDataSource(fake).LoadTable("SELECT * FROM t WHERE data_id = :id", (":id", "V1"));

            Assert.Same(dt, table);   // 直接回 IDbCtrl.Query 的 DataTable，不做轉型
            Assert.Equal("SELECT * FROM t WHERE data_id = :id", fake.LastSql);
            Assert.Equal("V1", fake.LastParameters.Single().value);
        }
    }

    // CSV 端的檔名覆寫（帶不帶 .csv 由 EnsureCsv 統一補齊）
    public class NamedReadCsvTests : IDisposable
    {
        private readonly List<string> _files = new List<string>();

        private void WriteDataFile(string fileName, string content)
        {
            FolderDir.Data.CreateFolder();
            string path = FolderDir.Data.GetFilePath(fileName);
            File.WriteAllText(path, content, new System.Text.UTF8Encoding(true));
            _files.Add(path);
        }

        public void Dispose()
        {
            foreach (var f in _files) if (File.Exists(f)) File.Delete(f);
        }

        [Fact]
        public void CsvLoadParam_NameOverride_ReadsSpecifiedFile()
        {
            WriteDataFile("LegacyDemand.csv", "Lot,Eqp,QTY\nL2,E2,5\n");

            var rows = new CsvDataSource().LoadParam<DsParam>("LegacyDemand");

            Assert.Single(rows);
            Assert.Equal("L2", rows[0].Lot);
            Assert.Equal(5, rows[0].QTY);
        }

        [Fact]
        public void CsvSetLoad_NameOverride_ReadsSpecifiedFile()
        {
            WriteDataFile("Set_Region.csv", "North\nSouth\n");

            var city = new Set_City();
            city.Load(new CsvDataSource(), "Region");   // CsvDataSource.LoadSet 自動補 Set_ 前綴

            Assert.Equal(new[] { "North", "South" }, city.ToList());
        }

        [Fact]
        public void CsvLoadTable_ReadsWholeCsvAsDataTable()
        {
            WriteDataFile("RawOrders.csv", "Lot,Eqp,QTY\nL1,E1,3\nL2,E2,5\n");

            var table = new CsvDataSource().LoadTable("RawOrders");

            Assert.Equal(new[] { "Lot", "Eqp", "QTY" }, table.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName));
            Assert.Equal(2, table.Rows.Count);
            Assert.Equal("L2", table.Rows[1]["Lot"]);
            Assert.Equal("5", table.Rows[1]["QTY"]);   // 全欄 string，不轉型
        }
    }
}
