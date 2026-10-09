using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;
using ObjectiveSense = OptimFoundation.Core.ObjectiveSense;

namespace OptimFoundation.Cplex.Tests.Integration
{
    /// <summary>
    /// 使用 CPLEX 檢查：宣告了卻沒被引用的變數會被 WARN 點名；模型結構與目標式方向取自 CPLEX，並寫進實驗說明檔。
    /// 需要 CPLEX DLL，不存在時全部直接結束。
    /// </summary>
    [Collection("Logging")]
    public class UnreferencedVariableIntegrationTests
    {
        private static readonly bool CplexAvailable =
            File.Exists(@"C:\IBM\ILOG\CPLEX_Studio2211\cplex\bin\x64_win64\ILOG.CPLEX.dll");

        private static CplexConfig Config() => new CplexConfig { TimeLimit = 30 };

        private static ProjectConfig Quiet() => new ProjectConfig { EnableSolverLog = false };

        private static OptEngine NewEngine()
        {
            var engine = new OptEngine(Config(), Quiet());
            engine.Build();
            return engine;
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

        private static List<string> UnreferencedWarnings(string log) =>
            log.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("| 警告 |", StringComparison.Ordinal)
                    && line.Contains("[變數未引用]", StringComparison.Ordinal))
                .ToList();

        // 3 binary + 1 integer + 1 continuous + 1 軟性 Surplus；一般 / 範圍 / 軟性限制式各一條；maximize
        // max 3·Pick_C + Amt_A − 2·Surplus  s.t. Pick_A + Pick_B ≤ 1, 0 ≤ Cnt_A ≤ 5, Amt_A − Surplus ≤ 10 → obj 13
        private static void BuildConsistentModel(OptEngine engine)
        {
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" });
            engine.BuildVars<VariableI_Cnt>(new[] { "A" });
            engine.BuildVars<VariableC_Amt>(new[] { "A" });

            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "B" });
            engine.AddRHS(1.0);
            engine.CreateLessEqual("PickLimit");

            engine.AddLHS(1.0, new VariableI_Cnt { S = "A" });
            engine.CreateRange(0, 5, "CntRange");

            engine.AddLHS(3.0, new VariableB_Pick { S = "C" });
            engine.AddLHS(1.0, new VariableC_Amt { S = "A" });
            engine.CreateMaximize();

