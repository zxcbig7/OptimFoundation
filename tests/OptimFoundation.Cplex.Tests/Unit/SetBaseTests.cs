using OptimFoundation.Core;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    public class SetBaseTests
    {
        // 具體積木（測試用）；SetName 去 Set_ 前綴的邏輯不影響防呆測試
        private sealed class Set_TestDate : SetBase<DateTime> { }
        private sealed class Set_TestEmp : SetBase<string> { }

        // ── 正常載入 + 保序 + Count + Contains ──────────────────────────────

        [Fact]
        public void LoadInline_ThenEnumerate_IsOrdered()
        {
            var s = new Set_TestEmp();
            s.LoadInline("Alice", "Bob", "Carol");

            Assert.Equal(3, s.Count);
            Assert.Equal(new[] { "Alice", "Bob", "Carol" }, s.ToList());
        }

        [Fact]
        public void Contains_ReflectsMembership()
        {
            var s = new Set_TestEmp();
            s.LoadInline("Alice", "Bob");

            Assert.True(s.Contains("Alice"));
            Assert.False(s.Contains("Zoe"));
        }

        [Fact]
        public void LoadFrom_AcceptsAnySequence()
        {
            var s = new Set_TestDate();
            s.LoadFrom(Enumerable.Range(1, 3).Select(d => new DateTime(2026, 8, d)));

            Assert.Equal(3, s.Count);
        }

        // ── 四道防呆 ──────────────────────────────────────────────────────

        [Fact]
        public void Count_BeforeLoad_Throws()
        {
            var s = new Set_TestEmp();
            Assert.Throws<InvalidOperationException>(() => _ = s.Count);
        }

        [Fact]
        public void Enumerate_BeforeLoad_Throws()
        {
            var s = new Set_TestEmp();
            Assert.Throws<InvalidOperationException>(() => s.ToList());
        }

        [Fact]
        public void DoubleLoad_Throws()
        {
            var s = new Set_TestEmp();
            s.LoadInline("Alice");
            Assert.Throws<InvalidOperationException>(() => s.LoadInline("Bob"));
        }

        [Fact]
        public void DuplicateMember_Throws()
        {
            var s = new Set_TestEmp();
            Assert.Throws<ArgumentException>(() => s.LoadInline("Alice", "Alice"));
        }

        [Fact]
        public void EmptyLoad_Throws()
        {
            var s = new Set_TestEmp();
            Assert.Throws<InvalidOperationException>(() => s.LoadInline());
        }

        // ── 與既有 VariableBuilder 的 IEnumerable 整合（零改動相容）──────────

        [Fact]
        public void SetBase_FlowsIntoConvertSetsToStringLists()
        {
            var dates = new Set_TestDate();
            dates.LoadInline(new DateTime(2026, 1, 1), new DateTime(2026, 1, 2));

            var lists = VariableBuilder.ConvertSetsToStringLists(dates);

            Assert.Single(lists);
            Assert.Equal(new[] { "2026-01-01", "2026-01-02" }, lists[0]);
        }

        [Fact]
        public void SetName_StripsSetPrefix()
        {
            var s = new Set_TestEmp();
            Assert.Equal("TestEmp", s.SetName);
        }
    }
}
