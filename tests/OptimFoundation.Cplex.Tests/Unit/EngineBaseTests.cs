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
            Assert.Equal(3, engine.VariableCount);
        }

        [Fact]
        public void BuildBVs_1D_BareStringArray_CreatesCorrectCount()
        {
            // 單獨傳 string[]：共變誤 bind 成 params 陣列本身，framework 應還原成單一 set
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new[] { "E1", "E2", "E3" });
            Assert.Equal(3, engine.VariableCount);
        }

        [Fact]
        public void BuildCVs_WithBounds_BareStringArray_CreatesCorrectCount()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(0, 100, new[] { "E1", "E2" });
            Assert.Equal(2, engine.VariableCount);
        }

        // ── BuildVars：型別由類別名前綴決定（B / C / I）───────────────

        [Fact]
        public void BuildVars_PrefixB_CreatesBinaryWithUnitBounds()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableB_Pick>(new List<string> { "A", "B" });
            Assert.Equal(2, engine.VariableCount);
            Assert.All(engine.BuiltVars, v =>
            {
                Assert.Equal(VarType.Binary, v.Type);
                Assert.Equal(0, v.Lb);
                Assert.Equal(1, v.Ub);
            });
        }

        [Fact]
        public void BuildVars_PrefixI_CreatesInteger()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableI_Cnt>(new List<string> { "A" });
            Assert.Equal(VarType.Integer, Assert.Single(engine.BuiltVars).Type);
        }

        [Fact]
        public void BuildVars_PrefixC_CreatesContinuous()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableC_Amt>(new List<string> { "A" });
            Assert.Equal(VarType.Continuous, Assert.Single(engine.BuiltVars).Type);
        }

        [Fact]
        public void BuildVars_InvalidPrefix_ThrowsWithNamingGuide()
        {
            var engine = NewEngine();
            var ex = Assert.Throws<ArgumentException>(() => engine.BuildVars<VarS>(new List<string> { "A" }));
            // 錯誤訊息必須教完整的三種合法前綴。
            Assert.Contains("VariableB_", ex.Message);
            Assert.Contains("VariableC_", ex.Message);
            Assert.Contains("VariableI_", ex.Message);
            Assert.Equal(0, engine.VariableCount);
        }

        [Fact]
        public void BuildBVs_ContinuousPrefix_ThrowsTypeMismatch()
        {
            var engine = NewEngine();
            var ex = Assert.Throws<ArgumentException>(
                () => engine.BuildBVs<VariableC_Amt>(new List<string> { "A" }));
            Assert.Contains("Continuous", ex.Message);
            Assert.Contains("Binary", ex.Message);
            Assert.Equal(0, engine.VariableCount);
        }

        [Fact]
        public void BuildCVs_IntegerPrefix_ThrowsTypeMismatch()
        {
            var engine = NewEngine();
            Assert.Throws<ArgumentException>(
                () => engine.BuildCVs<VariableI_Cnt>(new List<string> { "A" }));
            Assert.Equal(0, engine.VariableCount);
        }

        [Fact]
        public void BuildIVs_BinaryPrefix_ThrowsTypeMismatch()
        {
            var engine = NewEngine();
            Assert.Throws<ArgumentException>(
                () => engine.BuildIVs<VariableB_Pick>(new List<string> { "A" }));
            Assert.Equal(0, engine.VariableCount);
        }

        [Fact]
        public void BuildBVs_2D_ArraySets_CreatesCartesianProduct()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarDG>(
                new[] { new DateTime(2026, 1, 1), new DateTime(2026, 1, 2) },
                new[] { "D", "N" });
            Assert.Equal(4, engine.VariableCount);  // 2 × 2
        }

        [Fact]
        public void BuildBVs_2D_CreatesCartesianProduct()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarDG>(
                new List<DateTime> { new(2026, 1, 1), new(2026, 1, 2) },
                new List<string>   { "D", "N" });
            Assert.Equal(4, engine.VariableCount);  // 2 × 2
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
        public void BuildBVs_DateTimeKeyFormat_MatchesToStringExactly()
        {
            var engine = NewEngine();
            var date = new DateTime(2026, 1, 15);
            engine.BuildBVs<VarDG>(new[] { date }, new[] { "N" });

            string expected = new VarDG { D = date, G = "N" }.ToString();

            Assert.Equal("VarDG@2026_01_15@N", expected);
            Assert.Contains(expected, engine.GetSetVarNames<VarDG>());
        }

        [Fact]
        public void BuildBVs_CalledTwiceSameType_AppendsVars()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "A" });
            engine.BuildBVs<VarS>(new List<string> { "B" });
            Assert.Equal(2, engine.VariableCount);
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

        [Fact]
        public void CreateEqual_OwnerAndDimensions_ComposesCanonicalName()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "X" });
            engine.AddLHS(1.0, new VarS { S = "X" });

            engine.CreateEqual(new Constraint_Test(), new DateTime(2026, 1, 15), "E1");

            Assert.Equal("Constraint_Test@2026_01_15@E1", Assert.Single(engine.BuiltConstraints));
        }

        [Fact]
        public void CreateLessEqual_OwnerWithMultidimensionalSetRow_FlattensRowTokens()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "X" });
            engine.AddLHS(1.0, new VarS { S = "X" });

            engine.CreateLessEqual(new Constraint_Test(),
                new Set_Arc { NodeFrom = "A", NodeTo = "B" });

            Assert.Equal("Constraint_Test@A@B", Assert.Single(engine.BuiltConstraints));
        }

        [Fact]
        public void CreateRange_OwnerAndDimensions_ComposesCanonicalName()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "X" });
            engine.AddLHS(1.0, new VarS { S = "X" });

            engine.CreateRange(0.0, 10.0, new Constraint_Test(), "X");

            Assert.Equal("Constraint_Test@X", Assert.Single(engine.BuiltConstraints));
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
            Assert.Equal(2, engine.VariableCount);                        // x + 一個 surplus 彈性變數
        }

        [Fact]
        public void CreateLeSoft_WithName_UsesProvidedName()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });

            bool ok = engine.CreateLeSoft(5.0, 1.0, "CapacitySoft");

            Assert.True(ok);
            Assert.Single(engine.BuiltConstraints);
            Assert.Equal("CapacitySoft", engine.BuiltConstraints[0]);
            Assert.Contains(engine.BuiltVars, v => v.Name == "Surplus_CapacitySoft");
        }

        [Fact]
        public void CreateGeSoft_WithName_UsesProvidedName()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });

            bool ok = engine.CreateGeSoft(5.0, 1.0, "DemandSoft");

            Assert.True(ok);
            Assert.Single(engine.BuiltConstraints);
            Assert.Equal("DemandSoft", engine.BuiltConstraints[0]);
            Assert.Contains(engine.BuiltVars, v => v.Name == "Deficit_DemandSoft");
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
            Assert.Equal(3, engine.VariableCount);  // x + Delta_Neg + Delta_Pos
        }

        [Theory]
        [InlineData(ConstraintSense.LessEqual, "Surplus_Constraint_Test@X")]
        [InlineData(ConstraintSense.GreaterEqual, "Deficit_Constraint_Test@X")]
        [InlineData(ConstraintSense.Equal, "Delta_Neg_Constraint_Test@X")]
        public void CreateSoft_OwnerAndDimensions_ComposesCanonicalName(
            ConstraintSense sense,
            string expectedElasticVariable)
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });
            var owner = new Constraint_Test();

            bool created = sense switch
            {
                ConstraintSense.LessEqual => engine.CreateLeSoft(5.0, 1.0, owner, "X"),
                ConstraintSense.GreaterEqual => engine.CreateGeSoft(5.0, 1.0, owner, "X"),
                _ => engine.CreateEqSoft(5.0, 1.0, owner, "X")
            };

            Assert.True(created);
            Assert.Equal("Constraint_Test@X", Assert.Single(engine.BuiltConstraints));
            Assert.Contains(engine.BuiltVars, variable => variable.Name == expectedElasticVariable);
        }
    }
}
