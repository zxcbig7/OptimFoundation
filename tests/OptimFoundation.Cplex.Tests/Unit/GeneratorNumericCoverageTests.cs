using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Core;
using OptimFoundation.Modeling;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // ── 端到端鎖住「數值 sanity 涵蓋範圍」的實質漏洞修正（見框架資料防護規格追補）──
    //
    // 真實專案的值欄位多半不叫 QTY（Profit/Required/Stock…），一律走 [OptParam(HasValue=false)] +
    // 手寫 double 值欄位，先前 AutoSetsGenerator.ResolveNumberPropNames 只涵蓋「index props 裡型別
    // 是 double 的」＋「HasValue=true 才有的 QTY」，這類手寫值欄位完全不受數值 sanity 檢查。
    //
    // 這裡刻意讓本檔宣告的 [OptSet]/[OptParam]/DataContext 類別「真的」走 AutoSetsGenerator（見
    // csproj 把 Generators.csproj 掛成 Analyzer），而非像 DataValidatorTests 那樣手動塞
    // ParamRegistration/ParamRow——手動塞只驗證 DataValidator 本身正確，鎖不住 generator 端
    // ResolveNumberPropNames 的涵蓋範圍。反向證明：把 ResolveNumberPropNames 還原成舊版
    // （只取 index props + QTY）後，本檔測試必須失敗（Profit 不會被納入 numbersOf，NaN 永遠驗不到）。

    [OptSet]
    public partial class Set_GncItem
    {
    }

    [OptParam(HasValue = false)]
    [OptDim<Set_GncItem>("GncItem")]
    public partial class Parameter_GncProfit
    {
        // 使用者在 partial 另一半手寫的 double 值欄位（非 QTY，非 index 屬性）——真實專案的 Profit/Required/Stock 型態。
        public double Profit { get; set; }
    }

    public partial class GncDataload : DataContext
    {
        public Set_GncItem GncItemSet = new();
        public List<Parameter_GncProfit> ProfitRows = new();

        public GncDataload(IEnumerable<string> items, IEnumerable<Parameter_GncProfit> rows)
        {
            GncItemSet.LoadInline(items.ToArray());
            ProfitRows = rows.ToList();
        }
    }

    public class GeneratorNumericCoverageTests
    {
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
    }
}
