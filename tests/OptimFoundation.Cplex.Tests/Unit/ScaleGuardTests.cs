using System;
using System.IO;
using System.Linq;
using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // 模型大小檢查（EngineBase.PreSolveGuard）：已登記變數數量超過 ScaleWarnThreshold 時，只記錄警告，仍繼續求解。
    // Logging 直接將訊息寫入 Console 與檔案，所以測試讀回 log 檔，確認是否真的寫入警告。
    // Logging.SetLogFileName(tag) 把之後所有寫入導向一個帶 tag 的新檔，Solve() 後讀該檔內容比對是否含 WARN。
    // [Collection("Logging")] 讓使用同一 collection 的測試依序執行，
    // 避免其他共用 Logging 的測試改掉 log 檔名或混入訊息；不必停用所有測試的平行執行。
    [Collection("Logging")]
    public class ScaleGuardTests
    {
        private class SmallThresholdConfig : ISolverConfig
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
            public int ScaleWarnThreshold => 1;
        }

        private static string ReadLatestLogContent(string tag)
        {
            string dir = FolderDir.Log.GetPath();
            string? file = Directory.GetFiles(dir, $"{tag}_*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            Assert.True(file != null, $"找不到 log 檔（tag={tag}），Logging 應在寫入時建立此檔");
            // Logging 仍持有寫入 handle（FileShare.Read），讀取時須使用 FileShare.ReadWrite。
            using var fs = new FileStream(file!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }

        [Fact]
        public void PreSolveGuard_OverThreshold_LogsWarning_AndStillSolves()
        {
            string tag = "ScaleGuardOver_" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);

            var engine = new MockEngine(new SmallThresholdConfig());
            engine.Build();
            engine.BuildBVs<VarS>(new List<string> { "A", "B" }); // VariableCount = 2 > 門檻(1)

            bool ok = engine.Solve();

            Assert.True(ok); // scale guard 只警告，不阻擋求解

            string logContent = ReadLatestLogContent(tag);
            Assert.Contains("WARN", logContent);
            Assert.Contains("MODEL_SCALE_WARNING", logContent);
            Assert.Contains("count=2", logContent);
            Assert.Contains("threshold=1", logContent);
            Assert.Contains("result=continued", logContent);
        }

        [Fact]
        public void PreSolveGuard_UnderThreshold_NoWarning()
        {
            string tag = "ScaleGuardUnder_" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);
            Logging.Info("[Test] marker start"); // 先建立 log 檔，再檢查是否含 WARN。

            // 預設 MockConfig 門檻用 interface default(10,000,000)，遠大於這裡建的變數數，不應觸發警告
            var engine = new MockEngine();
            engine.Build();
            engine.BuildBVs<VarS>(new List<string> { "A" });
            // 變數要被引用才會進 solver 模型；沒引用的話會 WARN（UNREFERENCED_VARIABLES），與本測試的 scale guard 無關
            engine.AddLHS(1.0, new VarS { S = "A" });
            engine.CreateLessEqual("CapA");

            bool ok = engine.Solve();

            Assert.True(ok);

            string logContent = ReadLatestLogContent(tag);
            Assert.Contains("marker start", logContent); // 確認讀到的正是這次執行寫的檔
            Assert.DoesNotContain("WARN", logContent);
        }

        [Fact]
        public void PreSolveGuard_NullConfig_DoesNotThrow()
        {
            // 以 null 建立 MockEngine，確認缺少 Config 時會略過大小檢查，不丟出例外。
            var engine = new MockEngine(null!);
            engine.Build();
            engine.BuildBVs<VarS>(new List<string> { "A" });

            bool ok = engine.Solve();

            Assert.True(ok); // Config 為 null 時仍能完成求解
        }
    }
}
