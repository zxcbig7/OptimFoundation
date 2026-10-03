using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    /// <summary>
    /// 對照測試：泛型（TVariable）與 string 兩條路徑必須產出完全相同的結果。
    /// string 版唯一允許的差異是少掉「靠類別才做得到」的檢查（維度數量、類別名前綴與型別一致性）。
    /// </summary>
    public class StringOverloadParityTests
    {
        private static MockEngine NewEngine()
        {
            var e = new MockEngine();
            e.Build();
            return e;
        }

        // ── 建立變數：名稱、界限、型別三者都要一致 ────────────────────────

        [Fact]
        public void BuildBVs_TypedAndString_ProduceIdenticalVars()
        {
            var typed = NewEngine();
            typed.BuildBVs<VarS>(new List<string> { "E1", "E2" });

            var str = NewEngine();
            str.BuildBVs("VarS", new List<string> { "E1", "E2" });

            Assert.Equal(typed.GetSetVarNames<VarS>(), str.GetSetVarNames("VarS"));
            Assert.Equal(typed.BuiltVars, str.BuiltVars);
        }

        [Fact]
        public void BuildCVs_WithBounds_TypedAndString_ProduceIdenticalVars()
        {
            var typed = NewEngine();
            typed.BuildCVs<VarS>(-5, 100, new List<string> { "A", "B" });

            var str = NewEngine();
            str.BuildCVs("VarS", -5, 100, new List<string> { "A", "B" });

            Assert.Equal(typed.BuiltVars, str.BuiltVars);
        }

        [Fact]
        public void BuildIVs_TypedAndString_ProduceIdenticalVars()
        {
            var typed = NewEngine();
            typed.BuildIVs<VarS>(new List<string> { "A" });

            var str = NewEngine();
            str.BuildIVs("VarS", new List<string> { "A" });

            Assert.Equal(typed.BuiltVars, str.BuiltVars);
        }

        [Fact]
        public void BuildVars_TypedPrefixAndStringVarType_ProduceIdenticalVars()
        {
            // 泛型版由 VariableB_ 前綴推出 Binary；string 版沒有類別，改由 VarType 參數明確指定
            var typed = NewEngine();
            typed.BuildVars<VariableB_Pick>(new List<string> { "A", "B" });

            var str = NewEngine();
            str.BuildVars("VariableB_Pick", VarType.Binary, new List<string> { "A", "B" });

            Assert.Equal(typed.BuiltVars, str.BuiltVars);
        }

        [Fact]
        public void BuildBVs_MultiDim_TypedAndString_ProduceIdenticalNames()
        {
            var date = new DateTime(2026, 1, 15);

            var typed = NewEngine();
            typed.BuildBVs<VarDG>(new[] { date }, new[] { "N", "S" });

            var str = NewEngine();
            str.BuildBVs("VarDG", new[] { date }, new[] { "N", "S" });

            Assert.Equal(new[] { "VarDG@2026_01_15@N", "VarDG@2026_01_15@S" }, str.GetSetVarNames("VarDG"));
            Assert.Equal(typed.GetSetVarNames<VarDG>(), str.GetSetVarNames("VarDG"));
        }

        // ── 查詢：typed 與 string 拿到同一個 solver 變數 ──────────────────

        [Fact]
        public void ReadVar_InstanceAndName_ResolveToSameVar()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "E1" });

            var byInstance = engine.ReadVarByInstance(new VarS { S = "E1" });
            var byName = engine.ReadVarByName("VarS@E1");

            Assert.Equal(byInstance, byName);
        }

        [Fact]
        public void ReadVar_ByName_FindsVarNotBuiltByBuildVars()
        {
            // import 情境：名稱不符框架命名慣例，也不是經 Build*Vs 建立
            var engine = NewEngine();
            engine.AddUnregisteredVar("x1");

            Assert.Equal("x1", engine.ReadVarByName("x1"));
        }

        [Fact]
        public void ReadVar_ByName_UnknownName_Throws()
        {
            var engine = NewEngine();
            Assert.Throws<KeyNotFoundException>(() => engine.ReadVarByName("nope"));
        }

        [Fact]
        public void GetSetVarNames_TypedAndString_ReturnSameKeys()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "A", "B" });

            Assert.Equal(engine.GetSetVarNames<VarS>(), engine.GetSetVarNames("VarS"));
        }

        [Fact]
        public void GetSetVarNames_UnknownOrNullSetName_ReturnsEmpty()
        {
            var engine = NewEngine();
            Assert.Empty(engine.GetSetVarNames("NoSuchSet"));
            Assert.Empty(engine.GetSetVarNames(null));
        }

        [Fact]
        public void GetSetVarValues_UnknownOrNullSetName_ReturnsEmpty()
        {
            var engine = NewEngine();
            Assert.Empty(engine.GetSetVarValues("NoSuchSet"));
            Assert.Empty(engine.GetSetVarValues(null));
        }

        // ── 單一變數池：型別查詢以型別名篩選，GetAllVarNames 列出整個池 ────

        [Fact]
        public void GetSetVarNames_FiltersPoolByTypeName()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "A", "B" });
            // 型別名以 VarS 開頭但不是 VarS，不能被篩進來
            engine.BuildBVs("VarSX", new List<string> { "A" });
            // 0 維變數：名稱就是型別名
            engine.BuildCVs("Makespan");
            // 不經 Build*Vs，但名稱符合 VarS@… 也屬於 VarS
            engine.AddUnregisteredVar("VarS@C");

            Assert.Equal(new[] { "VarS@A", "VarS@B", "VarS@C" }, engine.GetSetVarNames<VarS>());
            Assert.Equal(new[] { "Makespan" }, engine.GetSetVarNames("Makespan"));
            Assert.Equal(engine.GetSetVarNames<VarS>(), engine.GetSolution("VarS").Keys);
        }

        [Fact]
        public void GetAllVarNames_ListsWholePool()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "A" });
            engine.AddUnregisteredVar("x1");

            Assert.Equal(new[] { "VarS@A", "x1" }, engine.GetAllVarNames());
        }

        // ── 命名驗證：string 版仍受 ModelNaming 約束 ─────────────────────

        [Theory]
        [InlineData("Has@Separator")]
        [InlineData("1LeadingDigit")]
        [InlineData("has space")]
        [InlineData("")]
        public void BuildBVs_String_InvalidSetName_Throws(string setName)
        {
            var engine = NewEngine();
            Assert.ThrowsAny<Exception>(() => engine.BuildBVs(setName, new List<string> { "A" }));
        }

        // ── Pool API：AddLHS / AddRHS 本來就吃 string ─────────────────────

        [Fact]
        public void AddLHS_AcceptsPlainStringVarName()
        {
            var engine = NewEngine();
            engine.AddUnregisteredVar("x1");

            Assert.True(engine.AddLHS(2.0, "x1"));
            Assert.Equal(1, engine.PoolState.LhsTerms);
        }
    }
}
