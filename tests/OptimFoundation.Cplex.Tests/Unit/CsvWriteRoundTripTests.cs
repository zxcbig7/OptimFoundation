using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // 測試用參數類（QTY 最後，對齊 canonical schema）
    public class WrParam : ParameterBase
    {
        public string Lot { get; set; } = string.Empty;
        public string Eqp { get; set; } = string.Empty;
        public double QTY { get; set; }
    }

    // 只有 index、沒有值的參數（對應 [OptParam(HasValue = false)]）
    public class WrKeyOnlyParam : ParameterBase
    {
        public int Row { get; set; }
        public int Column { get; set; }
    }

    // 多一個 public field + static property：GetProperties() 不會撈到 field / ReflectionHelper 會，
    // 用來守住「WriteParam 必須用 GetProperties()」這條規則
    public class WrFieldParam : ParameterBase
    {
        public string Lot { get; set; } = string.Empty;
        public double QTY { get; set; }
        public string Note = "ignored";
    }

    /// <summary>
    /// CsvCtrl 寫入端（WriteSet / WriteParam）與既有讀取端的 round-trip——
    /// 輸出位置就是 Data/，所以第二階段 new CsvDataSource() 原封不動讀得到（規格 2026-08-01-csv-data-snapshot）。
    /// </summary>
    public class CsvWriteRoundTripTests : IDisposable
    {
        private sealed class Set_WrEmp : SetBase<string> { }
        private sealed class Set_WrDate : SetBase<DateTime> { }
        private sealed class Set_WrNum : SetBase<int> { }

        private readonly List<string> _createdFiles = new List<string>();

        private string Track(string fileName)
        {
            string path = FolderDir.Data.GetFilePath(fileName);
            _createdFiles.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (var f in _createdFiles)
                if (File.Exists(f)) File.Delete(f);
        }

        // ── WriteSet ────────────────────────────────────────────────────

        [Fact]
        public void WriteSet_ThenReadStrSet_PreservesOrder()
        {
            Track("Set_WrEmp.csv");
            var s = new Set_WrEmp();
            s.LoadInline("Carol", "Alice", "Bob");

            CsvCtrl.WriteSet(s, "Set_WrEmp");

            Assert.Equal(new[] { "Carol", "Alice", "Bob" }, CsvCtrl.ReadStrSet("Set_WrEmp"));
        }

        [Fact]
        public void WriteSet_OmittedFileName_UsesTypeName()
        {
            Track("Set_WrEmp.csv");
            var s = new Set_WrEmp();
            s.LoadInline("A", "B");

            CsvCtrl.WriteSet(s);

            Assert.True(File.Exists(FolderDir.Data.GetFilePath("Set_WrEmp.csv")));
        }

        [Fact]
        public void WriteSet_IntSet_ReloadsIntoSameMembers()
        {
            Track("Set_WrNum.csv");
            var s = new Set_WrNum();
            s.LoadFrom(Enumerable.Range(1, 9));

            CsvCtrl.WriteSet(s, "Set_WrNum");

            var reloaded = new Set_WrNum();
            reloaded.LoadCsv("Set_WrNum");
            Assert.Equal(s.ToList(), reloaded.ToList());
        }

        [Fact]
        public void WriteSet_DateSet_RoundTripsAsIsoDate()
        {
            Track("Set_WrDate.csv");
            var s = new Set_WrDate();
            s.LoadInline(new DateTime(2026, 8, 1), new DateTime(2026, 8, 2));

            CsvCtrl.WriteSet(s, "Set_WrDate");

            Assert.Equal("2026-08-01", CsvCtrl.ReadStrSet("Set_WrDate")[0]);

            var reloaded = new Set_WrDate();
            reloaded.LoadCsv("Set_WrDate");
            Assert.Equal(s.ToList(), reloaded.ToList());
        }

        [Fact]
        public void WriteSet_DateWithTimeOfDay_Throws()
        {
            Track("Set_WrDate.csv");
            var s = new Set_WrDate();
            s.LoadInline(new DateTime(2026, 8, 1, 13, 30, 0));

            // index 粒度只到日（變數 key 就是 yyyy-MM-dd），靜默截掉時分秒會讓 round-trip 悄悄失真
            Assert.Throws<NotSupportedException>(() => CsvCtrl.WriteSet(s, "Set_WrDate"));
        }

        [Fact]
        public void WriteSet_NotLoaded_ThrowsWithoutCreatingFile()
        {
            string path = Track("Set_WrEmp.csv");
            if (File.Exists(path)) File.Delete(path);

            Assert.Throws<InvalidOperationException>(() => CsvCtrl.WriteSet(new Set_WrEmp(), "Set_WrEmp"));
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void WriteSet_MemberWithComma_RoundTrips()
        {
            Track("Set_WrEmp.csv");
            var s = new Set_WrEmp();
            s.LoadInline("Lee, Alice", "Bob \"BB\"");

            CsvCtrl.WriteSet(s, "Set_WrEmp");

            Assert.Equal(new[] { "Lee, Alice", "Bob \"BB\"" }, CsvCtrl.ReadStrSet("Set_WrEmp"));
        }

        // ── WriteParam ──────────────────────────────────────────────────

        [Fact]
        public void WriteParam_ThenBuildParameter_RoundTrips()
        {
            Track("WrParam.csv");
            var rows = new List<WrParam>
            {
                new WrParam { Lot = "L1", Eqp = "E1", QTY = 3.5 },
                new WrParam { Lot = "L2", Eqp = "E2", QTY = 7 },
            };

            CsvCtrl.WriteParam(rows);

            var back = CsvCtrl.BuildParameter<WrParam>();
            Assert.Equal(2, back.Count);
            Assert.Equal("L1", back[0].Lot);
            Assert.Equal("E1", back[0].Eqp);
            Assert.Equal(3.5, back[0].QTY);
            Assert.Equal("L2", back[1].Lot);
            Assert.Equal(7, back[1].QTY);
        }

        [Fact]
        public void WriteParam_KeyOnlyParam_RoundTrips()
        {
            Track("WrKeyOnlyParam.csv");
            var rows = new List<WrKeyOnlyParam>
            {
                new WrKeyOnlyParam { Row = 1, Column = 6 },
                new WrKeyOnlyParam { Row = 9, Column = 2 },
            };

            CsvCtrl.WriteParam(rows);

            var back = CsvCtrl.BuildParameter<WrKeyOnlyParam>();
            Assert.Equal(2, back.Count);
            Assert.Equal(1, back[0].Row);
            Assert.Equal(6, back[0].Column);
            Assert.Equal(9, back[1].Row);
        }

        [Fact]
        public void WriteParam_EmptyRows_WritesHeaderOnly()
        {
            string path = Track("WrParam.csv");

            CsvCtrl.WriteParam(new List<WrParam>());

            Assert.Equal("LOT,EQP,QTY", File.ReadAllLines(path)[0]);
            Assert.Empty(CsvCtrl.BuildParameter<WrParam>());
        }

        [Fact]
        public void WriteParam_ValueWithComma_RoundTrips()
        {
            Track("WrParam.csv");
            var rows = new List<WrParam> { new WrParam { Lot = "L,1", Eqp = "say \"hi\"", QTY = 1 } };

            CsvCtrl.WriteParam(rows);

            var back = CsvCtrl.BuildParameter<WrParam>();
            Assert.Equal("L,1", back[0].Lot);
            Assert.Equal("say \"hi\"", back[0].Eqp);
        }

        [Fact]
        public void WriteParam_DoublePrecision_RoundTripsExactly()
        {
            Track("WrParam.csv");
            double tricky = 0.1 + 0.2;
            var rows = new List<WrParam>
            {
                new WrParam { Lot = "L1", Eqp = "E1", QTY = tricky },
                new WrParam { Lot = "L2", Eqp = "E2", QTY = 1234567.891011 },
            };

            CsvCtrl.WriteParam(rows);

            var back = CsvCtrl.BuildParameter<WrParam>();
            Assert.Equal(tricky, back[0].QTY);
            Assert.Equal(1234567.891011, back[1].QTY);
        }

        [Fact]
        public void WriteParam_UsesGetProperties_NotReflectionHelper()
        {
            string path = Track("WrFieldParam.csv");

            CsvCtrl.WriteParam(new List<WrFieldParam> { new WrFieldParam { Lot = "L1", QTY = 2 } });

            // ReflectionHelper.GetMemberNames 會撈進 public field（NOTE），BuildParameter 用的 GetProperties() 不會——
            // 寫入端必須跟讀取端同一來源，否則 round-trip 對不上欄
            Assert.Equal("LOT,QTY", File.ReadAllLines(path)[0]);
            Assert.Equal(2, CsvCtrl.BuildParameter<WrFieldParam>()[0].QTY);
        }

        // ── 第二階段零改動：輸出即 CsvDataSource 的輸入 ──────────────────

        [Fact]
        public void WrittenFiles_AreReadableByCsvDataSource()
        {
            Track("Set_WrEmp.csv");
            Track("WrParam.csv");

            var s = new Set_WrEmp();
            s.LoadInline("Alice", "Bob");
            CsvCtrl.WriteSet(s, "Set_WrEmp");
            CsvCtrl.WriteParam(new List<WrParam> { new WrParam { Lot = "L1", Eqp = "E1", QTY = 4 } });

            var source = new CsvDataSource();

            Assert.Equal(new[] { "Alice", "Bob" }, source.LoadSet("Set_WrEmp"));
            Assert.Equal(4, source.LoadParam<WrParam>()[0].QTY);
        }

        [Fact]
        public void SetBase_LoadFromWrittenFile_MatchesOriginal()
        {
            Track("Set_WrEmp.csv");
            var original = new Set_WrEmp();
            original.LoadInline("Alice", "Bob", "Carol");
            CsvCtrl.WriteSet(original, "Set_WrEmp");

            var reloaded = new Set_WrEmp();
            reloaded.Load(new CsvDataSource(), "Set_WrEmp");

            Assert.Equal(original.ToList(), reloaded.ToList());
        }
    }
}
