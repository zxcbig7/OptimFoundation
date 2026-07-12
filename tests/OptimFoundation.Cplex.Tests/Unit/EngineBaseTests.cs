using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    public class EngineBaseTests
    {
        private static MockEngine NewEngine()
        {
            var e = new MockEngine();
            e.Build();
            return e;
        }

        // ── 變數建立 ───────────────────────────────────────────────────────

        [Fact]
        public void BuildBVs_1D_CreatesCorrectCount()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "E1", "E2", "E3" });
            Assert.Equal(3, engine.varCount);
        }

        [Fact]
        public void BuildBVs_1D_BareStringArray_CreatesCorrectCount()
        {
            // 單獨傳 string[]：共變誤 bind 成 params 陣列本身，framework 應還原成單一 set
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new[] { "E1", "E2", "E3" });
            Assert.Equal(3, engine.varCount);
        }

        [Fact]
        public void BuildCVs_WithBounds_BareStringArray_CreatesCorrectCount()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(0, 100, new[] { "E1", "E2" });
            Assert.Equal(2, engine.varCount);
        }

        // ── BuildVars：型別由類別名前綴決定（命名天條 B_/X_/I_）────────────

        [Fact]
        public void BuildVars_PrefixB_CreatesBinaryWithUnitBounds()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableB_Pick>(new List<string> { "A", "B" });
            Assert.Equal(2, engine.varCount);
            Assert.All(engine.BuiltVars, v =>
            {
                Assert.Equal(VarType.Binary, v.Type);
                Assert.Equal(0, v.Lb);
                Assert.Equal(1, v.Ub);
            });
        }

        [Fact]
        public void BuildVars_PrefixX_CreatesContinuous()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableX_Amt>(new List<string> { "A" });
            Assert.Equal(VarType.Continuous, Assert.Single(engine.BuiltVars).Type);
        }

        [Fact]
        public void BuildVars_PrefixI_CreatesInteger()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableI_Cnt>(new List<string> { "A" });
            Assert.Equal(VarType.Integer, Assert.Single(engine.BuiltVars).Type);
        }

        [Fact]
        public void BuildVars_InvalidPrefix_ThrowsWithNamingGuide()
        {
            var engine = NewEngine();
            var ex = Assert.Throws<ArgumentException>(() => engine.BuildVars<VarS>(new List<string> { "A" }));
            // 錯誤訊息必須教正確取名（三種前綴都要出現）
            Assert.Contains("VariableB_", ex.Message);
            Assert.Contains("VariableX_", ex.Message);
            Assert.Contains("VariableI_", ex.Message);
            Assert.Equal(0, engine.varCount);
        }

        [Fact]
        public void BuildBVs_2D_ArraySets_CreatesCartesianProduct()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarDG>(
                new[] { new DateTime(2026, 1, 1), new DateTime(2026, 1, 2) },
                new[] { "D", "N" });
            Assert.Equal(4, engine.varCount);  // 2 × 2
        }

        [Fact]
        public void BuildBVs_2D_CreatesCartesianProduct()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarDG>(
                new List<DateTime> { new(2026, 1, 1), new(2026, 1, 2) },
                new List<string>   { "D", "N" });
            Assert.Equal(4, engine.varCount);  // 2 × 2
        }

        [Fact]
        public void BuildBVs_KeyFormat_MatchesToString()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "E1" });
            var expected = new VarS { S = "E1" }.ToString();  // "VarS@E1"
            Assert.Contains(expected, engine.GetSetVarNames<VarS>());
        }

        [Fact]
        public void BuildBVs_CalledTwiceSameType_AppendsVars()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "A" });
            engine.BuildBVs<VarS>(new List<string> { "B" });
            Assert.Equal(2, engine.varCount);
            Assert.Equal(2, engine.GetSetVarNames<VarS>().Length);
        }

        [Fact]
        public void GetSetVarNames_UnknownType_ReturnsEmpty()
        {
            var engine = NewEngine();
            Assert.Empty(engine.GetSetVarNames<VarDG>());
        }

        // ── AddLHS / AddRHS 錯誤處理 ──────────────────────────────────────

        [Fact]
        public void AddLHS_NullVarSpec_ReturnsFalse()
        {
            var engine = NewEngine();
            bool result = engine.AddLHS(1.0, null!);
            Assert.False(result);
        }

        [Fact]
        public void AddLHS_MissingVar_ThrowsKeyNotFound()
        {
            var engine = NewEngine();
            var v = new VarS { S = "Ghost" };
            var ex = Assert.Throws<KeyNotFoundException>(() => engine.AddLHS(1.0, v));
            Assert.Contains("VarS@Ghost", ex.Message);
            Assert.Contains("AddLHS", ex.Message);
        }

        [Fact]
        public void AddRHS_MissingVar_ThrowsKeyNotFound()
        {
            var engine = NewEngine();
            var v = new VarS { S = "Ghost" };
            var ex = Assert.Throws<KeyNotFoundException>(() => engine.AddRHS(1.0, v));
            Assert.Contains("AddRHS", ex.Message);
        }

        [Fact]
        public void AddLHS_ExistingVar_ReturnsTrue()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "E1" });
            bool result = engine.AddLHS(2.0, new VarS { S = "E1" });
            Assert.True(result);
        }

        // ── Pool 行為 ──────────────────────────────────────────────────────

        [Fact]
        public void HasPool_AfterAddLHS_IsTrue()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "X" });
            engine.AddLHS(1.0, new VarS { S = "X" });
            Assert.True(engine.HasPool);
        }

        [Fact]
        public void ClearPool_ResetsHasPool()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "X" });
            engine.AddLHS(1.0, new VarS { S = "X" });
            engine.ClearPool();
            Assert.False(engine.HasPool);
        }

        [Fact]
        public void CreateEqual_BuildsConstraintAndClearsPool()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "X" });
            engine.AddLHS(1.0, new VarS { S = "X" });
            engine.AddRHS(5.0);
            engine.CreateEqual("Con@X");
            Assert.Contains("Con@X", engine.BuiltConstraints);
            Assert.False(engine.HasPool);  // pool 已清空
        }

        [Fact]
        public void CreateEqual_EmptyPool_ReturnsFalse()
        {
            var engine = NewEngine();
            bool result = engine.CreateEqual("EmptyCon");
            Assert.False(result);
            Assert.Empty(engine.BuiltConstraints);
        }

        [Fact]
        public void CreateEqual_DuplicateName_SkipsSecond()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "X" });

            // 第一條
            engine.AddLHS(1.0, new VarS { S = "X" });
            engine.CreateEqual("DupCon");

            // 第二條：相同 name → 應該 skip
            engine.AddLHS(1.0, new VarS { S = "X" });
            engine.CreateEqual("DupCon");

            Assert.Single(engine.BuiltConstraints);  // 只建了一條
        }

        // ── 目標式 ─────────────────────────────────────────────────────────

        [Fact]
        public void CreateMinimize_SetsObjectiveSense()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateMinimize();
            Assert.Equal(ObjectiveSense.Minimize, engine.ObjectiveSenseResult);
        }

        [Fact]
        public void CreateMaximize_SetsObjectiveSense()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateMaximize();
            Assert.Equal(ObjectiveSense.Maximize, engine.ObjectiveSenseResult);
        }

        // ── Soft Constraints ──────────────────────────────────────────────

        [Fact]
        public void SupportsSoftConstraints_MockEngine_IsTrue()
        {
            // 通用軟性限制式實作已上移 EngineBase，任何提供 primitive 的 engine 預設皆支援
            var engine = NewEngine();
            Assert.True(engine.SupportsSoftConstraints);
        }

        [Fact]
        public void CreateLeSoft_AddsElasticVarAndConstraint()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });

            bool ok = engine.CreateLeSoft(5.0, 1.0);

            Assert.True(ok);
            Assert.Single(engine.BuiltConstraints);                  // 建立一條軟性限制式
            Assert.Equal("Soft_LessEqual_1", engine.BuiltConstraints[0]);
            Assert.Equal(2, engine.varCount);                        // x + 一個 surplus 彈性變數
        }

        [Fact]
        public void CreateEqSoft_AddsTwoElasticVars()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });

            bool ok = engine.CreateEqSoft(5.0, 1.0, "Demand");

            Assert.True(ok);
            Assert.Single(engine.BuiltConstraints);
            Assert.Equal("Demand", engine.BuiltConstraints[0]);
            Assert.Equal(3, engine.varCount);  // x + Delta_Neg + Delta_Pos
        }
    }
}
