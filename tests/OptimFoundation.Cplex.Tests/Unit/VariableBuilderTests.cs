using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    public class VariableBuilderTests
    {
        // ── GetVarNames ────────────────────────────────────────────────────

        [Fact]
        public void GetVarNames_1D_String_GeneratesCorrectKeys()
        {
            var names = VariableBuilder.GetVarNames<VarS>(
                [new List<string> { "A", "B", "C" }]).ToList();

            Assert.Equal(3, names.Count);
            Assert.Equal("VarS@A", names[0]);
            Assert.Equal("VarS@B", names[1]);
            Assert.Equal("VarS@C", names[2]);
        }

        [Fact]
        public void GetVarNames_2D_DateTimeString_FormatsDateCorrectly()
        {
            var dates  = new List<DateTime> { new(2026, 1, 1), new(2026, 1, 2) };
            var groups = new List<string> { "D", "N" };

            var names = VariableBuilder.GetVarNames<VarDG>([dates, groups]).ToList();

            Assert.Equal(4, names.Count);
            Assert.Equal("VarDG@2026_01_01@D", names[0]);
            Assert.Equal("VarDG@2026_01_01@N", names[1]);
            Assert.Equal("VarDG@2026_01_02@D", names[2]);
            Assert.Equal("VarDG@2026_01_02@N", names[3]);
        }

        [Fact]
        public void GetVarNames_EmptySet_ReturnsEmpty()
        {
            var names = VariableBuilder.GetVarNames<VarS>([new List<string>()]).ToList();
            Assert.Empty(names);
        }

        [Fact]
        public void GetVarNames_IntSet_UsesDefaultToString()
        {
            var names = VariableBuilder.GetVarNames<VarInt>([new List<int> { 1, 2 }]).ToList();
            Assert.Equal("VarInt@1", names[0]);
            Assert.Equal("VarInt@2", names[1]);
        }

        // ── ConvertSetsToStringLists ───────────────────────────────────────

        [Fact]
        public void ConvertSets_DateTime_FormatsAsYYYYMMDD()
        {
            var lists = VariableBuilder.ConvertSetsToStringLists(
                new List<DateTime> { new(2026, 3, 5) });
            Assert.Equal("2026_03_05", lists[0][0]);
        }

        [Fact]
        public void ConvertSets_UnsupportedType_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                VariableBuilder.ConvertSetsToStringLists(new List<bool> { true }));
        }

        [Fact]
        public void ConvertSets_MemberContainsKeySeparator_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                VariableBuilder.ConvertSetsToStringLists(new List<string> { "A", "B@C" }));
            Assert.Contains("Set #1", ex.Message);
            Assert.Contains("B@C", ex.Message);
        }

        [Fact]
        public void GetVarNames_MemberContainsKeySeparator_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                VariableBuilder.GetVarNames<VarDG>(
                    [new List<DateTime> { new(2026, 1, 1) }, new List<string> { "D@N" }]).ToList());
        }

        // ── Array 支援（含 string[] 共變誤 bind 還原）──────────────────────

        [Fact]
        public void ConvertSets_BareStringArray_TreatedAsSingleSet()
        {
            // string[] 共變成 object[] 本身，元素散成裸 string → 應還原成單一 set
            var lists = VariableBuilder.ConvertSetsToStringLists(new[] { "A", "B", "C" });
            Assert.Single(lists);
            Assert.Equal(new List<string> { "A", "B", "C" }, lists[0]);
        }

        [Fact]
        public void ConvertSets_ValueTypeArrays_Work()
        {
            var lists = VariableBuilder.ConvertSetsToStringLists(
                new[] { 1, 2 },
                new[] { new DateTime(2026, 3, 5) });
            Assert.Equal(new List<string> { "1", "2" }, lists[0]);
            Assert.Equal("2026_03_05", lists[1][0]);
        }

        [Fact]
        public void ConvertSets_MixedSetAndBareString_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                VariableBuilder.ConvertSetsToStringLists(new List<string> { "A" }, "B"));
        }

        [Fact]
        public void GetVarNames_1D_StringArray_GeneratesCorrectKeys()
        {
            var names = VariableBuilder.GetVarNames<VarS>(new[] { "A", "B" }).ToList();
            Assert.Equal(2, names.Count);
            Assert.Equal("VarS@A", names[0]);
            Assert.Equal("VarS@B", names[1]);
        }
    }
}
