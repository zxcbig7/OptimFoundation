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
    }
}
