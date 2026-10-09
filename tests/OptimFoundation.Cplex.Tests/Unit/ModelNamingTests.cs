using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    /// <summary>
    /// 名稱規則依 CPLEX 匯出 .lp 的實測：會讓 .lp 改名或建模報錯的擋下，其餘放行。
    /// </summary>
    public class ModelNamingTests
    {
        private static MockEngine NewEngine()
        {
            var e = new MockEngine();
            e.Build();
            return e;
        }

        [Theory]
        [InlineData("a,b")]
        [InlineData("台北")]
        [InlineData("x!\"#$%&'()?{}~`;_.")]
        [InlineData("e1")] // e/E、數字、句點、「上」只限名稱開頭，維度值接在 @ 後面不受影響
        [InlineData(".5")]
        [InlineData("1")]
        [InlineData("上")]
        public void Dimension_LpSafeValue_Accepted(string value)
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { value });

            Assert.Equal(new[] { $"VarS@{value}" }, engine.GetAllVarNames());
        }

        [Theory]
        [InlineData("A[1]", "含保留字元")]
        [InlineData("A]", "含保留字元")]
        [InlineData("A|B", "含保留字元")]
        [InlineData("A\u0001B", "含控制字元")]
        [InlineData("A\u007FB", "含控制字元")]
        public void Dimension_LpUnsafeValue_Throws(string value, string reason)
        {
            var engine = NewEngine();

            var ex = Assert.Throws<ArgumentException>(() => engine.BuildCVs<VarS>(new List<string> { value }));
            Assert.Contains($"原因={reason}", ex.Message);
        }

        [Theory]
        [InlineData("Energy")]
        [InlineData("end")]
        [InlineData("上限")] // U+4E0A：CPLEX 建模報 Error 1236
        [InlineData("ĉA")] // U+0109
        [InlineData("1st")]
        [InlineData(".x")]
        public void ExplicitName_InvalidLeadingCharacter_Throws(string name)
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "a" });
            engine.AddLHS(1.0, new VarS { S = "a" });

            var ex = Assert.Throws<ArgumentException>(() => engine.CreateGreaterEqual(1, name));
            Assert.Contains("原因=開頭字元不合法", ex.Message);
        }

        [Theory]
        [InlineData("Floor")]
        [InlineData("需求,總量")]
        [InlineData("一般")] // U+4E00 低位元組 0x00，CPLEX 接受
        [InlineData("éA")]
        public void ExplicitName_LpSafe_Accepted(string name)
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "a" });
            engine.AddLHS(1.0, new VarS { S = "a" });

            Assert.True(engine.CreateGreaterEqual(1, name));
        }

        [Fact]
        public void Name_Exactly254Utf8Bytes_Accepted()
        {
            var engine = NewEngine();
            string value = new string('a', 254 - "VarS@".Length);

            engine.BuildCVs<VarS>(new List<string> { value });

            Assert.Single(engine.GetAllVarNames());
        }

        [Fact]
        public void Name_Over254Utf8Bytes_Throws()
        {
            var engine = NewEngine();
            string value = new string('甲', 84); // 5 + 84 × 3 = 257 位元組

            var ex = Assert.Throws<ArgumentException>(() => engine.BuildCVs<VarS>(new List<string> { value }));
            Assert.Contains("原因=超過254位元組", ex.Message);
        }
    }
}
