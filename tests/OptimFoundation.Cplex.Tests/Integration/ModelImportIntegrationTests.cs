using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Integration
{
    /// <summary>
    /// 檢查讀入 .lp / .mps / .sav 後是否重建變數與限制式的名稱索引；缺少 CPLEX DLL 時，測試方法直接返回。
    /// 每個測試先用程式建立並匯出模型，再讀回來，確認索引、取解與衝突分析等功能仍可使用。
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
            engine.CreateGreaterEqual(10, "Demand");
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
            engine.ExportModel(fileName);
            return fileName;
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

        [Theory(DisplayName = "匯入後 re-index：變數與限制式索引復原，解值正確")]
        [InlineData(".lp")]
        [InlineData(".mps")]
        [InlineData(".sav")]
        public void ReadModel_ReindexesAndSolves(string extension)
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(extension);

            using var engine = NewEngine();
            var counts = engine.ReadModel(fileName);

            Assert.Equal(2, counts.VarCount);
            Assert.Equal(2, counts.ConstraintCount);
            Assert.Equal(2, engine.VariableCount);
            Assert.Equal(2, engine.ConstraintCount);
            Assert.Equal(ModelType.LP, engine.ModelType);

            Assert.True(engine.Solve());
            Assert.Equal(SolveStatus.Optimal, engine.Status);
            Assert.Equal(26.0, engine.GetObjectiveValue(), 6);
        }

        [Theory(DisplayName = "副檔名大小寫不拘、支援 .gz / .bz2 壓縮，匯出與匯入 log 都帶 format")]
        [InlineData(".LP", "LP")]
        [InlineData(".sav.gz", "SAV.GZ")]
        [InlineData(".mps.bz2", "MPS.BZ2")]
        public void ExportImport_ExtensionVariants_RoundTripAndLogFormat(string extension, string expectedFormat)
        {
            if (!CplexAvailable) return;
            string tag = "ModelFormat_" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);

            string fileName = ExportReferenceModel(extension);
            using var engine = NewEngine();
            var counts = engine.ReadModel(fileName);

            Assert.Equal(2, counts.VarCount);
            Assert.Equal(2, counts.ConstraintCount);
            string log = ReadLog(tag);
            Assert.Contains($"格式={expectedFormat}", log.Split('\n').Single(l => l.Contains("[模型匯出完成]")));
            Assert.Contains($"格式={expectedFormat}", log.Split('\n').Single(l => l.Contains("[模型讀入完成]")));
        }

        [Theory(DisplayName = "匯入後 ModelType 依原模型變數型別判定：Binary / Integer / 連續不混淆")]
        [InlineData(".lp", false, false, ModelType.BP)]
        [InlineData(".sav", false, false, ModelType.BP)]
        [InlineData(".lp", true, false, ModelType.IP)]
        [InlineData(".sav", true, false, ModelType.IP)]
        [InlineData(".lp", true, true, ModelType.MILP)]
        [InlineData(".sav", true, true, ModelType.MILP)]
        public void ReadModel_IntegerModel_ReportsModelType(string extension, bool withInteger, bool withContinuous, ModelType expected)
        {
            if (!CplexAvailable) return;

            string fileName = $"ImportModelTypeTest_{Guid.NewGuid():N}{extension}";
            using (var source = NewEngine())
            {
                source.BuildVars<VariableB_Pick>(new[] { "a" });
                source.AddLHS(1.0, new VariableB_Pick { S = "a" });
                if (withInteger)
                {
                    source.BuildVars<VariableI_Cnt>(new[] { "a" });
                    source.AddLHS(1.0, new VariableI_Cnt { S = "a" });
                }
                if (withContinuous)
                {
                    source.BuildVars<VariableC_Amt>(new[] { "a" });
                    source.AddLHS(1.0, new VariableC_Amt { S = "a" });
                }
                source.CreateMinimize();
                source.ExportModel(fileName);
            }

            using var engine = NewEngine();
            engine.ReadModel(fileName);

            Assert.Equal(expected, engine.ModelType);
        }

        [Fact(DisplayName = "匯入後以變數名取解：名稱沿用原模型的 TypeName@dim 格式")]
        public void ReadModel_ReadsSolutionByName()
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(".lp");

            using var engine = NewEngine();
            engine.ReadModel(fileName);
            Assert.True(engine.Solve());

            Assert.Equal(4.0, engine.GetVariableValue("VarS@a"), 6);
            Assert.Equal(6.0, engine.GetVariableValue("VarS@b"), 6);

            var solution = engine.GetSolution();
            Assert.Equal(2, solution.Count);
            Assert.Equal(4.0, solution["VarS@a"], 6);
        }

        [Fact(DisplayName = "匯入後依 solver 型別分類取解可用；名稱沿用 TypeName@dim 時型別化取解也可用")]
        public void ReadModel_TypeBasedAndTypedSetSolutionBothWork()
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(".lp");

            using var engine = NewEngine();
            engine.ReadModel(fileName);
            Assert.True(engine.Solve());

            // 依 INumVar.Type 分類：不看名字，匯入模式照常運作
            Assert.Equal(2, engine.GetCVSolution().Count);
            Assert.Empty(engine.GetBVSolution());

            // 依型別名篩選變數池：匯入的名稱沿用 VarS@…，同樣篩得到
            var values = engine.GetSetVarValues<VarS>();
            Assert.Equal(2, values.Count);
            Assert.Equal(4.0, values["VarS@a"], 6);
            Assert.Equal(6.0, values["VarS@b"], 6);
            Assert.Equal(2, engine.GetAllVarNames().Length);
        }

        [Fact(DisplayName = "匯入的模型 infeasible 時仍跑 IIS 分析")]
        public void ReadModel_Infeasible_RunsConflictAnalysis()
        {
            if (!CplexAvailable) return;

            // a >= 10 且 a <= 4：必然無解
            string fileName = $"ImportInfeasible_{Guid.NewGuid():N}.lp";
            using (var source = NewEngine())
            {
                source.BuildCVs<VarS>(0, 100, new[] { "a" });
                source.AddLHS(1.0, new VarS { S = "a" });
                source.CreateGreaterEqual(10, "Floor");
                source.AddLHS(1.0, new VarS { S = "a" });
                source.CreateLessEqual(4, "Ceiling");
                source.AddLHS(1.0, new VarS { S = "a" });
                source.CreateMinimize();
                source.ExportModel(fileName);
            }

            using var engine = NewEngine();
            engine.ReadModel(fileName);

            Assert.False(engine.Solve());
            Assert.Equal(SolveStatus.Infeasible, engine.Status);
            // 匯入時若沒有填入 _constraints，RunConflictAnalysis 會因限制式清單為空而直接返回，無法列出衝突。
            Assert.NotEmpty(engine.GetConflictConstraints());
        }

        [Fact(DisplayName = "匯入後 LastMetrics 的規模欄位不是 0")]
        public void ReadModel_MetricsCarryModelSize()
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(".lp");

            using var engine = NewEngine();
            engine.ReadModel(fileName);
            Assert.True(engine.Solve());

            Assert.Equal(2, engine.LastMetrics.VarCount);
            Assert.Equal(2, engine.LastMetrics.ConstraintCount);
        }

        // ── OptModel.ReadModel：接上 OptProject / OptExperiment ────────────

        [Fact(DisplayName = "OptModel.ReadModel 走 OptProject.Solve 可求解，模型名取自檔名")]
        public void ReadModel_RunsThroughOptProject()
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(".lp");
            var model = OptModel.ReadModel(fileName);

            Assert.Equal(Path.GetFileNameWithoutExtension(fileName), model.Name);
            Assert.Equal(fileName, model.SourceFile);

            using var project = new OptProject("ImportProject_" + Guid.NewGuid().ToString("N"), retentionDays: 0)
                .LoadConfig(new ProjectConfig { EnableSolverLog = false });

            Assert.True(project.Solve(model, new CplexConfig()));
            Assert.Equal(26.0, project.Engine.GetObjectiveValue(), 6);
        }

        [Fact(DisplayName = "OptExperiment 可拿檔案模型跑多組設定，每個 trial 的規模欄位都正確")]
        public void ReadModel_RunsThroughOptExperiment()
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(".lp");

            var experiment = new OptProject("ImportExp_" + Guid.NewGuid().ToString("N"), retentionDays: 0)
                .Experiment("exp", "檔案模型 × 兩組設定")
                .AddModel(OptModel.ReadModel(fileName, "RefModel"))
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

        [Fact(DisplayName = "ReadModel 後仍可追加限制式：匯入先跑，再套用錄下的步驟")]
        public void ReadModel_ThenAddConstraints_AppliesBoth()
        {
            if (!CplexAvailable) return;

            string fileName = ExportReferenceModel(".lp");

            // 追加 a >= 4 不改變最佳解，但限制式數要多一條
            var model = OptModel.ReadModel(fileName)
                .AddConstraints(engine =>
                {
                    engine.AddLHS(1.0, "VarS@a");
                    engine.CreateGreaterEqual(4, "ExtraFloor");
                });

            using var project = new OptProject("ImportAppend_" + Guid.NewGuid().ToString("N"), retentionDays: 0)
                .LoadConfig(new ProjectConfig { EnableSolverLog = false });

            Assert.True(project.Solve(model, new CplexConfig()));
            Assert.Equal(3, project.Engine.ConstraintCount);
            Assert.Equal(26.0, project.Engine.GetObjectiveValue(), 6);
        }


        [Fact(DisplayName = "檔案不存在丟 FileNotFoundException")]
        public void ReadModel_MissingFile_Throws()
        {
            if (!CplexAvailable) return;

            using var engine = NewEngine();
            Assert.Throws<FileNotFoundException>(() => engine.ReadModel("no_such_model.lp"));
        }

        [Fact(DisplayName = "空檔名丟 ArgumentException")]
        public void ReadModel_EmptyFileName_Throws()
        {
            if (!CplexAvailable) return;

            using var engine = NewEngine();
            Assert.Throws<ArgumentException>(() => engine.ReadModel("  "));
        }

        [Fact(DisplayName = "Build() 之前呼叫丟 InvalidOperationException")]
        public void ReadModel_BeforeBuild_Throws()
        {
            if (!CplexAvailable) return;

            using var engine = new OptEngine(new CplexConfig(), new ProjectConfig { EnableSolverLog = false });
            Assert.Throws<InvalidOperationException>(() => engine.ReadModel("anything.lp"));
        }

        [Theory(DisplayName = "匯出副檔名不支援：交給 CPLEX 前就擋下，留 Error Log、不產生檔案")]
        [InlineData(".txt")]
        [InlineData("")]
        [InlineData(".bz2")]
        public void ExportModel_UnsupportedExtension_ThrowsAndLogs(string extension)
        {
            if (!CplexAvailable) return;
            string tag = "ModelExportExt_" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);
            string fileName = $"ExportExtTest_{Guid.NewGuid():N}{extension}";

            using var engine = NewEngine();
            BuildReferenceModel(engine);

            Assert.Throws<ArgumentException>(() => engine.ExportModel(fileName));
            Assert.False(File.Exists(FolderDir.Model.GetPathFile(fileName)));
            Assert.Contains(
                $"[模型匯出失敗] 位置=ExportModel 值={fileName} 原因=不支援的副檔名 支援格式=.lp|.mps|.sav[.gz|.bz2] 結果=中止",
                ReadLog(tag));
        }

        [Fact(DisplayName = "匯入副檔名不支援：先於檔案存在檢查，丟 ArgumentException 並留 Error Log")]
        public void ReadModel_UnsupportedExtension_ThrowsAndLogs()
        {
            if (!CplexAvailable) return;
            string tag = "ModelImportExt_" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);

            using var engine = NewEngine();

            Assert.Throws<ArgumentException>(() => engine.ReadModel("no_such_model.txt"));
            Assert.Contains(
                "[模型讀入失敗] 位置=ReadModel 值=no_such_model.txt 原因=不支援的副檔名 支援格式=.lp|.mps|.sav[.gz|.bz2] 結果=中止",
                ReadLog(tag));
        }
    }
}