            engine.AddLHS(1.0, new VariableC_Amt { S = "A" });
            engine.CreateLessEqualSoft(10, 2.0, "AmtBudget");
        }

        [Fact(DisplayName = "自建模型（含範圍 / 軟性 / maximize）：沒有未引用變數，模型結構與目標式方向取自 CPLEX")]
        public void Authored_NoWarning_MetricsFromCplex()
        {
            if (!CplexAvailable) return;
            string tag = StartLog("UnrefAuthored");

            using var engine = NewEngine();
            BuildConsistentModel(engine);
            Assert.True(engine.Solve());
            Assert.Equal(13, engine.GetObjectiveValue(), 6);

            var m = engine.LastMetrics!;
            Assert.Equal(6, m.VarCount);
            Assert.Equal(3, m.BinaryVarCount);
            Assert.Equal(1, m.IntegerVarCount);
            Assert.Equal(2, m.ContinuousVarCount);
            Assert.Equal(3, m.ConstraintCount);
            Assert.Equal(ObjectiveSense.Maximize, m.ObjectiveSense);
            Assert.Empty(UnreferencedWarnings(ReadLog(tag)));
        }

        // MPS 沒有目標式方向欄位：CPLEX 把 maximize 模型寫成「係數取負的 minimize」，讀回來就是 minimize、目標值反號
        [Theory(DisplayName = "匯入模型檔：ObjectiveSense 依檔案內容同步，沒有未引用變數")]
        [InlineData(".lp", ObjectiveSense.Maximize, 13.0)]
        [InlineData(".mps", ObjectiveSense.Minimize, -13.0)]
        [InlineData(".sav", ObjectiveSense.Maximize, 13.0)]
        public void Imported_ObjectiveSenseSynced(string extension, ObjectiveSense fileSense, double objective)
        {
            if (!CplexAvailable) return;
            string fileName = $"UnrefImport_{Guid.NewGuid():N}{extension}";
            using (var source = NewEngine())
            {
                BuildConsistentModel(source);
                source.ExportModel(fileName);
            }
            string tag = StartLog("UnrefImport");

            using var engine = NewEngine();
            engine.ReadModel(fileName);
            Assert.True(engine.Solve());

            Assert.Equal(fileSense, engine.ObjectiveSense);
            Assert.Equal(fileSense, engine.LastMetrics!.ObjectiveSense);
            Assert.Equal(objective, engine.GetObjectiveValue(), 6);
            if (extension == ".sav")
            {
                // .sav 是 CPLEX 原生格式，結構原樣保留；文字格式的範圍限制式會多一個 Rg 輔助變數，不比絕對數
                Assert.Equal(6, engine.LastMetrics!.VarCount);
                Assert.Equal(3, engine.LastMetrics!.ConstraintCount);
            }
            Assert.Empty(UnreferencedWarnings(ReadLog(tag)));
        }

        [Fact(DisplayName = "宣告沒引用的變數 → CPLEX 不收，WARN 點名該變數，照常求解")]
        public void UnreferencedVariable_WarnsWithName()
        {
            if (!CplexAvailable) return;
            string tag = StartLog("UnrefWarn");

            using var engine = NewEngine();
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "B" });
            engine.AddRHS(1.0);
            engine.CreateLessEqual("PickLimit");
            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.CreateMaximize();
            Assert.True(engine.Solve());

            Assert.Equal(2, engine.LastMetrics!.VarCount);
            string line = Assert.Single(UnreferencedWarnings(ReadLog(tag)));
            Assert.Contains("已宣告的變數沒被任何限制式或目標式引用", line);
            Assert.Contains("數量=1", line);
            Assert.Contains("變數類別=VariableB_Pick=1", line);
            Assert.Contains("範例=VariableB_Pick@C", line);
        }

        // CPLEX 移除限制式後仍保留先前已收錄的欄；ResetConstraint 後只用部分變數重建，不該誤報
        [Fact(DisplayName = "ResetConstraint 後只用部分變數重建 → CPLEX 保留已收錄的欄，不 WARN")]
        public void ResetConstraint_PartialRebuild_NoWarning()
        {
            if (!CplexAvailable) return;

            using var engine = NewEngine();
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" });
            foreach (var s in new[] { "A", "B", "C" }) engine.AddLHS(1.0, new VariableB_Pick { S = s });
            engine.AddRHS(2.0);
            engine.CreateLessEqual("PickLimit");
            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.CreateMaximize();
            Assert.True(engine.Solve());

            engine.ResetConstraint();
            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.AddLHS(1.0, new VariableB_Pick { S = "B" });
            engine.AddRHS(1.0);
            engine.CreateLessEqual("PickLimit");
            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.CreateMaximize();
            string tag = StartLog("UnrefResetConstraint");
            Assert.True(engine.Solve());

            Assert.Equal(3, engine.LastMetrics!.VarCount);
            Assert.Equal(1, engine.LastMetrics!.ConstraintCount);
            Assert.Empty(UnreferencedWarnings(ReadLog(tag)));
        }

        [Fact(DisplayName = "實驗：說明檔 model 區段有 CPLEX 的數量與目標式方向，log 有未引用變數 WARN")]
        public void Experiment_WritesModelSectionAndWarning()
        {
            if (!CplexAvailable) return;

            var consistent = new OptModel("Consistent")
                .AddVariables(e => e.BuildVars<VariableB_Pick>(new[] { "A", "B" }))
                .AddConstraints(e =>
                {
                    e.AddLHS(1.0, new VariableB_Pick { S = "A" });
                    e.AddLHS(1.0, new VariableB_Pick { S = "B" });
                    e.AddRHS(1.0);
                    e.CreateLessEqual("PickLimit");
                })
                .AddObjective(e =>
                {
                    e.AddLHS(1.0, new VariableB_Pick { S = "A" });
                    e.CreateMaximize();
                });
            var unreferenced = new OptModel("Unreferenced")
                .AddVariables(e => e.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" }))
                .AddConstraints(e =>
                {
                    e.AddLHS(1.0, new VariableB_Pick { S = "A" });
                    e.AddLHS(1.0, new VariableB_Pick { S = "B" });
                    e.AddRHS(1.0);
                    e.CreateLessEqual("PickLimit");
                })
                .AddObjective(e =>
                {
                    e.AddLHS(1.0, new VariableB_Pick { S = "A" });
                    e.CreateMaximize();
                });

            string name = $"UnrefExp_{Guid.NewGuid():N}";
            new OptProject(name, retentionDays: 0).Experiment("exp", "unreferenced variables")
                .AddModel(consistent)
                .AddModel(unreferenced)
                .AddSolverConfig("base", Config())
                .Run();

            string meta = File.ReadAllText(FolderDir.Experiment.GetPathFile($"{name}-exp-meta.csv"));
            Assert.DoesNotContain("schema", meta);
            Assert.Contains("model,Consistent.objectiveSense,Maximize", meta);
            Assert.Contains("model,Unreferenced.varCount,2", meta);
            Assert.DoesNotContain("modelStats", meta);

            string line = Assert.Single(UnreferencedWarnings(ReadLog($"{name}-exp_exp")));
            Assert.Contains("範例=VariableB_Pick@C", line);
        }
    }
}
