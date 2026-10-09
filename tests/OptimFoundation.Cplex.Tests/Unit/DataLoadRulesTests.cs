using System.Data;
using System.Text;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Modeling;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    [OptSet]
    [OptDim<DateTime>("Day")]
    [OptDim<string>("Site")]
    public partial class Set_LoadDaySite { }

    [OptParam]
    [OptDim<DateTime>("Day")]
    public partial class Parameter_LoadDayDemand { }

    [OptSet]
    [OptDim<string>("Site")]
    public partial class Set_LoadSite { }

    /// <summary>Set 與 Parameter 讀資料同一套規則：空白列 WARN 後略過、日期吃框架自己的格式、CSV 只收 UTF-8。</summary>
    [Collection("Logging")]
    public class DataLoadRulesTests
    {
        private static string StartLog(string prefix)
        {
            string tag = prefix + "_" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);
            return tag;
        }

        private static string ReadLog(string tag)
        {
            string file = Directory.GetFiles(FolderDir.Log.GetPath(), $"{tag}_*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }

        private static List<T> LoadCsv<T>(byte[] content) where T : ModelElementBase, new()
        {
            IDataSource source = new CsvDataSource();
            string fileName = $"load-rules-{Guid.NewGuid():N}.csv";
            string path = FolderDir.Input.GetPathFile(fileName);
            File.WriteAllBytes(path, content);
            try
            {
                return source.Load<T>(fileName);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

        [Fact]
        public void Csv_BlankLines_WarnAndSkip_ForSetAndParameter()
        {
            string tag = StartLog("BlankLines");

            var sets = LoadCsv<Set_LoadDaySite>(Utf8("Day,Site\n2026-06-01,A\n\n2026-06-02,B\n\n"));
            var parameters = LoadCsv<Parameter_LoadDayDemand>(Utf8("Day,QTY\n2026-06-01,5\n , \n2026-06-02,-1\n"));

            Assert.Equal(new[] { "A", "B" }, sets.Select(s => s.Site));
            Assert.Equal(new[] { 5.0, -1.0 }, parameters.Select(p => p.QTY));
            string log = ReadLog(tag);
            Assert.Contains("序號=3 原因=整列空白 結果=略過", log);
            Assert.Contains("序號=5 原因=整列空白 結果=略過", log);
        }

        [Fact]
        public void Db_AllNullRow_WarnsAndSkips_LikeCsv()
        {
            string tag = StartLog("DbBlankRow");
            var table = new DataTable();
            table.Columns.Add("Site", typeof(string));
            table.Rows.Add("A");
            table.Rows.Add(DBNull.Value);
            var db = new FakeDbCtrl { NextResult = table };

            var rows = new DbDataSource(db).Load<Set_LoadSite>("SELECT SITE FROM T");

            Assert.Equal("A", Assert.Single(rows).Site);
            Assert.Contains("[資料列為空] 名稱=SELECT SITE FROM T 序號=3 原因=整列空白 結果=略過", ReadLog(tag));
        }

        [Fact]
        public void Dates_FrameworkFormatAndIsoFormat_BothLoad()
        {
            // 框架寫出的解檔日期是 2026_06_01，要能原樣讀回
            var sets = LoadCsv<Set_LoadDaySite>(Utf8("Day,Site\n2026_06_01,A\n2026-06-02,B\n2026_06_03_08_30_00,C\n"));

            Assert.Equal(new DateTime(2026, 6, 1), sets[0].Day);
            Assert.Equal(new DateTime(2026, 6, 2), sets[1].Day);
            Assert.Equal(new DateTime(2026, 6, 3, 8, 30, 0), sets[2].Day);
            Assert.Equal("Set_LoadDaySite@2026_06_01@A", sets[0].ToString());
        }

        [Fact]
        public void Csv_NotUtf8_ThrowsInsteadOfReplacingCharacters()
        {
            // 0xA4 0xA4 是 Big5 / CP950 的「中」，不是合法 UTF-8
            byte[] cp950 = Utf8("Site\n").Concat(new byte[] { 0xA4, 0xA4 }).Concat(Utf8("\n")).ToArray();

            var error = Assert.Throws<InvalidDataException>(() => LoadCsv<Set_LoadSite>(cp950));

            Assert.Contains("不是 UTF-8 編碼", error.Message);
        }

        [Fact]
        public void Csv_Utf8WithBom_StillLoads()
        {
            byte[] withBom = new UTF8Encoding(true).GetPreamble().Concat(Utf8("Site\n中\n")).ToArray();

            Assert.Equal("中", Assert.Single(LoadCsv<Set_LoadSite>(withBom)).Site);
        }
    }
}
