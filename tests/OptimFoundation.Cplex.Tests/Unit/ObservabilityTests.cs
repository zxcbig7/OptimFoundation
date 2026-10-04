using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using OptimFoundation.Db.Oracle;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    [Collection("Logging")]
    public class ObservabilityTests
    {
        private sealed class TestConstraint : ConstraintBase { }

        private sealed class ThrowingPropertyConfig : ISolverConfig
        {
            public double? TimeLimit { get; set; }
            public double? MipGap { get; set; }
            public int? Threads { get; set; }
            public int? Seed { get; set; }
            public int? Emphasis { get; set; }
            public double? FeasibilityTol { get; set; }
            public double? OptimalityTol { get; set; }
            public int? RootAlgorithm { get; set; }
            public int? Presolve { get; set; }
            public double? HeuristicEffort { get; set; }
            public double? MemoryLimitMb { get; set; }
            public bool LogToConsole { get; set; }
            public string LogFilePath { get; set; } = "";
            public string BrokenProperty => throw new InvalidOperationException("snapshot getter failed");
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

        private static void AssertOneNamingError(string log, string context, string value, string reason)
        {
            string payload = $"[模型名稱不合法] 位置={context} 值={value} 原因={reason} 結果=中止";
            int count = 0;
            int offset = 0;
            while ((offset = log.IndexOf(payload, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += payload.Length;
            }
            Assert.Equal(1, count);
        }

        private static void AssertErrorCodeCount(string log, string eventCode, int expected)
        {
            string marker = $"[{eventCode}]";
            int count = log.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.Contains("| 錯誤 |", StringComparison.Ordinal)
                    && line.Contains(marker, StringComparison.Ordinal));
            Assert.Equal(expected, count);
        }

        [Fact]
        public void LogTimestamp_UsesSecondPrecision()
        {
            string tag = StartLog("TimestampPrecision");
            Logging.Info("timestamp_marker");

            string line = ReadLog(tag).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Single(x => x.Contains("timestamp_marker"));

            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} \|", line);
        }

        [Fact]
        public void EmptyConstraint_LogsKeywordAndName()
        {
            string tag = StartLog("EmptyConstraint");
            var engine = new MockEngine();
            engine.Build();

            bool created = engine.CreateEqual("Demand@D1");

            Assert.False(created);
            string log = ReadLog(tag);
            Assert.Contains("[限制式為空]", log);
            Assert.Contains("[限制式為空] 名稱=Demand@D1", log);
            Assert.Contains("原因=暫存區為空", log);
            Assert.Contains("結果=略過", log);
        }

        [Fact]
        public void DuplicateConstraint_LogsThatExistingConstraintWasKept()
        {
            string tag = StartLog("DuplicateConstraint");
            var engine = new MockEngine();
            engine.Build();
            engine.BuildBVs<VarS>(new[] { "X" });

            engine.AddLHS(1.0, new VarS { S = "X" });
            engine.CreateEqual("UniqueName");
            engine.AddLHS(1.0, new VarS { S = "X" });
            engine.CreateEqual("UniqueName");

            string log = ReadLog(tag);
            Assert.Contains("[限制式重複]", log);
            Assert.Contains("原因=名稱重複", log);
            Assert.Contains("名稱=UniqueName", log);
            Assert.Contains("結果=保留原值", log);
        }

        [Fact]
        public void DuplicateVariable_SkipsAndLogsThatExistingVariableWasKept()
        {
            string tag = StartLog("DuplicateVariable");
            var engine = new MockEngine();
            engine.Build();

            // 資料裡重複的 Set 成員，以及同一型別再建一次重疊的 sets
            engine.BuildBVs<VarS>(new List<string> { "A", "B", "A" });
            engine.BuildBVs<VarS>(new List<string> { "B", "C" });

            Assert.Equal(new[] { "VarS@A", "VarS@B", "VarS@C" }, engine.BuiltVars.Select(v => v.Name));
            Assert.Equal((5, 3), engine.VariableBuildCounts["VarS"]);
            string log = ReadLog(tag);
            Assert.Contains("[變數重複] 變數類別=VarS 數量=1 範例=VarS@A 原因=名稱已存在 結果=保留原值", log);
            Assert.Contains("[變數重複] 變數類別=VarS 數量=1 範例=VarS@B 原因=名稱已存在 結果=保留原值", log);
        }

        [Fact]
        public void DuplicateSoftConstraint_SkipsSecondAndKeepsElasticVariable()
        {
            string tag = StartLog("DuplicateSoftConstraint");
            var engine = new MockEngine();
            engine.Build();
            engine.BuildCVs<VarS>(new[] { "X" });

            engine.AddLHS(1.0, new VarS { S = "X" });
            Assert.True(engine.CreateLessEqualSoft(5.0, 1.0, "Budget"));
            engine.AddLHS(1.0, new VarS { S = "X" });
            Assert.True(engine.CreateLessEqualSoft(7.0, 1.0, "Budget"));

            Assert.Equal(1, engine.BuiltVars.Count(v => v.Name == "Surplus_Budget"));
            Assert.Equal(1, engine.SoftPenaltyTermCount);
            Assert.Equal(new[] { "Budget" }, engine.BuiltConstraints);
            Assert.False(engine.HasPool);
            Assert.Contains("[限制式重複] 名稱=Budget", ReadLog(tag));
        }

        [Fact]
        public void NamedSoftConstraint_LogsConfigurationAfterSuccessfulBuild()
        {
            string tag = StartLog("NamedSoftConstraint");
            var engine = new MockEngine();
            engine.Build();
            engine.BuildCVs<VarS>(new[] { "X" });
            engine.AddLHS(1.0, new VarS { S = "X" });

            bool created = engine.CreateLessEqualSoft(12.5, 3.0, "SetupBudget");

            Assert.True(created);
            string log = ReadLog(tag);
            Assert.Contains("[軟性限制式建立完成]", log);
            Assert.Contains("名稱=SetupBudget", log);
            Assert.Contains("方向=LessEqual", log);
            Assert.Contains("右側值=12.5", log);
            Assert.Contains("懲罰=3", log);
            Assert.DoesNotContain("結果=", log.Split('\n').Single(line => line.Contains("[軟性限制式建立完成]")));
        }

        [Fact]
        public void Solve_LogsAutomaticBuildActualExpectedCounts()
        {
            string tag = StartLog("AutomaticBuildSummary");
            var engine = new MockEngine();
            engine.Build();
            engine.BuildBVs<VarS>(new[] { "A", "B" });

            engine.AddLHS(1.0, new VarS { S = "A" });
            engine.CreateEqual("Demand@A");
            engine.AddLHS(1.0, new VarS { S = "A" });
            engine.CreateEqual("Demand@A");
            engine.CreateLessEqual("Capacity@A");

            engine.AddLHS(1.0, new VarS { S = "A" });
            engine.CreateMinimize();
            engine.Solve();

            string log = ReadLog(tag);
            Assert.Contains("[變數建立完成] 變數類別=VarS 數量=2/2", log);
            Assert.Contains("[變數建立摘要] 數量=2/2 變數類別數量=1 模型內變數數量=2", log);
            Assert.Contains("[目標式建立開始] 方向=Minimize 項數量=1", log);
            Assert.Contains("[目標式建立完成] 方向=Minimize 項數量=1 常數=0", log);
            Assert.DoesNotContain("[目標式建立完成] 方向=Minimize 項數量=1 常數=0 結果=", log);
            Assert.Contains("[限制式建立完成] 限制式類別=Demand 數量=1/2", log);
            Assert.Contains("[限制式建立完成] 限制式類別=Capacity 數量=0/1", log);
            Assert.Contains("[限制式建立摘要] 數量=1/3 限制式類別數量=2 模型內限制式數量=", log);
        }

        [Fact]
        public void Solve_LogsModelTypeWithVariableTypeCounts()
        {
            string tag = StartLog("ModelTypeSummary");
            var engine = new MockEngine();
            engine.Build();
            engine.BuildVars<VariableC_Amt>(new[] { "A", "B" });
            engine.BuildVars<VariableI_Cnt>(new[] { "A" });
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B", "C" });

            engine.AddLHS(1.0, new VariableC_Amt { S = "A" });
            engine.CreateMinimize();
            engine.Solve();

            Assert.Contains("[模型類型摘要] 模型類型=MILP 連續變數數量=2 整數變數數量=1 二元變數數量=3", ReadLog(tag));
        }

        [Fact]
        public void Solve_ContinuousOnly_LogsModelTypeLP()
        {
            string tag = StartLog("ModelTypeSummaryLP");
            var engine = new MockEngine();
            engine.Build();
            engine.BuildVars<VariableC_Amt>(new[] { "A", "B" });

            engine.AddLHS(1.0, new VariableC_Amt { S = "A" });
            engine.CreateMinimize();
            engine.Solve();

            Assert.Contains("[模型類型摘要] 模型類型=LP 連續變數數量=2 整數變數數量=0 二元變數數量=0", ReadLog(tag));
        }

        [Fact]
        public void Solve_BinaryOnly_LogsModelTypeBP()
        {
            string tag = StartLog("ModelTypeSummaryBP");
            var engine = new MockEngine();
            engine.Build();
            engine.BuildVars<VariableB_Pick>(new[] { "A", "B" });

            engine.AddLHS(1.0, new VariableB_Pick { S = "A" });
            engine.CreateMinimize();
            engine.Solve();

            Assert.Contains("[模型類型摘要] 模型類型=BP 連續變數數量=0 整數變數數量=0 二元變數數量=2", ReadLog(tag));
        }

        [Fact]
        public void BuildVars_InvalidPrefix_LogsBeforeThrow()
        {
            string tag = StartLog("VariablePrefixUnknown");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(() => engine.BuildVars<VarS>(new[] { "A" }));

            string log = ReadLog(tag);
            Assert.Contains("[變數型別不合法]", log);
            Assert.Contains("變數類別=VarS", log);
            Assert.Contains("結果=中止", log);
        }

        [Fact]
        public void ExplicitBuild_TypeMismatch_LogsBeforeThrow()
        {
            string tag = StartLog("VariableTypeMismatch");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(
                () => engine.BuildBVs<VariableC_Amt>(new[] { "A" }));

            string log = ReadLog(tag);
            Assert.Contains("[變數型別不一致]", log);
            Assert.Contains("變數類別=VariableC_Amt", log);
            Assert.Contains("宣告型別=Continuous", log);
            Assert.Contains("指定型別=Binary", log);
            Assert.Contains("結果=中止", log);
        }

        [Fact]
        public void VariableKeyGenerationFailure_LogsBeforeThrow()
        {
            string tag = StartLog("VariableBuildFailure");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(
                () => engine.BuildVars<VariableC_Amt>(new[] { true }));

            string log = ReadLog(tag);
            Assert.Contains("[變數維度集合不合法]", log);
            Assert.Contains("值=System.Boolean[]", log);
            Assert.Contains("原因=不支援的集合型別", log);
            Assert.Contains("結果=中止", log);
            AssertErrorCodeCount(log, "變數維度集合不合法", 1);
        }

        [Fact]
        public void EdgeCase1_ReservedCharacter_LogsOneErrorBeforeThrow()
        {
            string tag = StartLog("NamingEdge1");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(() => engine.BuildCVs<VariableC_Amt>(new[] { "E-01" }));

            AssertOneNamingError(ReadLog(tag), "集合 #1", "E-01", "含保留字元");
        }

        [Fact]
        public void EdgeCase2_NegativeDimension_LogsOneErrorBeforeThrow()
        {
            string tag = StartLog("NamingEdge2");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(() => engine.BuildCVs<VarInt>(new[] { -5 }));

            AssertOneNamingError(ReadLog(tag), "集合 #1", "-5", "含保留字元");
        }

        [Fact]
        public void EdgeCase4_DateTimeSubSecond_LogsOneErrorBeforeThrow()
        {
            string tag = StartLog("NamingEdge4");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(() => engine.BuildCVs<VarDG>(
                new[] { new DateTime(2026, 8, 9, 1, 2, 3, 250, DateTimeKind.Utc) }, new[] { "A" }));

            AssertOneNamingError(ReadLog(tag), "集合 #1", "2026-08-09T01:02:03.2500000Z", "日期含秒以下精度");
        }

        [Fact]
        public void EdgeCase10_NullDimensions_LogsOneErrorBeforeThrow()
        {
            string tag = StartLog("NamingEdge10");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(() => engine.CreateEqual(new TestConstraint(), null!));

            AssertOneNamingError(ReadLog(tag), nameof(TestConstraint), "<空值>", "維度陣列為空");
        }

        [Fact]
        public void EdgeCase11_InvalidExplicitName_LogsOneErrorBeforeThrow()
        {
            string tag = StartLog("NamingEdge11");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(() => engine.CreateEqual("Demand@2026-08-09"));

            AssertOneNamingError(ReadLog(tag), "CreateEqual 片段 #1", "2026-08-09", "含保留字元");
        }

        [Fact]
        public void PublicApiBoundary_UnexpectedException_LogsOnceAndRethrowsOriginal()
        {
            string tag = StartLog("UnexpectedBoundary");
            var original = new InvalidOperationException("boundary boom");

            var thrown = Assert.Throws<InvalidOperationException>(
                () => OptData.Load<object>(() => throw original));

            Assert.Same(original, thrown);
            string log = ReadLog(tag);
            Assert.Contains("[資料載入失敗]", log);
            Assert.Contains("位置=Load", log);
            Assert.Contains("值=System.Object", log);
            Assert.Contains("原因=boundary boom", log);
            Assert.Contains("結果=中止", log);
            AssertErrorCodeCount(log, "資料載入失敗", 1);
        }

        [Fact]
        public void PublicApiBoundary_DoesNotDuplicateAnAlreadyLoggedException()
        {
            string tag = StartLog("BoundaryDeduplication");
            var original = new InvalidOperationException("inner boom");

            var thrown = Assert.Throws<InvalidOperationException>(() =>
                OptData.Load<object>(() => throw Logging.ErrorOnce(
                    original, "內層操作失敗", "底層失敗", "InnerOperation", "bad-value", "inner_reason")));

            Assert.Same(original, thrown);
            string log = ReadLog(tag);
            Assert.Contains("[內層操作失敗]", log);
            Assert.DoesNotContain("[資料載入失敗]", log);
            AssertErrorCodeCount(log, "內層操作失敗", 1);
        }

        [Fact]
        public void ParameterLookup_MissingRow_LogsWarningAndReturnsNull()
        {
            string tag = StartLog("ParameterMissing");
            var rows = new List<Parameter_GncProfit>
            {
                new Parameter_GncProfit { GncItem = "Desk", QTY = 12.5 }
            };

            var row = rows.FindParameterOrLog(parameter => parameter.GncItem == "Chair", "Chair");

            Assert.Null(row);
            string log = ReadLog(tag);
            Assert.Contains("[參數找不到]", log);
            Assert.Contains("型別=Parameter_GncProfit", log);
            Assert.Contains("鍵=Chair", log);
            Assert.Contains("原因=沒有符合的資料列", log);
            Assert.Contains("結果=回傳空值", log);

        }

        [Fact]
        public void DataValidation_Issue_LogsWarningAndContinues()
        {
            string tag = StartLog("DataValidationWarn");

            var data = OptData.Load(() => new GncDataload(
                new[] { "Desk", "Desk" },
                new[] { new Parameter_GncProfit { GncItem = "Desk", Profit = double.NaN } }));

            Assert.Equal(2, data.DataIssues.Count);
            string log = ReadLog(tag);
            Assert.Contains("[資料不合法]", log);
            Assert.Contains("名稱=Set_GncItem 原因=鍵重複", log);
            Assert.Contains("名稱=Parameter_GncProfit 原因=數值不合法", log);
            Assert.Contains("結果=繼續", log);
            Assert.Contains("[資料載入摘要] 集合數量=1 參數數量=1 問題數量=2", log);
            Assert.DoesNotContain("| 錯誤 |", log);
        }

        [Fact]
        public void DataValidation_InvalidKey_LogsWarningWithoutNamingError()
        {
            string tag = StartLog("DataValidationInvalidKey");

            var data = OptData.Load(() => new GncDataload(
                new[] { "Chair A" },
                Array.Empty<Parameter_GncProfit>()));

            var issue = Assert.Single(data.DataIssues);
            Assert.Equal(DataIssueKind.InvalidKey, issue.Kind);
            string log = ReadLog(tag);
            Assert.Contains("名稱=Set_GncItem 原因=鍵不合法", log);
            Assert.Contains("原因=含空白字元", log);
            Assert.DoesNotContain("[模型名稱不合法]", log);
            Assert.DoesNotContain("| 錯誤 |", log);
        }

        [Fact]
        public void FrameworkSource_ProactiveThrowsMustUseErrorOnce()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OptimFoundation.sln")))
                directory = directory.Parent;

            Assert.NotNull(directory);
            string sourceRoot = Path.Combine(directory!.FullName, "src");
            var offenders = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => File.ReadLines(file)
                    .Select((line, index) => new { File = file, Line = index + 1, Text = line.Trim() }))
                .Where(item => !item.Text.StartsWith("//", StringComparison.Ordinal)
                    && (Regex.IsMatch(item.Text, @"\bthrow\s+new\b")
                        || Regex.IsMatch(item.Text, @"\bthrow\s+[A-Za-z_]\w*\s*;")))
                .Select(item => $"{Path.GetRelativePath(directory.FullName, item.File)}:{item.Line}: {item.Text}")
                .ToArray();

            Assert.True(offenders.Length == 0,
                "Framework throws must use Logging.ErrorOnce and boundary rethrows must use bare throw;:" +
                Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void ConfigSnapshot_WhenPropertyGetterFails_LogsOmission()
        {
            string tag = StartLog("SnapshotFallback");

            ConfigSnapshot snapshot = ConfigSnapshot.From(new ThrowingPropertyConfig());

            Assert.DoesNotContain("BrokenProperty", snapshot.SolverSpecific.Keys);
            string log = ReadLog(tag);
            Assert.Contains("[設定快照屬性略過]", log);
            Assert.Contains("結果=略過", log);
            Assert.Contains("屬性=BrokenProperty", log);
            Assert.Contains("snapshot getter failed", log);
        }

        [Fact]
        public void OracleConversion_InvalidValue_ThrowsInsteadOfWritingNull()
        {
            string tag = StartLog("OracleConversion");
            MethodInfo method = typeof(OracleDbCtrl).GetMethod(
                "ConvertToDbType",
                BindingFlags.Static | BindingFlags.NonPublic)!;

            var invocation = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(null, new object[] { typeof(int), "not-an-int" }));

            Assert.IsType<FormatException>(invocation.InnerException);
            string log = ReadLog(tag);
            Assert.Contains("[Oracle 資料轉型失敗]", log);
            Assert.Contains("位置=ConvertToDbType", log);
            Assert.Contains("值=not-an-int", log);
            Assert.Contains("原因=不支援或不合法的值", log);
            Assert.Contains("結果=中止", log);
            AssertErrorCodeCount(log, "Oracle 資料轉型失敗", 1);
        }
    }
}
