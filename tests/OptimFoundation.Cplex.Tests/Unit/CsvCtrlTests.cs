using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // 測試用參數類：properties-only、繼承 ModelElementBase（QTY 最後）
    public class CsvParam : ParameterBase
    {
        public string Lot { get; set; } = string.Empty;
        public string Eqp { get; set; } = string.Empty;
        public double QTY { get; set; }
    }

    public class CsvCtrlTests : IDisposable
    {
        private readonly List<string> _createdFiles = new List<string>();

        private string WriteDataFile(string fileName, string content)
        {
            FolderDir.Data.CreateFolder();
            string path = FolderDir.Data.GetFilePath(fileName);
            File.WriteAllText(path, content, new UTF8Encoding(true));
            _createdFiles.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (var f in _createdFiles)
                if (File.Exists(f)) File.Delete(f);
        }

        // ── 副檔名慣例：帶不帶 .csv 皆可 ─────────────────────────────────

        [Fact]
        public void ReadStrSet_WithAndWithoutExtension_BothWork()
        {
            WriteDataFile("Set_CsvT1.csv", "A\nB\nC\n");

            Assert.Equal(new[] { "A", "B", "C" }, CsvCtrl.ReadStrSet("Set_CsvT1.csv"));
            Assert.Equal(new[] { "A", "B", "C" }, CsvCtrl.ReadStrSet("Set_CsvT1"));
        }

        // ── BuildParameter：無表頭 legacy 按宣告順序 ────────────────────

        [Fact]
        public void BuildParameter_NoHeader_MapsByDeclarationOrder()
        {
            WriteDataFile("CsvT2.csv", "LOT1,EQP1,3.5\nLOT2,EQP2,7\n");

            var rows = CsvCtrl.BuildParameter<CsvParam>("CsvT2");

            Assert.Equal(2, rows.Count);
            Assert.Equal("LOT1", rows[0].Lot);
            Assert.Equal("EQP1", rows[0].Eqp);
            Assert.Equal(3.5, rows[0].QTY);
        }

        // ── BuildParameter：有表頭按名對位，欄序打亂也正確 ───────────────

        [Fact]
        public void BuildParameter_Header_MapsByName_IgnoresColumnOrder()
        {
            WriteDataFile("CsvT3.csv", "QTY,EQP,LOT\n9,EQP3,LOT9\n");

            var rows = CsvCtrl.BuildParameter<CsvParam>("CsvT3");

            Assert.Single(rows);
            Assert.Equal("LOT9", rows[0].Lot);
            Assert.Equal("EQP3", rows[0].Eqp);
            Assert.Equal(9, rows[0].QTY);
        }

        // ── Round-trip：solution 檔格式（DATA_ID/VAR_TYPE/USER 多餘欄自動忽略）──

        [Fact]
        public void BuildParameter_SolutionSchema_IgnoresExtraColumns()
        {
            WriteDataFile("CsvT4.csv",
                "DATA_ID,VAR_TYPE,LOT,EQP,QTY,USER\nD1,VariableB_X,LOT1,EQP2,1,VIC\n");

            var rows = CsvCtrl.BuildParameter<CsvParam>("CsvT4");

            Assert.Single(rows);
            Assert.Equal("LOT1", rows[0].Lot);
            Assert.Equal("EQP2", rows[0].Eqp);
            Assert.Equal(1, rows[0].QTY);
        }

        // ── 表頭缺必要欄 → 丟例外並點名缺哪欄 ──────────────────────────

        [Fact]
        public void BuildParameter_HeaderMissingColumn_Throws()
        {
            WriteDataFile("CsvT5.csv", "LOT,QTY\nLOT1,3\n");

            var ex = Assert.Throws<InvalidDataException>(() => CsvCtrl.BuildParameter<CsvParam>("CsvT5"));
            Assert.Contains("Eqp", ex.Message);
        }

        // ── 預設檔名 = {型別名}.csv（修 .csv.csv bug 的回歸測試）─────────

        [Fact]
        public void BuildParameter_DefaultFileName_UsesTypeName()
        {
            WriteDataFile("CsvParam.csv", "LOT,EQP,QTY\nLOT1,EQP1,2\n");

            var rows = CsvCtrl.BuildParameter<CsvParam>();

            Assert.Single(rows);
            Assert.Equal(2, rows[0].QTY);
        }

        // ── ReadParameter：容忍表頭行、key 格式不變 ─────────────────────

        [Fact]
        public void ReadParameter_SkipsHeader_KeepsKeyFormat()
        {
            WriteDataFile("CsvT6.csv", "LOT,EQP,QTY\nLOT1,EQP1,4\n");

            var dict = CsvCtrl.ReadParameter("CsvT6");

            Assert.Single(dict);
            Assert.Equal(4, dict["@LOT1@EQP1"]);
        }
    }
}
