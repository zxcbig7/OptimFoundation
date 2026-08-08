using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
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

        // ── RFC4180：引號內逗號不裂欄 ────────────────────────────────────

        [Fact]
        public void BuildParameter_QuotedFieldWithComma_DoesNotSplit()
        {
            // Lot 欄含逗號、用引號包住；Eqp/QTY 照常——驗證引號內的逗號不裂欄
            WriteDataFile("CsvT7.csv", "Lot,Eqp,QTY\n\"a, b\",c,1\n");

            var rows = CsvCtrl.BuildParameter<CsvParam>("CsvT7");

            Assert.Single(rows);
            Assert.Equal("a, b", rows[0].Lot);
            Assert.Equal("c", rows[0].Eqp);
            Assert.Equal(1, rows[0].QTY);
        }

        // ── RFC4180："" 跳脫成一個字面 " ──────────────────────────────────

        [Fact]
        public void BuildParameter_EscapedQuote_DecodesToLiteralQuote()
        {
            WriteDataFile("CsvT8.csv", "Lot,Eqp,QTY\n\"he said \"\"hi\"\"\",EQP1,2\n");

            var rows = CsvCtrl.BuildParameter<CsvParam>("CsvT8");

            Assert.Single(rows);
            Assert.Equal("he said \"hi\"", rows[0].Lot);
        }

        // ── RFC4180：一般無引號行為不變（既有 CSV 不 regression）────────────

        [Fact]
        public void BuildParameter_PlainUnquotedRow_Unchanged()
        {
            WriteDataFile("CsvT9.csv", "Lot,Eqp,QTY\nLOT1,EQP1,5\n");

            var rows = CsvCtrl.BuildParameter<CsvParam>("CsvT9");

            Assert.Single(rows);
            Assert.Equal("LOT1", rows[0].Lot);
            Assert.Equal("EQP1", rows[0].Eqp);
            Assert.Equal(5, rows[0].QTY);
        }

        // ── RFC4180：欄位內換行不支援 → 未閉合引號丟 InvalidDataException ───

        [Fact]
        public void BuildParameter_UnclosedQuote_ThrowsInvalidDataException()
        {
            WriteDataFile("CsvT10.csv", "Lot,Eqp,QTY\n\"unterminated,EQP1,3\n");

            var ex = Assert.Throws<InvalidDataException>(() => CsvCtrl.BuildParameter<CsvParam>("CsvT10"));
            Assert.Contains("未閉合", ex.Message);
            Assert.Contains("不支援欄位內換行", ex.Message);
        }

        // ── ReadStrSet（單欄 Set 檔）：同一套解析，含逗號引號欄位不裂 ────────

        [Fact]
        public void ReadStrSet_QuotedFieldWithComma_KeptWhole()
        {
            WriteDataFile("Set_CsvT11.csv", "A\n\"Product, Large\"\nC\n");

            var result = CsvCtrl.ReadStrSet("Set_CsvT11.csv");

            Assert.Equal(new[] { "A", "Product, Large", "C" }, result);
        }

        [Fact]
        public void BuildParameter_QuotedMultilineField_IsOneRecord()
        {
            WriteDataFile("CsvT12.csv", "Lot,Eqp,QTY\n\"North\nDepot\",EQP1,2\n");

            var rows = CsvCtrl.BuildParameter<CsvParam>("CsvT12");

            Assert.Single(rows);
            Assert.Equal("North\nDepot", rows[0].Lot);
        }

        [Fact]
        public void BuildParameter_TrimsAndParsesNumbersWithInvariantCulture()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                WriteDataFile("CsvT13.csv", "Lot,Eqp,QTY\n L1 , E1 , 1.5 \n");

                var rows = CsvCtrl.BuildParameter<CsvParam>("CsvT13");

                Assert.Equal("L1", rows[0].Lot);
                Assert.Equal("E1", rows[0].Eqp);
                Assert.Equal(1.5, rows[0].QTY);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void BuildParameter_EmptyNumericCell_ThrowsFormatException()
        {
            WriteDataFile("CsvT14.csv", "Lot,Eqp,QTY\nL1,E1, \n");

            Assert.Throws<FormatException>(() => CsvCtrl.BuildParameter<CsvParam>("CsvT14"));
        }

        [Fact]
        public void ReadDoubleSet_TrimsAndUsesInvariantCulture()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                WriteDataFile("Set_CsvT15.csv", " 1.5 \n");

                Assert.Equal(new[] { 1.5 }, CsvCtrl.ReadDoubleSet("Set_CsvT15"));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
