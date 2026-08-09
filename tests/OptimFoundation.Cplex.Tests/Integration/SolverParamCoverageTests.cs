using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OptimFoundation.Core;
using OptimFoundation.Cplex.Tests.Mocks;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Integration
{
    /// <summary>
    /// Solver 參數全覆蓋：逐一設定 Configuration() 接線的每個 CplexConfig 旋鈕，
    /// 用 Trial.Capture 真跑一次 CPLEX 求解（CPLEX 拒絕該參數會丟例外 → 測試失敗），
    /// 再 Save 成 Experiment CSV/JSON。每個參數一列，Label = "參數=值"。
    /// 目的：證明遷移後每個 SetParam 路徑 runtime 可用，且反映在實驗 CSV 上。
    /// </summary>
    public class SolverParamCoverageTests
    {
        private static readonly bool CplexAvailable =
            File.Exists(@"C:\IBM\ILOG\CPLEX_Studio2211\cplex\bin\x64_win64\ILOG.CPLEX.dll");

        // (label, 套用單一旋鈕)；涵蓋 OptEngine.Configuration() 內每個 SetParam 路徑。
        //
        // 演算法 / 搜尋策略選擇類（離散列舉旋鈕）→ 列舉「全部合法整數值」，每個值一列，
        // 證明 CPLEX 對該參數的每個選項都接受並能終止求解。值域取自 CPLEX 22.1.1：
        // 範圍弄錯（如對 fraccuts/mircuts/flowcovers 給 3，或對 NodeAlg 給 concurrent=6）
        // CPLEX 會在 SetParam 當下丟例外 → 本測試即失敗，故下列值域為實測安全範圍。
        //
        // 連續 / 純量類（容差、時限、記憶體、執行緒、種子、倍數、計數）→ 維持單一代表值，不展開。
        private static IReadOnlyList<(string Label, Action<CplexConfig> Apply)> Knobs => new (string, Action<CplexConfig>)[]
        {
            // ── 核心：連續 / 純量（單值） ──
            ("Threads=4", c => c.Threads = 4),
            ("RowRead=30000", c => c.RowRead = 30000),
            ("MemoryLimitMb=2048", c => c.MemoryLimitMb = 2048),
            ("MipGap=1e-4", c => c.MipGap = 1e-4),
            ("Seed=7", c => c.Seed = 7),
            ("OptimalityTol=1e-6", c => c.OptimalityTol = 1e-6),
            ("FeasibilityTol=1e-6", c => c.FeasibilityTol = 1e-6),
            ("TimeLimit=30", c => c.TimeLimit = 30),
            ("PolishAfterTime=1e9", c => c.PolishAfterTime = 1e9),
            ("Presolve=on", c => c.PreIndicator = true),
            ("HeuristicEffort=1.0", c => c.HeuristicEffort = 1.0),

            ("Symmetry=-1", c => c.Symmetry = -1),
            ("Symmetry=1", c => c.Symmetry = 1),
            ("Symmetry=2", c => c.Symmetry = 2),
            ("Symmetry=3", c => c.Symmetry = 3),

            // ── node 選擇策略：0 DFS / 1 best-bound / 2 best-estimate / 3 alt best-estimate ──
            ("NodeSelect=0", c => c.NodeSelect = 0),
            ("NodeSelect=1", c => c.NodeSelect = 1),
            ("NodeSelect=2", c => c.NodeSelect = 2),
            ("NodeSelect=3", c => c.NodeSelect = 3),

            // ── 解析模式 MIPEmphasis：0 平衡 / 1 可行 / 2 最佳 / 3 bound / 4 隱藏解 ──
            ("Emphasis=0", c => c.Emphasis = 0),
            ("Emphasis=1", c => c.Emphasis = 1),
            ("Emphasis=2", c => c.Emphasis = 2),
            ("Emphasis=3", c => c.Emphasis = 3),
            ("Emphasis=4", c => c.Emphasis = 4),

            // ── 分支變數選擇 VarSel：-1 min-infeas / 0 自動 / 1 max-infeas / 2 pseudo / 3 strong / 4 pseudo-reduced ──
            ("VariableSelect=-1", c => c.VariableSelect = -1),
            ("VariableSelect=0", c => c.VariableSelect = 0),
            ("VariableSelect=1", c => c.VariableSelect = 1),
            ("VariableSelect=2", c => c.VariableSelect = 2),
            ("VariableSelect=3", c => c.VariableSelect = 3),
            ("VariableSelect=4", c => c.VariableSelect = 4),

            // ── root LP 演算法 RootAlg：0 自動 / 1 primal / 2 dual / 3 network / 4 barrier / 5 sifting / 6 concurrent ──
            ("RootAlgorithm=0", c => c.RootAlgorithm = 0),
            ("RootAlgorithm=1", c => c.RootAlgorithm = 1),
            ("RootAlgorithm=2", c => c.RootAlgorithm = 2),
            ("RootAlgorithm=3", c => c.RootAlgorithm = 3),
            ("RootAlgorithm=4", c => c.RootAlgorithm = 4),
            ("RootAlgorithm=5", c => c.RootAlgorithm = 5),
            ("RootAlgorithm=6", c => c.RootAlgorithm = 6),

            // ── 節點資訊儲存 NodeFileInd：0 不存 / 1 記憶體壓縮 / 2 磁碟 / 3 磁碟壓縮 ──
            ("NodeFileStrategy=0", c => c.NodeFileStrategy = 0),
            ("NodeFileStrategy=1", c => c.NodeFileStrategy = 1),
            ("NodeFileStrategy=2", c => c.NodeFileStrategy = 2),
            ("NodeFileStrategy=3", c => c.NodeFileStrategy = 3),

            // ── 節點 LP 演算法 NodeAlg（SubAlg）：0 自動 / 1 primal / 2 dual / 3 network / 4 barrier / 5 sifting（無 concurrent） ──
            ("NodeAlgorithm=0", c => c.NodeAlgorithm = 0),
            ("NodeAlgorithm=1", c => c.NodeAlgorithm = 1),
            ("NodeAlgorithm=2", c => c.NodeAlgorithm = 2),
            ("NodeAlgorithm=3", c => c.NodeAlgorithm = 3),
            ("NodeAlgorithm=4", c => c.NodeAlgorithm = 4),
            ("NodeAlgorithm=5", c => c.NodeAlgorithm = 5),

            // ── 決定論 / 計時 ──
            // 平行模式 Parallel：-1 機會式 / 0 自動 / 1 決定論
            ("ParallelMode=-1", c => c.ParallelMode = -1),
            ("ParallelMode=0", c => c.ParallelMode = 0),
            ("ParallelMode=1", c => c.ParallelMode = 1),
            ("DeterministicTimeLimit=1e7", c => c.DeterministicTimeLimit = 1e7),
            // 計時方式 ClockType：0 自動 / 1 CPU / 2 wall-clock
            ("ClockType=0", c => c.ClockType = 0),
            ("ClockType=1", c => c.ClockType = 1),
            ("ClockType=2", c => c.ClockType = 2),
            ("NumericalEmphasis=true", c => c.NumericalEmphasis = true),

            // ── MIP 容差：連續（單值） ──
            ("IntegralityTolerance=1e-5", c => c.IntegralityTolerance = 1e-5),
            ("AbsoluteMipGap=1e-6", c => c.AbsoluteMipGap = 1e-6),

            // ── MIP limits：連續 / 計數（單值） ──
            ("NodeLimit=1e9", c => c.NodeLimit = 1_000_000_000),
            ("TreeMemoryLimitMb=1024", c => c.TreeMemoryLimitMb = 1024),
            ("IntegerSolutionLimit=100", c => c.IntegerSolutionLimit = 100),

            // ── 變數探測 Probe：-1 關 / 0 自動 / 1..3 漸積極 ──
            ("Probe=-1", c => c.Probe = -1),
            ("Probe=0", c => c.Probe = 0),
            ("Probe=1", c => c.Probe = 1),
            ("Probe=2", c => c.Probe = 2),
            ("Probe=3", c => c.Probe = 3),
            ("RinsHeuristicFrequency=0", c => c.RinsHeuristicFrequency = 0),
            // B&C 搜尋法 MIPSearch：0 自動 / 1 傳統 / 2 動態
            ("MipSearch=0", c => c.MipSearch = 0),
            ("MipSearch=1", c => c.MipSearch = 1),
            ("MipSearch=2", c => c.MipSearch = 2),
            // 潛降策略 DiveType：0 自動 / 1 傳統 / 2 探測 / 3 引導
            ("DiveType=0", c => c.DiveType = 0),
            ("DiveType=1", c => c.DiveType = 1),
            ("DiveType=2", c => c.DiveType = 2),
            ("DiveType=3", c => c.DiveType = 3),
            // 分支方向 BrDir：-1 向下 / 0 自動 / 1 向上
            ("BranchDirection=-1", c => c.BranchDirection = -1),
            ("BranchDirection=0", c => c.BranchDirection = 0),
            ("BranchDirection=1", c => c.BranchDirection = 1),

            // ── Cuts：CutsFactor / CutPasses 為計數（單值）；各 cut 族列舉全部合法值 ──
            ("CutsFactor=4.0", c => c.CutsFactor = 4.0),
            ("CutPasses=0", c => c.CutPasses = 0),
            // Gomory（FracCuts）：-1 關 / 0 自動 / 1 中度 / 2 積極（無 3）
            ("GomoryCuts=-1", c => c.GomoryCuts = -1),
            ("GomoryCuts=0", c => c.GomoryCuts = 0),
            ("GomoryCuts=1", c => c.GomoryCuts = 1),
            ("GomoryCuts=2", c => c.GomoryCuts = 2),
            // Covers：-1 關 / 0 自動 / 1..3 漸積極
            ("CoverCuts=-1", c => c.CoverCuts = -1),
            ("CoverCuts=0", c => c.CoverCuts = 0),
            ("CoverCuts=1", c => c.CoverCuts = 1),
            ("CoverCuts=2", c => c.CoverCuts = 2),
            ("CoverCuts=3", c => c.CoverCuts = 3),
            // Cliques：-1 關 / 0 自動 / 1..3 漸積極
            ("CliqueCuts=-1", c => c.CliqueCuts = -1),
            ("CliqueCuts=0", c => c.CliqueCuts = 0),
            ("CliqueCuts=1", c => c.CliqueCuts = 1),
            ("CliqueCuts=2", c => c.CliqueCuts = 2),
            ("CliqueCuts=3", c => c.CliqueCuts = 3),
            // MIR（MIRCut）：-1 關 / 0 自動 / 1 中度 / 2 積極（無 3）
            ("MirCuts=-1", c => c.MirCuts = -1),
            ("MirCuts=0", c => c.MirCuts = 0),
            ("MirCuts=1", c => c.MirCuts = 1),
            ("MirCuts=2", c => c.MirCuts = 2),
            // FlowCovers：-1 關 / 0 自動 / 1 中度 / 2 積極（無 3）
            ("FlowCoverCuts=-1", c => c.FlowCoverCuts = -1),
            ("FlowCoverCuts=0", c => c.FlowCoverCuts = 0),
            ("FlowCoverCuts=1", c => c.FlowCoverCuts = 1),
            ("FlowCoverCuts=2", c => c.FlowCoverCuts = 2),

            // ── 純 LP（Simplex / Barrier） ──
            ("SimplexIterationLimit=100000", c => c.SimplexIterationLimit = 100000),
            // Barrier 演算法 BarAlg：0 預設 / 1 / 2 / 3
            ("BarrierAlgorithm=0", c => c.BarrierAlgorithm = 0),
            ("BarrierAlgorithm=1", c => c.BarrierAlgorithm = 1),
            ("BarrierAlgorithm=2", c => c.BarrierAlgorithm = 2),
            ("BarrierAlgorithm=3", c => c.BarrierAlgorithm = 3),
        };

        [Fact(DisplayName = "每個 solver 參數逐一套用 → 真求解 → 反映到 Experiment CSV")]
        public void EverySolverParam_AppliedAndSolves_ReflectedInCsv()
        {
            if (!CplexAvailable) return;

            const string expName = "solver-param-coverage";

            // Experiment.Save 對同名實驗是 append 語意，先清前次殘留才能 assert 精確筆數
            foreach (var ext in new[] { ".json", ".csv" })
            {
                string stale = FolderDir.Experiment.GetFilePath(expName + ext);
                if (File.Exists(stale)) File.Delete(stale);
            }

            var exp = new Experiment(expName, "逐一套用每個 CplexConfig solver 旋鈕並記錄一次求解");

            var failures = new List<string>();
            var projectConfig = new ProjectConfig { EnableSolverLog = false };

            foreach (var (label, apply) in Knobs)
            {
                var config = new CplexConfig();
                apply(config);

                using var engine = new OptEngine(config, projectConfig);
                engine.Build();          // Build → Configuration(config)：在此套用該旋鈕的 SetParam
                BuildKnapsack(engine);   // 小型 MILP，讓 MIP 類參數真正生效

                // Capture 內部呼叫 Solve()；CPLEX 若拒絕該參數會在此丟例外 → 測試失敗
                var trial = Trial.Capture(engine, label, () => engine.Solve(), note: label);
                exp.AddTrial(trial);

                // 此 knapsack 在 root 即最佳；任何非終止狀態代表該參數破壞求解
                if (trial.Metrics.Status != SolveStatus.Optimal &&
                    trial.Metrics.Status != SolveStatus.Feasible)
                {
                    failures.Add($"{label} → {trial.Metrics.Status}");
                }
            }

            exp.Save();

            // 全部旋鈕都記成一個 Trial
            Assert.Equal(Knobs.Count, exp.Trials.Count);

            var expectedSymmetry = new Dictionary<string, int>
            {
                ["Symmetry=-1"] = -1,
                ["Symmetry=1"] = 1,
                ["Symmetry=2"] = 2,
                ["Symmetry=3"] = 3,
            };
            foreach (var (label, value) in expectedSymmetry)
            {
                var trial = Assert.Single(exp.Trials, t => t.Label == label);
                Assert.True(trial.Metrics.Status is SolveStatus.Optimal or SolveStatus.Feasible);
                Assert.Equal(value, Assert.IsType<int>(trial.Config.SolverSpecific["Symmetry"]));
            }

            // 每個參數都實際求解成功
            Assert.True(failures.Count == 0,
                "以下參數求解未達終止狀態：" + Environment.NewLine + string.Join(Environment.NewLine, failures));

            // 反映在 CSV：每個 label 都應出現在輸出的 CSV
            string csvPath = FolderDir.Experiment.GetFilePath(expName + ".csv");
            Assert.True(File.Exists(csvPath), $"CSV 未產出：{csvPath}");
            string csv = File.ReadAllText(csvPath);
            foreach (var (label, _) in Knobs)
                Assert.Contains(label, csv);

            var saved = Experiment.Load(expName);
            foreach (var (label, value) in expectedSymmetry)
            {
                var trial = Assert.Single(saved.Trials, t => t.Label == label);
                var recordedValue = Assert.IsType<JsonElement>(trial.Config.SolverSpecific["Symmetry"]);
                Assert.Equal(value, recordedValue.GetInt32());
            }
        }

        /// <summary>3 物品 0/1 背包：max 3a+4b+5c s.t. 2a+3b+4c &lt;= 5。root 即最佳。</summary>
        private static void BuildKnapsack(OptEngine engine)
        {
            var items = new List<string> { "a", "b", "c" };
            engine.BuildBVs<VarS>(items);

            var weight = new Dictionary<string, double> { ["a"] = 2, ["b"] = 3, ["c"] = 4 };
            var value = new Dictionary<string, double> { ["a"] = 3, ["b"] = 4, ["c"] = 5 };

            foreach (var i in items) engine.AddLHS(weight[i], new VarS { S = i });
            engine.CreateLessEqual(5.0, "cap");

            foreach (var i in items) engine.AddLHS(value[i], new VarS { S = i });
            engine.CreateMaximize();
        }
    }
}
