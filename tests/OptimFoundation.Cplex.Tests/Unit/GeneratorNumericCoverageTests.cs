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
    // ── 端到端鎖住「數值 sanity 涵蓋範圍」的實質漏洞修正（見框架資料防護規格追補）──
    //
    // 手寫 double 值欄位（Profit/Required/Stock…）也必須受數值 sanity 檢查；Parameter 的
    // canonical 模型係數仍固定是 generator 產生的 QTY。
    //
    // 這裡刻意讓本檔宣告的 [OptSet]/[OptParam]/DataContext 類別「真的」走 AutoSetsGenerator（見
    // csproj 把 Generators.csproj 掛成 Analyzer），而非像 DataValidatorTests 那樣手動塞
    // ParamRegistration/ParamRow——手動塞只驗證 DataValidator 本身正確，鎖不住 generator 端
    // ResolveNumberPropNames 的涵蓋範圍。反向證明：把 ResolveNumberPropNames 還原成舊版
    // （只取 index props + QTY）後，本檔測試必須失敗（Profit 不會被納入 numbersOf，NaN 永遠驗不到）。

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
        // 使用者在 partial 另一半手寫的 double 值欄位（非 QTY，非 index 屬性）——真實專案的 Profit/Required/Stock 型態。
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
        [Fact]
        public void CsvSource_Load_UsesExplicitFileNameInsteadOfRowClassName()
        {
            string fileName = $"product-master-{Guid.NewGuid():N}.csv";
            string path = FolderDir.Data.GetFilePath(fileName);

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
            var ex = Assert.Throws<DataValidationException>(() =>
                OptData.Load(() => new GncDataload(
                    new[] { "Desk" },
                    new[] { new Parameter_GncProfit { GncItem = "Desk", Profit = badValue } })));

            Assert.Contains(ex.Issues, i => i.Kind == DataIssueKind.Numeric && i.Detail.Contains("Profit"));
        }

        // 正常值不誤擋：確認修正只是「擴大涵蓋範圍」，不是「所有 double 屬性都被誤判成問題」。
        [Fact]
        public void HandwrittenNonQtyDoubleField_NormalValue_NoIssue()
        {
            var result = OptData.Load(() => new GncDataload(
                new[] { "Desk" },
                new[] { new Parameter_GncProfit { GncItem = "Desk", Profit = 12.5 } }));

            Assert.Single(result.ProfitRows);
            Assert.Equal(12.5, result.ProfitRows[0].Profit);
        }

        [Fact]
        public void DuplicateSetKey_IsRegisteredByGeneratorAndRejected()
        {
            var ex = Assert.Throws<DataValidationException>(() =>
                OptData.Load(() => new GncDataload(
                    new[] { "Desk", "Desk" },
                    new[] { new Parameter_GncProfit { GncItem = "Desk", Profit = 12.5 } })));

            Assert.Contains(ex.Issues, issue =>
                issue.Kind == DataIssueKind.DuplicateKey
                && issue.Parameter == nameof(Set_GncItem)
                && issue.Detail.Contains("duplicate Set key"));
        }
    }
}
