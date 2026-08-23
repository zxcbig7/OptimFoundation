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
            string payload = $"[MODEL_NAME_INVALID] 模型名稱驗證失敗 | context={context} value={value} reason={reason} result=aborted";
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
                .Count(line => line.Contains("| ERROR |", StringComparison.Ordinal)
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
            Assert.Contains("CONSTRAINT_EMPTY", log);
            Assert.Contains("未建立限制式", log);
            Assert.Contains("Demand@D1", log);
            Assert.Contains("reason=pool_empty", log);
            Assert.Contains("result=skipped", log);
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
            Assert.Contains("CONSTRAINT_DUPLICATE", log);
            Assert.Contains("略過重複限制式", log);
            Assert.Contains("UniqueName", log);
            Assert.Contains("result=kept_existing", log);
        }

        [Fact]
        public void NamedSoftConstraint_LogsConfigurationAfterSuccessfulBuild()
        {
            string tag = StartLog("NamedSoftConstraint");
            var engine = new MockEngine();
            engine.Build();
            engine.BuildCVs<VarS>(new[] { "X" });
            engine.AddLHS(1.0, new VarS { S = "X" });

            bool created = engine.CreateLeSoft(12.5, 3.0, "SetupBudget");

            Assert.True(created);
            string log = ReadLog(tag);
            Assert.Contains("[軟性限制式建立完成]", log);
            Assert.Contains("name=SetupBudget", log);
            Assert.Contains("sense=LessEqual", log);
            Assert.Contains("rhs=12.5", log);
            Assert.Contains("penalty=3", log);
            Assert.Contains("result=success", log);
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
            Assert.Contains("[變數建立完成] type=VarS count=2/2", log);
            Assert.Contains("[變數建立摘要] 已建立=2/2（實際/預期） 變數類別=1 種 模型內合計=2", log);
            Assert.Contains("[目標式建構開始] sense=Minimize terms=1", log);
            Assert.Contains("[目標式建構完成] sense=Minimize terms=1 result=success", log);
            Assert.Contains("[限制式建立] 群組=Demand 已建立=1/2（實際/預期）", log);
            Assert.Contains("[限制式建立] 群組=Capacity 已建立=0/1（實際/預期）", log);
            Assert.Contains("[限制式建立摘要] 已建立=1/3（實際/預期） 群組=2 個 solver 實際持有=", log);
        }

        [Fact]
        public void BuildVars_InvalidPrefix_LogsBeforeThrow()
        {
            string tag = StartLog("VariablePrefixUnknown");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(() => engine.BuildVars<VarS>(new[] { "A" }));

            string log = ReadLog(tag);
            Assert.Contains("VARIABLE_TYPE_UNKNOWN", log);
            Assert.Contains("type=VarS", log);
            Assert.Contains("result=aborted", log);
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
            Assert.Contains("VARIABLE_TYPE_MISMATCH", log);
            Assert.Contains("type=VariableC_Amt", log);
            Assert.Contains("declared=Continuous", log);
            Assert.Contains("requested=Binary", log);
            Assert.Contains("result=aborted", log);
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
            Assert.Contains("VARIABLE_SET_INVALID", log);
            Assert.Contains("value=System.Boolean[]", log);
            Assert.Contains("reason=unsupported_set_type", log);
            Assert.Contains("result=aborted", log);
            AssertErrorCodeCount(log, "VARIABLE_SET_INVALID", 1);
        }

        [Fact]
        public void EdgeCase1_ReservedCharacter_LogsOneErrorBeforeThrow()
        {
            string tag = StartLog("NamingEdge1");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(() => engine.BuildCVs<VariableC_Amt>(new[] { "E-01" }));

            AssertOneNamingError(ReadLog(tag), "Set #1", "E-01", "contains_reserved_character");
        }

        [Fact]
        public void EdgeCase2_NegativeDimension_LogsOneErrorBeforeThrow()
        {
            string tag = StartLog("NamingEdge2");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(() => engine.BuildCVs<VariableC_Amt>(new[] { -5 }));

            AssertOneNamingError(ReadLog(tag), "Set #1", "-5", "contains_reserved_character");
        }

        [Fact]
        public void EdgeCase4_DateTimeWithTime_LogsOneErrorBeforeThrow()
        {
            string tag = StartLog("NamingEdge4");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(() => engine.BuildCVs<VarDG>(
                new[] { new DateTime(2026, 8, 9, 1, 2, 3, DateTimeKind.Utc) }, new[] { "A" }));

            AssertOneNamingError(ReadLog(tag), "Set #1", "2026-08-09T01:02:03.0000000Z", "datetime_contains_time");
        }

        [Fact]
        public void EdgeCase10_NullDimensions_LogsOneErrorBeforeThrow()
        {
            string tag = StartLog("NamingEdge10");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(() => engine.CreateEqual(new TestConstraint(), null!));

            AssertOneNamingError(ReadLog(tag), nameof(TestConstraint), "<null>", "dimensions_array_is_null");
        }

        [Fact]
        public void EdgeCase11_InvalidExplicitName_LogsOneErrorBeforeThrow()
        {
            string tag = StartLog("NamingEdge11");
            var engine = new MockEngine();
            engine.Build();

            Assert.Throws<ArgumentException>(() => engine.CreateEqual("Demand@2026-08-09"));

            AssertOneNamingError(ReadLog(tag), "CreateEqual token #1", "2026-08-09", "contains_reserved_character");
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
            Assert.Contains("[DATA_LOAD_FAILED]", log);
            Assert.Contains("context=Load", log);
            Assert.Contains("value=System.Object", log);
            Assert.Contains("reason=boundary boom", log);
            Assert.Contains("result=aborted", log);
            AssertErrorCodeCount(log, "DATA_LOAD_FAILED", 1);
        }

        [Fact]
        public void PublicApiBoundary_DoesNotDuplicateAnAlreadyLoggedException()
        {
            string tag = StartLog("BoundaryDeduplication");
            var original = new InvalidOperationException("inner boom");

            var thrown = Assert.Throws<InvalidOperationException>(() =>
                OptData.Load<object>(() => throw Logging.ErrorOnce(
                    original, "INNER_FAILURE", "底層失敗", "InnerOperation", "bad-value", "inner_reason")));

            Assert.Same(original, thrown);
            string log = ReadLog(tag);
            Assert.Contains("[INNER_FAILURE]", log);
            Assert.DoesNotContain("[DATA_LOAD_FAILED]", log);
            AssertErrorCodeCount(log, "INNER_FAILURE", 1);
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
            Assert.Contains("[PARAMETER_NOT_FOUND]", log);
            Assert.Contains("context=Parameter_GncProfit", log);
            Assert.Contains("key=Chair", log);
            Assert.Contains("reason=no_matching_row", log);
            Assert.Contains("result=missing", log);
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
            Assert.Contains("CONFIG_SNAPSHOT_SKIPPED", log);
            Assert.Contains("設定快照略過屬性", log);
            Assert.Contains("BrokenProperty", log);
            Assert.Contains("snapshot getter failed", log);
        }

        [Fact]
        public void OracleConversion_InvalidValue_ThrowsInsteadOfWritingNull()
        {
            string tag = StartLog("OracleConversion");
            MethodInfo method = typeof(OracleDBCtrl).GetMethod(
                "ConvertToDbType",
                BindingFlags.Static | BindingFlags.NonPublic)!;

            var invocation = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(null, new object[] { typeof(int), "not-an-int" }));

            Assert.IsType<FormatException>(invocation.InnerException);
            string log = ReadLog(tag);
            Assert.Contains("ORACLE_CONVERSION_FAILED", log);
            Assert.Contains("Oracle 資料轉型失敗", log);
            Assert.Contains("context=ConvertToDbType", log);
            Assert.Contains("value=not-an-int", log);
            Assert.Contains("reason=unsupported_or_invalid_value", log);
            Assert.Contains("result=aborted", log);
            AssertErrorCodeCount(log, "ORACLE_CONVERSION_FAILED", 1);
        }
    }
}
