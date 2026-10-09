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
            // 直接傳入 string[] 時，C# 會把它當成 params 陣列本身；框架應將字串成員視為同一個集合。
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
        public void BuildVars_LegacyPrefixX_CreatesContinuous()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableX_LegacyAmt>(new List<string> { "A" });
            Assert.Equal(VarType.Continuous, Assert.Single(engine.BuiltVars).Type);
        }

        [Fact]
        public void BuildVars_LegacyPrefixY_CreatesInteger()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableY_LegacyCnt>(new List<string> { "A" });
            Assert.Equal(VarType.Integer, Assert.Single(engine.BuiltVars).Type);
        }

        [Fact]
        public void BuildVars_InvalidPrefix_ThrowsWithNamingGuide()
        {
            var engine = NewEngine();
            var ex = Assert.Throws<ArgumentException>(() => engine.BuildVars<VarS>(new List<string> { "A" }));
            Assert.Contains("VariableB_", ex.Message);
            Assert.Contains("VariableC_", ex.Message);
            Assert.Contains("VariableI_", ex.Message);
            Assert.Equal(0, engine.VariableCount);
        }

        // ── BuildVars：依位置逐維比對維度數量與型別 ───────────────

        [Fact]
        public void BuildVars_SetRowsAndTuples_MatchingTypes_CreatesSameNames()
        {
            var date = new DateTime(2026, 1, 1);
            var fromRows = NewEngine();
            fromRows.BuildVars<VariableC_ArcFlowByDate>(
                new List<Set_Arc> { new() { NodeFrom = "A", NodeTo = "B" } },
                new List<DateTime> { date });
            var fromTuples = NewEngine();
            fromTuples.BuildVars<VariableC_ArcFlowByDate>(
                new List<(string, string)> { ("A", "B") },
                new List<DateTime> { date });

            Assert.Equal("VariableC_ArcFlowByDate@A@B@2026_01_01", Assert.Single(fromRows.BuiltVars).Name);
            Assert.Equal(fromRows.BuiltVars, fromTuples.BuiltVars);
        }

        [Fact]
        public void BuildVars_SwappedDimensionTypes_ThrowsBeforeCreatingVars()
        {
            var engine = NewEngine();
            var ex = Assert.Throws<ArgumentException>(() => engine.BuildVars<VariableC_ArcFlowByDate>(
                new List<DateTime> { new(2026, 1, 1) },
                new List<string> { "A" },
                new List<string> { "B" }));
            Assert.Contains("維度型別不一致", ex.Message);
            Assert.Contains("NodeFrom", ex.Message);
            Assert.Equal(0, engine.VariableCount);
        }

        [Fact]
        public void BuildVars_IntSetForStringDimension_Throws()
        {
            var engine = NewEngine();
            var ex = Assert.Throws<ArgumentException>(() => engine.BuildVars<VariableC_Amt>(new List<int> { 1 }));
            Assert.Contains("維度型別不一致", ex.Message);
            Assert.Equal(0, engine.VariableCount);
        }

        [Fact]
        public void BuildVars_EmptySetWithWrongType_StillThrows()
        {
            // 型別取自宣告型別，空集合也會比對
            var engine = NewEngine();
            var ex = Assert.Throws<ArgumentException>(() => engine.BuildVars<VariableC_ArcFlowByDate>(
                new List<Set_Arc>(),
                new List<string>()));
            Assert.Contains("維度型別不一致", ex.Message);
        }

        [Fact]
        public void BuildVars_DimensionCountMismatch_Throws()
        {
            var engine = NewEngine();
            var ex = Assert.Throws<ArgumentException>(() => engine.BuildVars<VariableC_ArcFlowWrongArity>(
                new List<Set_Arc> { new() { NodeFrom = "A", NodeTo = "B" } }));
            Assert.Contains("維度數量不一致", ex.Message);
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
            Assert.Equal(4, engine.VariableCount);
        }

        [Fact]
        public void BuildBVs_2D_CreatesCartesianProduct()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarDG>(
                new List<DateTime> { new(2026, 1, 1), new(2026, 1, 2) },
                new List<string>   { "D", "N" });
            Assert.Equal(4, engine.VariableCount);
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

        // ── ModelType：由變數型別組成推導 LP / MILP / IP / BP ──────────

        [Fact]
        public void ModelType_NoVariables_IsLP()
        {
            var engine = NewEngine();
            Assert.Equal(ModelType.LP, engine.ModelType);
        }

        [Fact]
        public void ModelType_OnlyContinuous_IsLP()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableC_Amt>(new List<string> { "A", "B" });
            Assert.Equal(ModelType.LP, engine.ModelType);
        }

        [Fact]
        public void ModelType_ContinuousAndBinary_IsMILP()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableC_Amt>(new List<string> { "A", "B" });
            engine.BuildVars<VariableB_Pick>(new List<string> { "A" });
            Assert.Equal(ModelType.MILP, engine.ModelType);
        }

        [Fact]
        public void ModelType_ContinuousAndInteger_IsMILP()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableC_Amt>(new List<string> { "A" });
            engine.BuildVars<VariableI_Cnt>(new List<string> { "A" });
            Assert.Equal(ModelType.MILP, engine.ModelType);
        }

        [Fact]
        public void ModelType_OnlyInteger_IsIP()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableI_Cnt>(new List<string> { "A" });
            Assert.Equal(ModelType.IP, engine.ModelType);
        }

        [Fact]
        public void ModelType_IntegerAndBinary_IsIP()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableI_Cnt>(new List<string> { "A" });
            engine.BuildVars<VariableB_Pick>(new List<string> { "A", "B" });
            Assert.Equal(ModelType.IP, engine.ModelType);
        }

        [Fact]
        public void ModelType_OnlyBinary_IsBP()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableB_Pick>(new List<string> { "A", "B" });
            Assert.Equal(ModelType.BP, engine.ModelType);
        }

        [Fact]
        public void ModelType_SoftConstraintElasticVars_StayLP()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableC_Amt>(new List<string> { "A" });
            engine.AddLHS(1.0, new VariableC_Amt { S = "A" });
            engine.CreateLessEqualSoft(5.0, 1.0);
            Assert.Equal(ModelType.LP, engine.ModelType);
        }

        [Fact]
        public void ModelType_BinaryWithSoftConstraint_BecomesMILP()
        {
            // 彈性變數是連續變數，solver 實際面對的是混整數模型
            var engine = NewEngine();
            engine.BuildVars<VariableB_Pick>(new List<string> { "A" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.CreateLessEqualSoft(0.0, 1.0);
            Assert.Equal(ModelType.MILP, engine.ModelType);
        }


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
            Assert.False(engine.HasPool);
        }

        [Fact]
        public void CreateEqual_EmptyPool_ReturnsFalse()
        {
            var engine = NewEngine();
            bool result = engine.CreateEqual("NoTermCon");
            Assert.False(result);
            Assert.Empty(engine.BuiltConstraints);
        }

        [Fact]
        public void CreateEqual_DuplicateName_SkipsSecond()
        {
            var engine = NewEngine();
            engine.BuildBVs<VarS>(new List<string> { "X" });

            engine.AddLHS(1.0, new VarS { S = "X" });
            engine.CreateEqual("DupCon");

            // 名稱重複，應略過。
            engine.AddLHS(1.0, new VarS { S = "X" });
            engine.CreateEqual("DupCon");

            Assert.Single(engine.BuiltConstraints);
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


        [Fact]
        public void SupportsSoftConstraints_MockEngine_IsTrue()
        {
            // 軟性限制式由 EngineBase 建立；只要 engine 實作底層建模方法，就能使用這項功能。
            var engine = NewEngine();
            Assert.True(engine.SupportsSoftConstraints);
        }

        [Fact]
        public void CreateLessEqualSoft_AddsElasticVarAndConstraint()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });

            bool ok = engine.CreateLessEqualSoft(5.0, 1.0);

            Assert.True(ok);
            Assert.Single(engine.BuiltConstraints);                  // 建立一條軟性限制式
            Assert.Equal("Soft_LessEqual_1", engine.BuiltConstraints[0]);
            Assert.Equal(2, engine.VariableCount);                        // x + 一個 surplus 彈性變數
        }

        [Fact]
        public void CreateLessEqualSoft_WithName_UsesProvidedName()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });

            bool ok = engine.CreateLessEqualSoft(5.0, 1.0, "CapacitySoft");

            Assert.True(ok);
            Assert.Single(engine.BuiltConstraints);
            Assert.Equal("CapacitySoft", engine.BuiltConstraints[0]);
            Assert.Contains(engine.BuiltVars, v => v.Name == "Surplus_CapacitySoft");
        }

        [Fact]
        public void CreateGreaterEqualSoft_WithName_UsesProvidedName()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });

            bool ok = engine.CreateGreaterEqualSoft(5.0, 1.0, "DemandSoft");

            Assert.True(ok);
            Assert.Single(engine.BuiltConstraints);
            Assert.Equal("DemandSoft", engine.BuiltConstraints[0]);
            Assert.Contains(engine.BuiltVars, v => v.Name == "Deficit_DemandSoft");
        }

        [Fact]
        public void CreateEqualSoft_AddsTwoElasticVars()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new List<string> { "x" });
            engine.AddLHS(1.0, new VarS { S = "x" });

            bool ok = engine.CreateEqualSoft(5.0, 1.0, "Demand");

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
                ConstraintSense.LessEqual => engine.CreateLessEqualSoft(5.0, 1.0, owner, "X"),
                ConstraintSense.GreaterEqual => engine.CreateGreaterEqualSoft(5.0, 1.0, owner, "X"),
                _ => engine.CreateEqualSoft(5.0, 1.0, owner, "X")
            };

            Assert.True(created);
            Assert.Equal("Constraint_Test@X", Assert.Single(engine.BuiltConstraints));
            Assert.Contains(engine.BuiltVars, variable => variable.Name == expectedElasticVariable);
        }
    }
}
