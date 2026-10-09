using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    /// <summary>檢查目標式常數是否保留、忽略右側暫存算式時是否警告、無界限值的表示方式，以及 AddMIPStart 的共用處理。</summary>
    [Collection("Logging")]
    public class PoolSemanticsAndMipStartTests
    {
        private static MockEngine NewEngine()
        {
            var e = new MockEngine();
            e.Build();
            return e;
        }

        private static string StartLog(string prefix)
        {
            string tag = prefix + "_" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);
            return tag;
        }

        private static string ReadLog(string tag)
        {
            string file = Directory.GetFiles(FolderDir.Log.GetPath(), $"{tag}_*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }


        [Fact(DisplayName = "目標式常數項：AddLHS(常數) 帶進 SetObjective")]
        public void CreateMinimize_PassesLhsConstantToSolver()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.AddLHS(5.0);
            engine.AddLHS(2.5);
            engine.CreateMinimize();

            Assert.Equal(7.5, engine.ObjectiveConstant);
            Assert.Equal(7.5, engine.ObjectiveConstantResult);
            Assert.False(engine.HasPool);
        }

        [Fact(DisplayName = "目標式常數項：soft penalty 重設目標式時常數項保留")]
        public void SoftPenalty_KeepsObjectiveConstant()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.AddLHS(4.0);
            engine.CreateMinimize();

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateGreaterEqualSoft(3.0, 10.0);

            Assert.Equal(4.0, engine.ObjectiveConstantResult);
        }

        [Fact(DisplayName = "目標式遇到 RHS pool：warn 並捨棄")]
        public void CreateMinimize_WithRhsPool_WarnsAndDiscards()
        {
            string tag = StartLog("ObjectiveRhsIgnored");
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x", "y" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.AddRHS(2.0, new VarS { S = "y" });
            engine.AddRHS(3.0);
            engine.CreateMinimize();

            Assert.Equal(0, engine.ObjectiveConstantResult);
            Assert.False(engine.HasPool);
            Assert.Contains("[右側暫存區略過] 位置=CreateMinimize 名稱=<目標式> 右側項數量=1 右側常數=3 原因=目標式只採用左側 結果=略過", ReadLog(tag));
        }

        [Fact(DisplayName = "空 pool 略過：清空常數，不殘留到下一條限制式")]
        public void EmptyConstraint_ClearsConstantsBeforeNextConstraint()
        {
            string tag = StartLog("EmptyClearsPool");
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x" });

            // 範本常見寫法：迴圈加變數項（這一組剛好一個都沒有）→ AddRHS(1) → CreateEqual
            engine.AddLHS(2.0);
            engine.AddRHS(1.0);
            Assert.False(engine.CreateEqual("Assign@empty"));
            Assert.Equal((0, 0.0, 0, 0.0), engine.PoolState);
            Assert.Contains("[限制式為空] 名稱=Assign@empty 常數=2 右側項數量=0 右側常數=1 原因=暫存區為空 結果=略過", ReadLog(tag));

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.AddRHS(1.0);
            Assert.Equal((1, 0.0, 0, 1.0), engine.PoolState);
            Assert.True(engine.CreateEqual("Assign@x"));
        }

        [Fact(DisplayName = "Create*(rhs) 左式沒有變數項：右側變數項與常數一起清空")]
        public void EmptyConstraintWithRhs_ClearsRhsTermsAndConstants()
        {
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x" });

            engine.AddRHS(1.0, new VarS { S = "x" });
            engine.AddRHS(3.0);
            Assert.False(engine.CreateLessEqual(5.0, "Cap@empty"));
            Assert.Equal((0, 0.0, 0, 0.0), engine.PoolState);
        }

        [Fact(DisplayName = "軟性限制式空 pool 略過：常數一起清空")]
        public void EmptySoftConstraint_ClearsConstants()
        {
            var engine = NewEngine();

            engine.AddRHS(2.0);
            Assert.False(engine.CreateLessEqualSoft(1.0, 10.0, "Soft@empty"));
            Assert.Equal((0, 0.0, 0, 0.0), engine.PoolState);
        }


        [Fact(DisplayName = "CreateRange 遇到 RHS pool：warn、仍建立、pool 清空")]
        public void CreateRange_WithRhsPool_WarnsAndStillBuilds()
        {
            string tag = StartLog("RangeRhsIgnored");
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x", "y" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.AddRHS(1.0, new VarS { S = "y" });
            Assert.True(engine.CreateRange(0, 10, "Band@x"));

            Assert.Contains("Band@x", engine.BuiltConstraints);
            Assert.False(engine.HasPool);
            Assert.Contains("[右側暫存區略過] 位置=CreateRange 名稱=Band@x 右側項數量=1 右側常數=0 原因=範圍限制式只採用左側 結果=略過", ReadLog(tag));
        }

        [Fact(DisplayName = "CreateRange 沒有 RHS pool：不發 warn")]
        public void CreateRange_WithoutRhsPool_NoWarn()
        {
            string tag = StartLog("RangeRhsClean");
            var engine = NewEngine();
            engine.BuildCVs<VarS>(new[] { "x" });

            engine.AddLHS(1.0, new VarS { S = "x" });
            engine.CreateRange(0, 10, "Band@x");

            Assert.DoesNotContain("[右側暫存區略過]", ReadLog(tag));
        }


        [Fact(DisplayName = "無上限統一為 OptBounds.Infinity = 1E20（BuildVars 與 soft 彈性變數）")]
        public void UnboundedVariables_UseCplexInfinity()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableC_Amt>(new[] { "A" });
            engine.BuildVars<VariableI_Cnt>(new[] { "A" });
            engine.AddLHS(1.0, new VariableC_Amt { S = "A" });
            engine.CreateLessEqualSoft(5.0, 1.0, "Cap@A");

            Assert.Equal(1E20, OptBounds.Infinity);
            Assert.All(engine.BuiltVars, v => Assert.True(v.Ub == OptBounds.Infinity, $"{v.Name} ub={v.Ub}"));
        }


        [Fact(DisplayName = "AddMIPStart：名稱對應到變數後交給 solver，未知名稱略過")]
        public void AddMIPStart_ResolvesNamesAndSkipsUnknown()
        {
            string tag = StartLog("MipStartUnknown");
            var engine = NewEngine();
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B" });

            int applied = engine.AddMIPStart(new Dictionary<string, double>
            {
                ["VariableB_Pick@A"] = 1,
                ["VariableB_Pick@B"] = 0,
                ["VariableB_Pick@Z"] = 1,
            }, "warm");

            Assert.Equal(2, applied);
            var start = Assert.Single(engine.MipStarts);
            Assert.Equal("warm", start.Name);
            Assert.Equal(new[] { ("VariableB_Pick@A", 1.0), ("VariableB_Pick@B", 0.0) }, start.Entries.OrderBy(e => e.Var));
            Assert.Contains("[起始解變數找不到] 名稱=warm 數量=3 找不到變數數量=1 範例=VariableB_Pick@Z 原因=變數不在模型內 結果=略過", ReadLog(tag));
        }

        [Fact(DisplayName = "AddMIPStart：LP 模型 warn 後略過")]
        public void AddMIPStart_LpModel_Skips()
        {
            string tag = StartLog("MipStartLp");
            var engine = NewEngine();
            engine.BuildVars<VariableC_Amt>(new[] { "A" });

            int applied = engine.AddMIPStart(new Dictionary<string, double> { ["VariableC_Amt@A"] = 3 });

            Assert.Equal(0, applied);
            Assert.Empty(engine.MipStarts);
            Assert.Contains("[起始解略過] 名稱=<自動> 數量=1 原因=線性規劃模型 結果=略過", ReadLog(tag));
        }

        [Fact(DisplayName = "AddMIPStart：沒有任何名稱對得上 → 略過不呼叫 solver")]
        public void AddMIPStart_NoMatchingVariable_Skips()
        {
            var engine = NewEngine();
            engine.BuildVars<VariableB_Pick>(new[] { "A" });

            int applied = engine.AddMIPStart(new Dictionary<string, double> { ["Other@A"] = 1 });

            Assert.Equal(0, applied);
            Assert.Empty(engine.MipStarts);
        }

        [Fact(DisplayName = "AddMIPStart：values 為 null 丟 ArgumentNullException")]
        public void AddMIPStart_NullValues_Throws()
        {
            var engine = NewEngine();
            Assert.Throws<ArgumentNullException>(() => engine.AddMIPStart(null));
        }
    }
}
