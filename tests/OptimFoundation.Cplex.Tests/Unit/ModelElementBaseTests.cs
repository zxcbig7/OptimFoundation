using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    public class ModelElementBaseTests
    {
        // ── ToString ───────────────────────────────────────────────────────

        [Fact]
        public void ToString_String_ReturnsTypeNameAtValue()
        {
            var v = new VarS { S = "Employee1" };
            Assert.Equal("VarS@Employee1", v.ToString());
        }

        [Fact]
        public void ToString_DateTime_FormatsAsYYYYMMDD()
        {
            var v = new VarDG { D = new DateTime(2026, 1, 15), G = "N" };
            Assert.Equal("VarDG@2026-01-15@N", v.ToString());
        }

        [Fact]
        public void ToString_DefaultValues_ReturnsDefaultStrings()
        {
            // 未設定的 string property 預設為 ""
            var v = new VarS();
            Assert.Equal("VarS@", v.ToString());
        }

        // ── InitClassBySets ────────────────────────────────────────────────

        [Fact]
        public void InitClassBySets_CorrectCount_SetsProperties()
        {
            var v = new VarDG();
            v.InitClassBySets(new DateTime(2026, 6, 1), "D");
            Assert.Equal(new DateTime(2026, 6, 1), v.D);
            Assert.Equal("D", v.G);
        }

        [Fact]
        public void InitClassBySets_WrongCount_ThrowsArgumentException()
        {
            var v = new VarDG();
            var ex = Assert.Throws<ArgumentException>(() => v.InitClassBySets("only_one_arg"));
            Assert.Contains("VarDG", ex.Message);
            Assert.Contains("2", ex.Message); // 期望 2 個
        }

        [Fact]
        public void InitClassBySets_TypeConversion_StringToInt()
        {
            var v = new VarInt();
            v.InitClassBySets("42");
            Assert.Equal(42, v.N);
        }

        [Fact]
        public void InitClassBySets_TypeMismatch_ThrowsInvalidCastException()
        {
            var v = new VarDG();
            // 傳入無法轉換為 DateTime 的字串
            Assert.Throws<InvalidCastException>(() =>
                v.InitClassBySets("not_a_date", "G"));
        }

        // ── PropertyInfo 快取（間接驗證：多次呼叫結果一致）────────────────

        [Fact]
        public void ToString_CalledMultipleTimes_ReturnsConsistentResult()
        {
            var v = new VarDG { D = new DateTime(2026, 2, 14), G = "E" };
            string key1 = v.ToString();
            string key2 = v.ToString();
            string key3 = v.ToString();
            Assert.Equal(key1, key2);
            Assert.Equal(key2, key3);
        }
    }
}
