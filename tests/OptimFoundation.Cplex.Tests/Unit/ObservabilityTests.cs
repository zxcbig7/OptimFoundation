using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using OptimFoundation.Db.Oracle;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    [Collection("Logging")]
    public class ObservabilityTests
    {
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
            Assert.Contains("[變數建立摘要] 總數=2/2 類別數=1", log);
            Assert.Contains("[目標式建構開始] sense=Minimize terms=1", log);
            Assert.Contains("[目標式建構完成] sense=Minimize terms=1 result=success", log);
            Assert.Contains("[限制式建立完成] group=Demand count=1/2", log);
            Assert.Contains("[限制式建立完成] group=Capacity count=0/1", log);
            Assert.Contains("[限制式建立摘要] 總數=1/3 群組數=2", log);
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
            Assert.Contains("result=write_aborted", log);
        }
    }
}
