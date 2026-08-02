using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            ("workThreads=4", c => c.workThreads = 4),
            ("rowRead=30000", c => c.rowRead = 30000),
            ("workMemory=2048", c => c.workMemory = 2048),
            ("epGap=1e-4", c => c.epGap = 1e-4),
            ("randomSeed=7", c => c.randomSeed = 7),
            ("epOpt=1e-6", c => c.epOpt = 1e-6),
            ("epRHS=1e-6", c => c.epRHS = 1e-6),
            ("timeLimit=30", c => c.timeLimit = 30),
            ("polishAfterTime=1e9", c => c.polishAfterTime = 1e9),
            ("Presolve=on", c => c.PreIndicator = true),
            ("HeuristicEffort=1.0", c => c.HeuristicEffort = 1.0),

            // ── node 選擇策略：0 DFS / 1 best-bound / 2 best-estimate / 3 alt best-estimate ──
            ("nodeSelect=0", c => c.nodeSelect = 0),
            ("nodeSelect=1", c => c.nodeSelect = 1),
            ("nodeSelect=2", c => c.nodeSelect = 2),
            ("nodeSelect=3", c => c.nodeSelect = 3),

            // ── 解析模式 MIPEmphasis：0 平衡 / 1 可行 / 2 最佳 / 3 bound / 4 隱藏解 ──
            ("mipEmphasis=0", c => c.mipEmphasis = 0),
            ("mipEmphasis=1", c => c.mipEmphasis = 1),
            ("mipEmphasis=2", c => c.mipEmphasis = 2),
            ("mipEmphasis=3", c => c.mipEmphasis = 3),
            ("mipEmphasis=4", c => c.mipEmphasis = 4),

            // ── 分支變數選擇 VarSel：-1 min-infeas / 0 自動 / 1 max-infeas / 2 pseudo / 3 strong / 4 pseudo-reduced ──
            ("varSel=-1", c => c.varSel = -1),
            ("varSel=0", c => c.varSel = 0),
            ("varSel=1", c => c.varSel = 1),
            ("varSel=2", c => c.varSel = 2),
            ("varSel=3", c => c.varSel = 3),
            ("varSel=4", c => c.varSel = 4),

            // ── root LP 演算法 RootAlg：0 自動 / 1 primal / 2 dual / 3 network / 4 barrier / 5 sifting / 6 concurrent ──
            ("algorithm=0", c => c.algorithm = 0),
            ("algorithm=1", c => c.algorithm = 1),
            ("algorithm=2", c => c.algorithm = 2),
            ("algorithm=3", c => c.algorithm = 3),
            ("algorithm=4", c => c.algorithm = 4),
            ("algorithm=5", c => c.algorithm = 5),
            ("algorithm=6", c => c.algorithm = 6),

            // ── 節點資訊儲存 NodeFileInd：0 不存 / 1 記憶體壓縮 / 2 磁碟 / 3 磁碟壓縮 ──
            ("nodeFileInd=0", c => c.nodeFileInd = 0),
            ("nodeFileInd=1", c => c.nodeFileInd = 1),
            ("nodeFileInd=2", c => c.nodeFileInd = 2),
            ("nodeFileInd=3", c => c.nodeFileInd = 3),

            // ── 節點 LP 演算法 NodeAlg（SubAlg）：0 自動 / 1 primal / 2 dual / 3 network / 4 barrier / 5 sifting（無 concurrent） ──
            ("NodeAlgorithm=0", c => c.NodeAlgorithm = 0),
            ("NodeAlgorithm=1", c => c.NodeAlgorithm = 1),
            ("NodeAlgorithm=2", c => c.NodeAlgorithm = 2),
            ("NodeAlgorithm=3", c => c.NodeAlgorithm = 3),
            ("NodeAlgorithm=4", c => c.NodeAlgorithm = 4),
            ("NodeAlgorithm=5", c => c.NodeAlgorithm = 5),

            // ── 決定論 / 計時 ──
            // 平行模式 Parallel：-1 機會式 / 0 自動 / 1 決定論
            ("parallelMode=-1", c => c.parallelMode = -1),
            ("parallelMode=0", c => c.parallelMode = 0),
            ("parallelMode=1", c => c.parallelMode = 1),
            ("detTimeLimit=1e7", c => c.detTimeLimit = 1e7),
            // 計時方式 ClockType：0 自動 / 1 CPU / 2 wall-clock
            ("clockType=0", c => c.clockType = 0),
            ("clockType=1", c => c.clockType = 1),
            ("clockType=2", c => c.clockType = 2),
            ("numericalEmphasis=true", c => c.numericalEmphasis = true),

            // ── MIP 容差：連續（單值） ──
            ("epInt=1e-5", c => c.epInt = 1e-5),
            ("epAGap=1e-6", c => c.epAGap = 1e-6),

            // ── MIP limits：連續 / 計數（單值） ──
            ("nodeLimit=1e9", c => c.nodeLimit = 1_000_000_000),
            ("treeMemoryLimit=1024", c => c.treeMemoryLimit = 1024),
            ("intSolLimit=100", c => c.intSolLimit = 100),

            // ── 變數探測 Probe：-1 關 / 0 自動 / 1..3 漸積極 ──
            ("probe=-1", c => c.probe = -1),
            ("probe=0", c => c.probe = 0),
            ("probe=1", c => c.probe = 1),
            ("probe=2", c => c.probe = 2),
            ("probe=3", c => c.probe = 3),
            ("rinsHeur=0", c => c.rinsHeur = 0),
            // B&C 搜尋法 MIPSearch：0 自動 / 1 傳統 / 2 動態
            ("mipSearch=0", c => c.mipSearch = 0),
            ("mipSearch=1", c => c.mipSearch = 1),
            ("mipSearch=2", c => c.mipSearch = 2),
            // 潛降策略 DiveType：0 自動 / 1 傳統 / 2 探測 / 3 引導
            ("diveType=0", c => c.diveType = 0),
            ("diveType=1", c => c.diveType = 1),
            ("diveType=2", c => c.diveType = 2),
            ("diveType=3", c => c.diveType = 3),
            // 分支方向 BrDir：-1 向下 / 0 自動 / 1 向上
            ("branchDir=-1", c => c.branchDir = -1),
            ("branchDir=0", c => c.branchDir = 0),
            ("branchDir=1", c => c.branchDir = 1),

            // ── Cuts：CutsFactor / CutPasses 為計數（單值）；各 cut 族列舉全部合法值 ──
            ("cutsFactor=4.0", c => c.cutsFactor = 4.0),
            ("cutPasses=0", c => c.cutPasses = 0),
            // Gomory（FracCuts）：-1 關 / 0 自動 / 1 中度 / 2 積極（無 3）
            ("gomoryCuts=-1", c => c.gomoryCuts = -1),
            ("gomoryCuts=0", c => c.gomoryCuts = 0),
            ("gomoryCuts=1", c => c.gomoryCuts = 1),
            ("gomoryCuts=2", c => c.gomoryCuts = 2),
            // Covers：-1 關 / 0 自動 / 1..3 漸積極
            ("coverCuts=-1", c => c.coverCuts = -1),
            ("coverCuts=0", c => c.coverCuts = 0),
            ("coverCuts=1", c => c.coverCuts = 1),
            ("coverCuts=2", c => c.coverCuts = 2),
            ("coverCuts=3", c => c.coverCuts = 3),
            // Cliques：-1 關 / 0 自動 / 1..3 漸積極
            ("cliqueCuts=-1", c => c.cliqueCuts = -1),
            ("cliqueCuts=0", c => c.cliqueCuts = 0),
            ("cliqueCuts=1", c => c.cliqueCuts = 1),
            ("cliqueCuts=2", c => c.cliqueCuts = 2),
            ("cliqueCuts=3", c => c.cliqueCuts = 3),
            // MIR（MIRCut）：-1 關 / 0 自動 / 1 中度 / 2 積極（無 3）
            ("mirCuts=-1", c => c.mirCuts = -1),
            ("mirCuts=0", c => c.mirCuts = 0),
            ("mirCuts=1", c => c.mirCuts = 1),
            ("mirCuts=2", c => c.mirCuts = 2),
            // FlowCovers：-1 關 / 0 自動 / 1 中度 / 2 積極（無 3）
            ("flowCoverCuts=-1", c => c.flowCoverCuts = -1),
            ("flowCoverCuts=0", c => c.flowCoverCuts = 0),
            ("flowCoverCuts=1", c => c.flowCoverCuts = 1),
            ("flowCoverCuts=2", c => c.flowCoverCuts = 2),

            // ── 純 LP（Simplex / Barrier） ──
            ("simplexIterLimit=100000", c => c.simplexIterLimit = 100000),
            // Barrier 演算法 BarAlg：0 預設 / 1 / 2 / 3
            ("barrierAlgorithm=0", c => c.barrierAlgorithm = 0),
            ("barrierAlgorithm=1", c => c.barrierAlgorithm = 1),
            ("barrierAlgorithm=2", c => c.barrierAlgorithm = 2),
            ("barrierAlgorithm=3", c => c.barrierAlgorithm = 3),
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

            // 每個參數都實際求解成功
            Assert.True(failures.Count == 0,
                "以下參數求解未達終止狀態：" + Environment.NewLine + string.Join(Environment.NewLine, failures));

            // 反映在 CSV：每個 label 都應出現在輸出的 CSV
            string csvPath = FolderDir.Experiment.GetFilePath(expName + ".csv");
            Assert.True(File.Exists(csvPath), $"CSV 未產出：{csvPath}");
            string csv = File.ReadAllText(csvPath);
            foreach (var (label, _) in Knobs)
                Assert.Contains(label, csv);
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
