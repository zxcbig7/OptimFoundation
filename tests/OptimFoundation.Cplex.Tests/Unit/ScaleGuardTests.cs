using System;
using System.IO;
using System.Linq;
using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // Scale guard（EngineBase.PreSolveGuard）：TotalVarCount 超過 ISolverConfig.ScaleWarnThreshold → Logging.Warn，不 throw、不中止。
    // Logging 是 static class，直接寫 console/log 檔，無法用 mock 攔截呼叫；改用「讀回 log 檔內容」證明警告確實觸發——
    // Logging.SetLogFileName(tag) 把之後所有寫入導向一個帶 tag 的新檔，Solve() 後讀該檔內容比對是否含 WARN。
    // [Collection("Logging")]：本類別內測試序列執行（xUnit 預設同 class 內即序列，此為局部保險）；
    // 全 repo 只有本類別碰 Logging 靜態單例，故不需要全域 [assembly: CollectionBehavior(DisableTestParallelization)]。
    [Collection("Logging")]
    public class ScaleGuardTests
    {
        private class SmallThresholdConfig : ISolverConfig
        {
            public double? TimeLimit { get; set; }
            public double? MipGap { get; set; }
            public int? Threads { get; set; }
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
            // Logging 的 FileWriter 仍持有寫入 handle（FileShare.Read）：讀端須明確開 FileShare.ReadWrite 才不會撞共用衝突
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
            engine.BuildBVs<VarS>(new List<string> { "A", "B" }); // TotalVarCount = 2 > 門檻(1)

            bool ok = engine.Solve();

            Assert.True(ok); // scale guard 只警告，不阻擋求解

            string logContent = ReadLatestLogContent(tag);
            Assert.Contains("WARN", logContent);
            Assert.Contains("TotalVarCount=2", logContent);
            Assert.Contains("ScaleWarnThreshold=1", logContent);
        }

        [Fact]
        public void PreSolveGuard_UnderThreshold_NoWarning()
        {
            string tag = "ScaleGuardUnder_" + Guid.NewGuid().ToString("N");
            Logging.SetLogFileName(tag);
            Logging.Info("[Test] marker start"); // 確保 log 檔一定被建立，之後才能斷言「內容不含 WARN」

            // 預設 MockConfig 門檻用 interface default(10,000,000)，遠大於這裡建的變數數，不應觸發警告
            var engine = new MockEngine();
            engine.Build();
            engine.BuildBVs<VarS>(new List<string> { "A" });

            bool ok = engine.Solve();

            Assert.True(ok);

            string logContent = ReadLatestLogContent(tag);
            Assert.Contains("marker start", logContent); // 確認讀到的正是這次執行寫的檔
            Assert.DoesNotContain("WARN", logContent);
        }

        [Fact]
        public void PreSolveGuard_NullConfig_DoesNotThrow()
        {
            // Config 為 null 時防禦性跳過：MockEngine(ISolverConfig) 建構子直接傳 null 模擬
            var engine = new MockEngine(null!);
            engine.Build();
            engine.BuildBVs<VarS>(new List<string> { "A" });

            bool ok = engine.Solve();

            Assert.True(ok); // 沒有因 Config==null 而炸
        }
    }
}
