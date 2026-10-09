using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    public class VariableManagerTests
    {

        [Fact]
        public void ComposeNames_1D_String_GeneratesCorrectKeys()
        {
            var names = VariableManager.ComposeNames("VarS",
                [new List<string> { "A", "B", "C" }]).ToList();

            Assert.Equal(3, names.Count);
            Assert.Equal("VarS@A", names[0]);
            Assert.Equal("VarS@B", names[1]);
            Assert.Equal("VarS@C", names[2]);
        }

        [Fact]
        public void ComposeNames_2D_DateTimeString_FormatsDateCorrectly()
        {
            var dates  = new List<DateTime> { new(2026, 1, 1), new(2026, 1, 2) };
            var groups = new List<string> { "D", "N" };

            var names = VariableManager.ComposeNames("VarDG", [dates, groups]).ToList();

            Assert.Equal(4, names.Count);
            Assert.Equal("VarDG@2026_01_01@D", names[0]);
            Assert.Equal("VarDG@2026_01_01@N", names[1]);
            Assert.Equal("VarDG@2026_01_02@D", names[2]);
            Assert.Equal("VarDG@2026_01_02@N", names[3]);
        }

        [Fact]
        public void ComposeNames_EmptySet_ReturnsEmpty()
        {
            var names = VariableManager.ComposeNames("VarS", [new List<string>()]).ToList();
            Assert.Empty(names);
        }

        [Fact]
        public void ComposeNames_IntSet_UsesDefaultToString()
        {
            var names = VariableManager.ComposeNames("VarInt", [new List<int> { 1, 2 }]).ToList();
            Assert.Equal("VarInt@1", names[0]);
            Assert.Equal("VarInt@2", names[1]);
        }


        [Fact]
        public void ConvertSets_DateTime_FormatsAsYYYYMMDD()
        {
            var lists = VariableManager.ConvertSetsToTokens(
                new List<DateTime> { new(2026, 3, 5) });
            Assert.Equal("2026_03_05", lists[0][0]);
        }

        [Fact]
        public void ConvertSets_DateTimeWithTime_ExpandsToSeconds()
        {
            var lists = VariableManager.ConvertSetsToTokens(
                new List<DateTime> { new(2026, 3, 5, 23, 59, 59) });
            Assert.Equal("2026_03_05_23_59_59", lists[0][0]);
        }

        [Fact]
        public void ConvertSets_UnsupportedType_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                VariableManager.ConvertSetsToTokens(new List<bool> { true }));
        }

        [Fact]
        public void ConvertSets_MemberContainsKeySeparator_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                VariableManager.ConvertSetsToTokens(new List<string> { "A", "B@C" }));
            Assert.Contains("集合 #1", ex.Message);
            Assert.Contains("B@C", ex.Message);
        }

        [Fact]
        public void ComposeNames_MemberContainsKeySeparator_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                VariableManager.ComposeNames("VarDG",
                    [new List<DateTime> { new(2026, 1, 1) }, new List<string> { "D@N" }]).ToList());
        }

        // ── 陣列支援：直接傳入 string[] 時仍視為一個集合 ──────────────────────

        [Fact]
        public void ConvertSets_BareStringArray_TreatedAsSingleSet()
        {
            // string[] 可直接當成 params object[] 傳入，導致成員被拆開；框架應將它們還原成同一個集合。
            var lists = VariableManager.ConvertSetsToTokens(new[] { "A", "B", "C" });
            Assert.Single(lists);
            Assert.Equal(new List<string> { "A", "B", "C" }, lists[0]);
        }

        [Fact]
        public void ConvertSets_ValueTypeArrays_Work()
        {
            var lists = VariableManager.ConvertSetsToTokens(
                new[] { 1, 2 },
                new[] { new DateTime(2026, 3, 5) });
            Assert.Equal(new List<string> { "1", "2" }, lists[0]);
            Assert.Equal("2026_03_05", lists[1][0]);
        }

        [Fact]
        public void ConvertSets_MixedSetAndBareString_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                VariableManager.ConvertSetsToTokens(new List<string> { "A" }, "B"));
        }

        [Fact]
        public void ComposeNames_1D_StringArray_GeneratesCorrectKeys()
        {
            var names = VariableManager.ComposeNames("VarS", new[] { "A", "B" }).ToList();
            Assert.Equal(2, names.Count);
            Assert.Equal("VarS@A", names[0]);
            Assert.Equal("VarS@B", names[1]);
        }

        // ── 把關：不建 engine 就能測 ─────────────────────────────

        [Fact]
        public void ValidateVariableClass_ResolvesTypeFromPrefix()
        {
            var sets = new object[] { new List<string> { "A" } };

            Assert.Equal(VarType.Binary, VariableManager.ValidateVariableClass<VariableB_Pick>(null, "BuildVars", sets));
            Assert.Equal(VarType.Continuous, VariableManager.ValidateVariableClass<VariableC_Amt>(null, "BuildVars", sets));
            Assert.Equal(VarType.Integer, VariableManager.ValidateVariableClass<VariableI_Cnt>(null, "BuildVars", sets));
        }

        [Fact]
        public void ValidateVariableClass_NoPrefix_RequiresExplicitType()
        {
            var sets = new object[] { new List<string> { "A" } };

            var error = Assert.Throws<ArgumentException>(() => VariableManager.ValidateVariableClass<VarS>(null, "BuildVars", sets));
            Assert.Contains("BuildVars<VarS>", error.Message);
            // 明確指定型別的入口（BuildCVs 等）可建立沒有正式前綴的類別
            Assert.Equal(VarType.Integer, VariableManager.ValidateVariableClass<VarS>(VarType.Integer, "BuildIVs", sets));
        }

        [Fact]
        public void ValidateVariableClass_PrefixConflictsWithRequestedType_Throws()
        {
            var sets = new object[] { new List<string> { "A" } };

            var error = Assert.Throws<ArgumentException>(
                () => VariableManager.ValidateVariableClass<VariableB_Pick>(VarType.Continuous, "BuildCVs", sets));
            Assert.Contains("前綴宣告為 Binary", error.Message);
        }

        [Fact]
        public void ValidateVariableClass_DimensionCountMismatch_Throws()
        {
            var sets = new object[] { new List<string> { "A" }, new List<string> { "B" } };

            Assert.Throws<ArgumentException>(() => VariableManager.ValidateVariableClass<VariableC_Amt>(null, "BuildVars", sets));
        }

        [Fact]
        public void SkipDuplicates_DropsExistingAndRepeatedNames_KeepsOrder()
        {
            var existing = new HashSet<string> { "VarS@B" };

            var newNames = VariableManager.SkipDuplicates("VarS", new[] { "VarS@A", "VarS@B", "VarS@A", "VarS@C" }, existing.Contains);

            Assert.Equal(new[] { "VarS@A", "VarS@C" }, newNames);
        }

        // ── 索引 ─────────────────────────────────────────────

        [Fact]
        public void GetNamesOfType_MatchesExactZeroDimAndPrefix_NotSimilarNames()
        {
            var all = new[] { "VarS@A", "VarS", "VarSX@A", "VarDG@2026_01_01@N", "VarS@B" };

            Assert.Equal(new[] { "VarS@A", "VarS", "VarS@B" }, VariableManager.GetNamesOfType("VarS", all));
            Assert.Empty(VariableManager.GetNamesOfType(null, all));
        }

        [Fact]
        public void GetTypeName_IsTextBeforeFirstSeparator()
        {
            Assert.Equal("VarDG", VariableManager.GetTypeName("VarDG@2026_01_01@N"));
            Assert.Equal("VariableC_Makespan", VariableManager.GetTypeName("VariableC_Makespan"));
            Assert.Equal("<未命名>", VariableManager.GetTypeName(" "));
        }
    }
}
