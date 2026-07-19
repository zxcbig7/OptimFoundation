using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OptimFoundation.Core;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // DataValidator 是純函式：直接建構 sets/params 餵進去，不需經 DataContext/generator。
    public class DataValidatorTests
    {
        // 測試用 Set 積木（string 元素）
        private sealed class Set_DvProduct : SetBase<string> { }
        private sealed class Set_DvRegion : SetBase<string> { }

        private static Set_DvProduct BuildProductSet(params string[] members)
        {
            var s = new Set_DvProduct();
            s.LoadInline(members);
            return s;
        }

        private static Set_DvRegion BuildRegionSet(params string[] members)
        {
            var s = new Set_DvRegion();
            s.LoadInline(members);
            return s;
        }

        private static IReadOnlyDictionary<string, ISetBrick> SetsOf(params (string name, ISetBrick set)[] items)
            => items.ToDictionary(i => i.name, i => i.set);

        private static ParamRow Row(object[] index, params (string, double)[] numbers)
            => new ParamRow(index, numbers);

        private static ParamRegistration Reg(string name, string[] indexSets, bool fullGrid, params ParamRow[] rows)
            => new ParamRegistration(name, indexSets, fullGrid, rows);

        // (a) 乾淨資料 → 回空清單
        [Fact]
        public void Validate_CleanData_ReturnsEmpty()
        {
            var sets = SetsOf(("Product", BuildProductSet("Desk", "Chair")));
            var reg = Reg("Parameter_Demo", new[] { "Product" }, false,
                Row(new object[] { "Desk" }, ("QTY", 10.0)));

            var issues = DataValidator.Validate(sets, new[] { reg });

            Assert.Empty(issues);
        }

        // (b) dangling：值型別對，但不在 set 內
        [Fact]
        public void Validate_ValueNotInSet_ReportsDangling()
        {
            var sets = SetsOf(("Product", BuildProductSet("Desk", "Chair")));
            var reg = Reg("Parameter_Demo", new[] { "Product" }, false,
                Row(new object[] { "Table" }, ("QTY", 1.0)));

            var issues = DataValidator.Validate(sets, new[] { reg });

            var issue = Assert.Single(issues);
            Assert.Equal(DataIssueKind.Dangling, issue.Kind);
        }

        // (c) type mismatch：值型別與 set 元素型別不符 → 回 TypeMismatch 不是 Dangling
        [Fact]
        public void Validate_TypeMismatch_ReportsTypeMismatch_NotDangling()
        {
            var sets = SetsOf(("Product", BuildProductSet("Desk")));
            var reg = Reg("Parameter_Demo", new[] { "Product" }, false,
                Row(new object[] { 123 }, ("QTY", 1.0))); // int 值餵進 string set

            var issues = DataValidator.Validate(sets, new[] { reg });

            var issue = Assert.Single(issues);
            Assert.Equal(DataIssueKind.TypeMismatch, issue.Kind);
        }

        // (d) duplicate key：同一 parameter 兩列同 index
        [Fact]
        public void Validate_DuplicateIndexKey_ReportsDuplicateKey()
        {
            var sets = SetsOf(("Product", BuildProductSet("Desk")));
            var reg = Reg("Parameter_Demo", new[] { "Product" }, false,
                Row(new object[] { "Desk" }, ("QTY", 1.0)),
                Row(new object[] { "Desk" }, ("QTY", 2.0)));

            var issues = DataValidator.Validate(sets, new[] { reg });

            var issue = Assert.Single(issues);
            Assert.Equal(DataIssueKind.DuplicateKey, issue.Kind);
        }

        // (e) NaN
        [Fact]
        public void Validate_NaNValue_ReportsNumeric()
        {
            var sets = SetsOf(("Product", BuildProductSet("Desk")));
            var reg = Reg("Parameter_Demo", new[] { "Product" }, false,
                Row(new object[] { "Desk" }, ("QTY", double.NaN)));

            var issues = DataValidator.Validate(sets, new[] { reg });

            var issue = Assert.Single(issues);
            Assert.Equal(DataIssueKind.Numeric, issue.Kind);
        }

        // (f) Infinity
        [Fact]
        public void Validate_InfinityValue_ReportsNumeric()
        {
            var sets = SetsOf(("Product", BuildProductSet("Desk")));
            var reg = Reg("Parameter_Demo", new[] { "Product" }, false,
                Row(new object[] { "Desk" }, ("QTY", double.PositiveInfinity)));

            var issues = DataValidator.Validate(sets, new[] { reg });

            var issue = Assert.Single(issues);
            Assert.Equal(DataIssueKind.Numeric, issue.Kind);
        }

        // (g) 超過量級門檻 1e15
        [Fact]
        public void Validate_MagnitudeOverCeiling_ReportsNumeric()
        {
            var sets = SetsOf(("Product", BuildProductSet("Desk")));
            var reg = Reg("Parameter_Demo", new[] { "Product" }, false,
                Row(new object[] { "Desk" }, ("QTY", 2e15)));

            var issues = DataValidator.Validate(sets, new[] { reg });

            var issue = Assert.Single(issues);
            Assert.Equal(DataIssueKind.Numeric, issue.Kind);
        }

        // (h) 聚合：一次餵入多種問題，確認全部都被回報（不是只回第一個）
        [Fact]
        public void Validate_MultipleIssues_ReportsAllAtOnce()
        {
            var sets = SetsOf(("Product", BuildProductSet("Desk")));
            var reg = Reg("Parameter_Demo", new[] { "Product" }, false,
                Row(new object[] { "Table" }, ("QTY", 1.0)),        // dangling
                Row(new object[] { "Desk" }, ("QTY", double.NaN)),  // numeric
                Row(new object[] { "Desk" }, ("QTY", 2.0)));        // duplicate key（與上一列同 index）

            var issues = DataValidator.Validate(sets, new[] { reg });

            Assert.Equal(3, issues.Count);
            Assert.Contains(issues, i => i.Kind == DataIssueKind.Dangling);
            Assert.Contains(issues, i => i.Kind == DataIssueKind.Numeric);
            Assert.Contains(issues, i => i.Kind == DataIssueKind.DuplicateKey);
        }

        // ── [FullGrid] 完整性檢查（第四類，opt-in）──

        // (a) fullGrid=true 且滿格 → 無 issue
        [Fact]
        public void Validate_FullGrid_CompleteData_ReturnsEmpty()
        {
            var sets = SetsOf(
                ("Product", BuildProductSet("Desk", "Chair")),
                ("Region", BuildRegionSet("North", "South")));
            var reg = Reg("Parameter_Demo", new[] { "Product", "Region" }, true,
                Row(new object[] { "Desk", "North" }, ("QTY", 1.0)),
                Row(new object[] { "Desk", "South" }, ("QTY", 2.0)),
                Row(new object[] { "Chair", "North" }, ("QTY", 3.0)),
                Row(new object[] { "Chair", "South" }, ("QTY", 4.0)));

            var issues = DataValidator.Validate(sets, new[] { reg });

            Assert.Empty(issues);
        }

        // (b) fullGrid=true 且缺格 → 報 MissingCell，訊息含缺的組合
        [Fact]
        public void Validate_FullGrid_MissingCell_ReportsMissingCellWithCombo()
        {
            var sets = SetsOf(
                ("Product", BuildProductSet("Desk", "Chair")),
                ("Region", BuildRegionSet("North", "South")));
            var reg = Reg("Parameter_Demo", new[] { "Product", "Region" }, true,
                Row(new object[] { "Desk", "North" }, ("QTY", 1.0)),
                Row(new object[] { "Desk", "South" }, ("QTY", 2.0)),
                Row(new object[] { "Chair", "North" }, ("QTY", 3.0)));
                // 缺 (Chair, South)

            var issues = DataValidator.Validate(sets, new[] { reg });

            var issue = Assert.Single(issues);
            Assert.Equal(DataIssueKind.MissingCell, issue.Kind);
            Assert.Contains("Chair", issue.Detail);
            Assert.Contains("South", issue.Detail);
        }

        // (c) opt-in 有效：fullGrid=false 且缺格 → 不回報（稀疏資料不被誤擋）
        [Fact]
        public void Validate_NotFullGrid_MissingCell_NotReported()
        {
            var sets = SetsOf(
                ("Product", BuildProductSet("Desk", "Chair")),
                ("Region", BuildRegionSet("North", "South")));
            var reg = Reg("Parameter_Demo", new[] { "Product", "Region" }, false,
                Row(new object[] { "Desk", "North" }, ("QTY", 1.0)));
                // 缺 3 格，但未標 [FullGrid]——稀疏合法，不應誤報

            var issues = DataValidator.Validate(sets, new[] { reg });

            Assert.Empty(issues);
        }

        // (d) 缺格與其他類型問題並存 → 全部一起回報（不短路）
        [Fact]
        public void Validate_FullGrid_MissingCellWithOtherIssue_ReportsBothNoShortCircuit()
        {
            var sets = SetsOf(
                ("Product", BuildProductSet("Desk", "Chair")),
                ("Region", BuildRegionSet("North", "South")));
            var reg = Reg("Parameter_Demo", new[] { "Product", "Region" }, true,
                Row(new object[] { "Desk", "North" }, ("QTY", 5.0)),
                Row(new object[] { "Desk", "South" }, ("QTY", double.NaN)), // numeric 問題
                Row(new object[] { "Chair", "North" }, ("QTY", 8.0)));
                // 缺 (Chair, South) → missing cell 問題

            var issues = DataValidator.Validate(sets, new[] { reg });

            Assert.Equal(2, issues.Count);
            Assert.Contains(issues, i => i.Kind == DataIssueKind.MissingCell);
            Assert.Contains(issues, i => i.Kind == DataIssueKind.Numeric);
        }

        // 規模保護：期望格數 > 1,000,000 → 不枚舉笛卡兒積，只比對格數是否相符（否則 25 萬級迴圈會爆記憶體/時間）
        [Fact]
        public void Validate_FullGrid_ExpectedCellsOverCeiling_SkipsEnumeration_ReportsCountMismatchOnly()
        {
            var bigProduct = BuildProductSet(Enumerable.Range(0, 1001).Select(i => "P" + i).ToArray());
            var bigRegion = BuildRegionSet(Enumerable.Range(0, 1001).Select(i => "R" + i).ToArray());
            var sets = SetsOf(("Product", bigProduct), ("Region", bigRegion));
            var reg = Reg("Parameter_Demo", new[] { "Product", "Region" }, true); // 0 列，期望 1001*1001=1,002,001 格

            var sw = Stopwatch.StartNew();
            var issues = DataValidator.Validate(sets, new[] { reg });
            sw.Stop();

            var issue = Assert.Single(issues);
            Assert.Equal(DataIssueKind.MissingCell, issue.Kind);
            Assert.Contains("1002001", issue.Detail);
            Assert.Contains("過大", issue.Detail);
            Assert.True(sw.ElapsedMilliseconds < 3000,
                $"應直接比對格數、不逐一枚舉笛卡兒積，實際耗時 {sw.ElapsedMilliseconds}ms 過久，疑似仍在枚舉");
        }

        // 輸出上限：缺格數 > 20 → 只列前 20 組，補「還有 N 組未列出」
        [Fact]
        public void Validate_FullGrid_ManyMissingCells_CapsListingAt20()
        {
            var setA = BuildProductSet(Enumerable.Range(0, 5).Select(i => "P" + i).ToArray());
            var setB = BuildRegionSet(Enumerable.Range(0, 5).Select(i => "R" + i).ToArray());
            var sets = SetsOf(("Product", setA), ("Region", setB));
            var reg = Reg("Parameter_Demo", new[] { "Product", "Region" }, true); // 0 列，25 格全缺

            var issues = DataValidator.Validate(sets, new[] { reg });

            var issue = Assert.Single(issues);
            Assert.Equal(DataIssueKind.MissingCell, issue.Kind);
            Assert.Contains("缺 25 格", issue.Detail);
            Assert.Contains("還有 5 組未列出", issue.Detail);
        }

        // ── 檢查零：宣告的 index-set 名是否存在（每 parameter 驗一次，不掛列迴圈）──

        // 迴歸鎖：set 名打錯 + 零列 → 仍須回報。
        // 歷史漏洞：此驗證原本寫在逐列迴圈內，零列時迴圈不執行＝完全靜默，而 [FullGrid] 檢查也 return，兩邊都不報。
        [Fact]
        public void Validate_UnknownSetName_NoRows_StillReportsMissingSet()
        {
            var sets = SetsOf(("Product", BuildProductSet("Desk", "Chair")));
            var reg = Reg("Parameter_Demo", new[] { "Prodcut" }, false);   // 名稱打錯且零列

            var issues = DataValidator.Validate(sets, new[] { reg });

            var issue = Assert.Single(issues);
            Assert.Equal(DataIssueKind.MissingSet, issue.Kind);
            Assert.Contains("Prodcut", issue.Detail);
        }

        // set 名打錯且有多列 → 只報一次，不是每列噴一筆重複噪音
        [Fact]
        public void Validate_UnknownSetName_ManyRows_ReportsOnlyOnce()
        {
            var sets = SetsOf(("Product", BuildProductSet("Desk", "Chair")));
            var reg = Reg("Parameter_Demo", new[] { "Prodcut" }, false,
                Row(new object[] { "Desk" }, ("QTY", 1.0)),
                Row(new object[] { "Chair" }, ("QTY", 2.0)));

            var issues = DataValidator.Validate(sets, new[] { reg });

            Assert.Single(issues.Where(i => i.Kind == DataIssueKind.MissingSet));
        }

        // 覆蓋率缺口（前一輪驗收 WARN）：fullGrid=true + index-set 名打錯。
        // CheckFullGrid 對找不到的 set 會直接 return（避免與檢查零重複噪音），必須確認這不會讓整個驗證完全靜默——
        // 檢查零（CheckDeclaredSets）仍須獨立回報 MissingSet，且不因 FullGrid 提前 return 而漏掉。
        [Fact]
        public void Validate_FullGrid_UnknownSetName_StillReportsMissingSet_NotSilencedByFullGridReturn()
        {
            var sets = SetsOf(("Product", BuildProductSet("Desk", "Chair")));
            var reg = Reg("Parameter_Demo", new[] { "Prodcut" }, true);   // fullGrid=true + 名稱打錯，零列

            var issues = DataValidator.Validate(sets, new[] { reg });

            var issue = Assert.Single(issues);
            Assert.Equal(DataIssueKind.MissingSet, issue.Kind);
            Assert.Contains("Prodcut", issue.Detail);
        }

        // ── 數值涵蓋範圍：HasValue=false + 手寫（非 QTY）double 值欄位（見框架資料防護規格追補）──
        // 真實專案值欄位多半不叫 QTY（Profit/Required/Stock…），走 [OptParam(HasValue=false)] + 手寫 double 屬性。
        // 這裡直接驗證器端證明：只要該欄位有被納入 ParamRow.Numbers（即 generator 端有正確萃取），
        // NaN/Infinity/超量級門檻都會被回報 Numeric——generator 端「是否真的納入」由
        // GeneratorNumericCoverageTests（走真實 AutoSetsGenerator）另外鎖住，見該檔案的反向證明。
        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(2e15)]
        public void Validate_NonQtyHandwrittenValueField_BadValue_ReportsNumeric(double badValue)
        {
            var sets = SetsOf(("Product", BuildProductSet("Desk")));
            // HasValue=false 情境：ParamRow.Numbers 只含手寫欄位（如 Profit），不含 QTY
            var reg = Reg("Parameter_SandwichProfit", new[] { "Product" }, false,
                Row(new object[] { "Desk" }, ("Profit", badValue)));

            var issues = DataValidator.Validate(sets, new[] { reg });

            var issue = Assert.Single(issues);
            Assert.Equal(DataIssueKind.Numeric, issue.Kind);
            Assert.Contains("Profit", issue.Detail);
        }

        // ── 同一顆 Set 被多個自訂維度名引用（[OptDim<TSet>("自訂名")]）──
        // generator 除了用型別名註冊 Set，還會為每個自訂維度名各別名一次，指向同一顆 Set 欄位；
        // 此處模擬別名註冊後的結果（同一物件登記在兩個鍵下），驗證兩個名字都查得到、不報 MissingSet。
        [Fact]
        public void Validate_SetRegisteredUnderMultipleAliases_BothNamesResolve_NoMissingSet()
        {
            var groupSet = BuildProductSet("D", "E", "N", "C"); // 借用 Set_DvProduct 型別模擬 Set_Group 積木
            var sets = SetsOf(("Group", groupSet), ("PreGroup", groupSet)); // 同一物件註冊兩個鍵，模擬別名註冊
            var reg = Reg("Parameter_NightToDay", new[] { "PreGroup", "Group" }, false,
                Row(new object[] { "N", "D" }));

            var issues = DataValidator.Validate(sets, new[] { reg });

            Assert.Empty(issues);
        }
    }
}
