using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Modeling;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // 使用真實 generator 產碼，驗證自訂 double 欄位也接受數值檢查；模型係數仍使用 QTY。

    // Set 的元素型別一律由同類別上的 OptDim<T> 宣告。
    [OptSet]
    [OptDim<string>("GncItem")]
    public partial class Set_GncItem
    {
    }

    [OptParam]
    [OptDim<string>("GncItem")]
    public partial class Parameter_GncProfit
    {
        // 自訂 double 欄位，不屬於 QTY 或索引。
        public double Profit { get; set; }
    }

    [OptParam]
    public partial class Parameter_GncScalar
    {
    }

    [OptVar]
    [OptDim<string>("GncItem")]
    public partial class VariableC_GncAmount
    {
    }

    [OptVar]
    [OptDim<string>("GncItem")]
    public partial class VariableI_GncCount
    {
    }

    public partial class GncDataload : DataContext
    {
        public List<Set_GncItem> GncItemSet = new();
        public List<Parameter_GncProfit> ProfitRows = new();

        public GncDataload(IEnumerable<string> items, IEnumerable<Parameter_GncProfit> rows)
        {
            GncItemSet = items.Select(GncItem => new Set_GncItem { GncItem = GncItem }).ToList();
            ProfitRows = rows.ToList();
        }
    }

    public class GeneratorNumericCoverageTests
    {
        private static List<Parameter_GncProfit> LoadProfitCsv(string content)
        {
            string fileName = $"profit-{Guid.NewGuid():N}.csv";
            string path = FolderDir.Input.GetPathFile(fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            try
            {
                IDataSource source = new CsvDataSource();
                return source.Load<Parameter_GncProfit>(fileName);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void CsvSource_Load_UsesExplicitFileNameInsteadOfRowClassName()
        {
            string fileName = $"product-master-{Guid.NewGuid():N}.csv";
            string path = FolderDir.Input.GetPathFile(fileName);

            try
            {
                CsvCtrl.WriteRows(
                    new[] { new Set_GncItem { GncItem = "Desk" } },
                    fileName);

                IDataSource source = new CsvDataSource();
                var rows = source.Load<Set_GncItem>(fileName);

                var row = Assert.Single(rows);
                Assert.Equal("Desk", row.GncItem);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void OptParam_AlwaysGeneratesQty()
        {
            Assert.NotNull(typeof(Parameter_GncProfit).GetProperty("QTY"));
        }

        [Fact]
        public void OptParam_WithoutDims_GeneratesScalarQty()
        {
            Assert.Equal(typeof(ParameterBase), typeof(Parameter_GncScalar).BaseType);
            Assert.NotNull(typeof(Parameter_GncScalar).GetProperty("QTY"));
            Assert.Null(typeof(Parameter_GncScalar).GetProperty("GncItem"));
        }

        [Fact]
        public void OptVar_PrefixC_GeneratesContinuousVariableModel()
        {
            Assert.Equal(typeof(VariableBase), typeof(VariableC_GncAmount).BaseType);
            Assert.NotNull(typeof(VariableC_GncAmount).GetProperty("GncItem"));
        }

        [Fact]
        public void OptVar_PrefixI_GeneratesIntegerVariableModel()
        {
            Assert.Equal(typeof(VariableBase), typeof(VariableI_GncCount).BaseType);
            Assert.NotNull(typeof(VariableI_GncCount).GetProperty("GncItem"));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(2e15)]
        public void HandwrittenNonQtyDoubleField_BadValue_ReportsNumeric(double badValue)
        {
            var result = OptData.Load(() => new GncDataload(
                new[] { "Desk" },
                new[] { new Parameter_GncProfit { GncItem = "Desk", Profit = badValue } }));

            Assert.Contains(result.DataIssues, i => i.Kind == DataIssueKind.Numeric && i.Detail.Contains("Profit"));
        }

        [Fact]
        public void HandwrittenNonQtyDoubleField_NormalValue_NoIssue()
        {
            var result = OptData.Load(() => new GncDataload(
                new[] { "Desk" },
                new[] { new Parameter_GncProfit { GncItem = "Desk", Profit = 12.5 } }));

            Assert.Single(result.ProfitRows);
            Assert.Equal(12.5, result.ProfitRows[0].Profit);
            Assert.Empty(result.DataIssues);
        }

        [Fact]
        public void DuplicateSetKey_IsRegisteredByGeneratorAndReportedWithoutBlocking()
        {
            var result = OptData.Load(() => new GncDataload(
                new[] { "Desk", "Desk" },
                new[] { new Parameter_GncProfit { GncItem = "Desk", Profit = 12.5 } }));

            Assert.Equal(2, result.GncItemSet.Count);
            Assert.Contains(result.DataIssues, issue =>
                issue.Kind == DataIssueKind.DuplicateKey
                && issue.Parameter == nameof(Set_GncItem)
                && issue.Detail.Contains("集合鍵重複"));
        }

        // 數值欄位不參與模型名稱組成，因此負數或科學記號中的 -、+ 不應觸發名稱字元檢查。
        [Fact]
        public void CsvLoad_NegativeAndScientificValues_AreNotNamingErrors()
        {
            var rows = LoadProfitCsv("GncItem,Profit,QTY\nDesk,-1.5,-5\nChair,2,1E-05\n");

            var result = OptData.Load(() => new GncDataload(new[] { "Desk", "Chair" }, rows));

            Assert.Equal(-5, result.ProfitRows[0].QTY);
            Assert.Equal(1E-05, result.ProfitRows[1].QTY);
            Assert.Empty(result.DataIssues);
        }

        [Fact]
        public void CsvLoad_KeyWithWhitespace_ReportsInvalidKeyWithoutBlocking()
        {
            var rows = LoadProfitCsv("GncItem,Profit,QTY\nChair A,1,5\n");

            var result = OptData.Load(() => new GncDataload(new[] { "Desk" }, rows));

            Assert.Equal("Chair A", Assert.Single(result.ProfitRows).GncItem);
            Assert.Contains(result.DataIssues, issue =>
                issue.Kind == DataIssueKind.InvalidKey
                && issue.Parameter == nameof(Parameter_GncProfit)
                && issue.Detail.Contains("GncItem='Chair A'")
                && issue.Detail.Contains("原因=含空白字元"));
        }

        [Fact]
        public void SetKeyWithReservedCharacter_ReportsInvalidKeyAndSkipsDuplicateCheck()
        {
            var result = OptData.Load(() => new GncDataload(
                new[] { "A@B", "A@B" },
                Array.Empty<Parameter_GncProfit>()));

            Assert.Equal(2, result.DataIssues.Count);
            Assert.All(result.DataIssues, issue => Assert.Equal(DataIssueKind.InvalidKey, issue.Kind));
        }

        [Fact]
        public void DuplicateParameterKey_IsReportedWithoutBlocking()
        {
            var result = OptData.Load(() => new GncDataload(
                new[] { "Desk" },
                new[]
                {
                    new Parameter_GncProfit { GncItem = "Desk", Profit = 1 },
                    new Parameter_GncProfit { GncItem = "Desk", Profit = 2 }
                }));

            Assert.Equal(2, result.ProfitRows.Count);
            Assert.Contains(result.DataIssues, issue =>
                issue.Kind == DataIssueKind.DuplicateKey
                && issue.Parameter == nameof(Parameter_GncProfit)
                && issue.Detail.Contains("參數鍵重複"));
        }
    }
}
