using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Integration
{
    /// <summary>
    /// 模型匯入（.lp / .mps / .sav）與 re-index。需要 CPLEX DLL 才能執行，DLL 不存在時全部 Skip。
    /// 每個測試先用 code 建模並匯出，再讀回來，證明匯入後框架索引與下游功能都復原。
    /// </summary>
    [Collection("Logging")]
    public class ModelImportIntegrationTests
    {
        private static readonly bool CplexAvailable =
            File.Exists(@"C:\IBM\ILOG\CPLEX_Studio2211\cplex\bin\x64_win64\ILOG.CPLEX.dll");

        private static OptEngine NewEngine(bool exportLp = false)
        {
            var engine = new OptEngine(
                new CplexConfig { TimeLimit = 30 },
                new ProjectConfig { EnableSolverLog = false, ExportLP = exportLp });
            engine.Build();
            return engine;
        }

        // min 2a + 3b  s.t. a + b >= 10, a <= 4 → a=4, b=6, obj=26
        private static void BuildReferenceModel(OptEngine engine)
        {
            engine.BuildCVs<VarS>(0, 100, new[] { "a", "b" });
            engine.AddLHS(1.0, new VarS { S = "a" });
            engine.AddLHS(1.0, new VarS { S = "b" });
            engine.CreateGreatEqual(10, "Demand");
            engine.AddLHS(1.0, new VarS { S = "a" });
            engine.CreateLessEqual(4, "CapA");
            engine.AddLHS(2.0, new VarS { S = "a" });
            engine.AddLHS(3.0, new VarS { S = "b" });
            engine.CreateMinimize();
        }

        // 建參考模型 → 匯出成指定副檔名 → 回傳 Models 資料夾下的檔名
        private static string ExportReferenceModel(string extension)
        {
            string fileName = $"ImportTest_{Guid.NewGuid():N}{extension}";
            using var engine = NewEngine();
            BuildReferenceModel(engine);
            engine.ExportModelFile(fileName);
            return fileName;
        }

        [Theory(DisplayName = "匯入後 re-index：變數與限制式索引復原，解值正確")]
        [InlineData(".lp")]
        [InlineData(".mps")]
        [InlineData(".sav")]
        public void ImportModel_ReindexesAndSolves(string extension)
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(extension);

            using var engine = NewEngine();
            var counts = engine.ImportModel(fileName);

            Assert.Equal(2, counts.VarCount);
            Assert.Equal(2, counts.ConstraintCount);
            Assert.Equal(2, engine.VariableCount);
            Assert.Equal(2, engine.ConstraintCount);

            Assert.True(engine.Solve());
            Assert.Equal(SolveStatus.Optimal, engine.Status);
            Assert.Equal(26.0, engine.GetObjectiveValue(), 6);
        }

        [Fact(DisplayName = "匯入後以變數名取解：名稱沿用原模型的 TypeName@dim 格式")]
        public void ImportModel_ReadsSolutionByName()
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(".lp");

            using var engine = NewEngine();
            engine.ImportModel(fileName);
            Assert.True(engine.Solve());

            Assert.Equal(4.0, engine.GetVariableValue("VarS@a"), 6);
            Assert.Equal(6.0, engine.GetVariableValue("VarS@b"), 6);

            var solution = engine.GetSolution();
            Assert.Equal(2, solution.Count);
            Assert.Equal(4.0, solution["VarS@a"], 6);
        }

        [Fact(DisplayName = "匯入後依 solver 型別分類取解可用；型別化取解不可用")]
        public void ImportModel_TypeBasedSolutionWorks_TypedSetSolutionDoesNot()
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(".lp");

            using var engine = NewEngine();
            engine.ImportModel(fileName);
            Assert.True(engine.Solve());

            // 依 INumVar.Type 分類：不看名字，匯入模式照常運作
            Assert.Equal(2, engine.GetCVSolution().Count);
            Assert.Empty(engine.GetBVSolution());

            // 依 C# 變數類別分組：匯入的模型沒有類別可對應，VariableSets 保持空的
            Assert.Empty(engine.GetSetVarNames<VarS>());
            Assert.Empty(engine.GetSetVarValues<VarS>());
            Assert.Empty(engine.GetAllVarNames());
            Assert.Equal(2, engine.GetAllVarNames(true).Length);
        }

        [Fact(DisplayName = "匯入的模型 infeasible 時仍跑 IIS 分析")]
        public void ImportModel_Infeasible_RunsConflictAnalysis()
        {
            if (!CplexAvailable) return;

            // a >= 10 且 a <= 4：必然無解
            string fileName = $"ImportInfeasible_{Guid.NewGuid():N}.lp";
            using (var source = NewEngine())
            {
                source.BuildCVs<VarS>(0, 100, new[] { "a" });
                source.AddLHS(1.0, new VarS { S = "a" });
                source.CreateGreatEqual(10, "Floor");
                source.AddLHS(1.0, new VarS { S = "a" });
                source.CreateLessEqual(4, "Ceiling");
                source.AddLHS(1.0, new VarS { S = "a" });
                source.CreateMinimize();
                source.ExportModelFile(fileName);
            }

            using var engine = NewEngine();
            engine.ImportModel(fileName);

            Assert.False(engine.Solve());
            Assert.Equal(SolveStatus.Infeasible, engine.Status);
            // _constraints 若沒 re-index 就是空的，RunConflictAnalysis 會被守衛擋掉、拿不到任何衝突
            Assert.NotEmpty(engine.GetConflictConstraints());
        }

        [Fact(DisplayName = "匯入後 LastMetrics 的規模欄位不是 0")]
        public void ImportModel_MetricsCarryModelSize()
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(".lp");

            using var engine = NewEngine();
            engine.ImportModel(fileName);
            Assert.True(engine.Solve());

            Assert.Equal(2, engine.LastMetrics.VarCount);
            Assert.Equal(2, engine.LastMetrics.ConstraintCount);
        }

        // ── OptModel.FromFile：接上 OptProject / OptExperiment ────────────

        [Fact(DisplayName = "OptModel.FromFile 走 OptProject 可求解，模型名取自檔名")]
        public void FromFile_RunsThroughOptProject()
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(".lp");
            var model = OptModel.FromFile(fileName);

            Assert.Equal(Path.GetFileNameWithoutExtension(fileName), model.Name);
            Assert.Equal(fileName, model.SourceFile);

            using var project = new OptProject(model, "ImportProject_" + Guid.NewGuid().ToString("N"), retentionDays: 0)
                .UseConfig(() => new ProjectConfig { EnableSolverLog = false });

            Assert.True(project.Execute());
            Assert.Equal(26.0, project.Engine.GetObjectiveValue(), 6);
        }

        [Fact(DisplayName = "OptExperiment 可拿檔案模型跑多組設定，每個 trial 的規模欄位都正確")]
        public void FromFile_RunsThroughOptExperiment()
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(".lp");

            var experiment = new OptExperiment("ImportExp_" + Guid.NewGuid().ToString("N"), "檔案模型 × 兩組設定")
                .AddModel(OptModel.FromFile(fileName, "RefModel"))
                .AddConfig("r0", new CplexConfig { TimeLimit = 30 })
                .AddConfig("r1-single-thread", new CplexConfig { TimeLimit = 30, Threads = 1 })
                .Run();

            Assert.Equal(2, experiment.Trials.Count);
            Assert.All(experiment.Trials, t =>
            {
                Assert.Equal("RefModel", t.Model);
                Assert.Equal(SolveStatus.Optimal, t.Metrics.Status);
                Assert.Equal(26.0, t.Metrics.ObjectiveValue, 6);
                Assert.Equal(2, t.Metrics.VarCount);
                Assert.Equal(2, t.Metrics.ConstraintCount);
            });
        }

        [Fact(DisplayName = "FromFile 後仍可追加限制式：匯入先跑，再套用錄下的步驟")]
        public void FromFile_ThenAddConstraints_AppliesBoth()
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(".lp");

            // 追加 a >= 4 不改變最佳解，但限制式數要多一條
            var model = OptModel.FromFile(fileName)
                .AddConstraints(engine =>
                {
                    engine.AddLHS(1.0, "VarS@a");
                    engine.CreateGreatEqual(4, "ExtraFloor");
                });

            using var project = new OptProject(model, "ImportAppend_" + Guid.NewGuid().ToString("N"), retentionDays: 0)
                .UseConfig(() => new ProjectConfig { EnableSolverLog = false });

            Assert.True(project.Execute());
            Assert.Equal(3, project.Engine.ConstraintCount);
            Assert.Equal(26.0, project.Engine.GetObjectiveValue(), 6);
        }

        // ── 錯誤處理 ──────────────────────────────────────────────────────

        [Fact(DisplayName = "檔案不存在丟 FileNotFoundException")]
        public void ImportModel_MissingFile_Throws()
        {
            if (!CplexAvailable) return;

            using var engine = NewEngine();
            Assert.Throws<FileNotFoundException>(() => engine.ImportModel("no_such_model.lp"));
        }

        [Fact(DisplayName = "空檔名丟 ArgumentException")]
        public void ImportModel_EmptyFileName_Throws()
        {
            if (!CplexAvailable) return;

            using var engine = NewEngine();
            Assert.Throws<ArgumentException>(() => engine.ImportModel("  "));
        }

        [Fact(DisplayName = "Build() 之前呼叫丟 InvalidOperationException")]
        public void ImportModel_BeforeBuild_Throws()
        {
            if (!CplexAvailable) return;

            using var engine = new OptEngine(new CplexConfig(), new ProjectConfig { EnableSolverLog = false });
            Assert.Throws<InvalidOperationException>(() => engine.ImportModel("anything.lp"));
        }
    }
}
