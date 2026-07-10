using System.Diagnostics;
using ILOG.Concert;
using OptimFoundation.Cplex;
using OptimFoundation.Core;
using ThreadTest.Benders;
using ThreadTest.Data;
using ThreadTest.VariableClass;

namespace ThreadTest
{
    /// <summary>
    /// Benders Decomposition for Facility Location + Transportation MILP.
    ///
    /// 問題結構：
    ///   Y[s] ∈ {0,1}  ─ 是否開設貨源 s（固定成本 fc[s]）
    ///   X[s,d] ≥ 0    ─ 從 s 運往 d 的數量（變動成本 vc[s,d]）
    ///
    /// Benders 分解：
    ///   主問題（MILP）：決定 Y + 子問題成本近似 θ
    ///   子問題（LP）  ：給定 Y*，求最低運輸成本 → 回傳 dual 值生成切割
    ///
    /// 使用到的新函式：
    ///   SubProblemEngine.RebuildWithY() → ResetConstraint()  每輪重設子問題
    ///   OptEngine.CopyModel()                                備份子問題快照
    ///   OptEngine.MergeModel()                               合併子問題限制式
    ///   OptEngine.CreateGreatEqualThread()                   示範跨引擎 API
    /// </summary>
    public class BendersDecomposition : IDisposable
    {
        private MasterProblemEngine _master;
        private SubProblemEngine _sub;
        private readonly BendersDataload _data;

        public Stopwatch totalTimer = new Stopwatch();

        private string _projectName => GetType().Name;

        public BendersDecomposition()
        {
            _data = new BendersDataload();
            Logging.SetLogFileName(_projectName);
        }

        private CplexConfig GetConfig() => new CplexConfig
        {
            workThreads = 4,
            enableLog = false,
            exportLP = false
        };

        public bool Execute()
        {
            totalTimer.Restart();
            Logging.Info($"[{_projectName}] 開始 Benders Decomposition");
            PrintProblemData();

            // ─────────────────────────────────────────────────────────
            // 初始化主問題
            // ─────────────────────────────────────────────────────────
            _master = new MasterProblemEngine(GetConfig());
            _master.Build();
            _master.SetModelName("BendersMaster");
            _master.InitializeVariables(_data);
            _master.BuildInitialModel();

            // ─────────────────────────────────────────────────────────
            // 初始化子問題（變數只建一次，限制式每輪用 ResetConstraint 更新）
            // ─────────────────────────────────────────────────────────
            _sub = new SubProblemEngine(GetConfig());
            _sub.Build();
            _sub.SetModelName("BendersSub");
            _sub.InitializeVariables(_data);

            // 以 Y* = 全部開設 建立第一輪子問題
            var initY = _data.Sources.ToDictionary(s => s, _ => 1.0);
            _sub.RebuildWithY(initY);

            // CopyModel：備份初始子問題狀態（示範 API）
            OptEngine subSnapshot = _sub.CopyModel(_sub);
            Logging.Info("[Init] CopyModel 備份初始子問題完成");
            subSnapshot?.Dispose();

            // ─────────────────────────────────────────────────────────
            // Benders 主迴圈
            // ─────────────────────────────────────────────────────────
            double UB = double.MaxValue;
            double LB = double.MinValue;
            double epsilon = 1e-4;
            int maxIter = 20;
            bool optimal = false;

            for (int iter = 1; iter <= maxIter; iter++)
            {
                Logging.Info($"\n══ Benders Iteration {iter} ══");

                // ── Step 1：求解主問題 ──────────────────────────────
                bool masterOk = _master.Solve();
                if (!masterOk)
                {
                    Logging.Info("[Master] 無解，終止");
                    break;
                }

                LB = _master.GetObjectiveValue();
                var yValues = _master.GetYValues();
                double thetaVal = _master.GetThetaValue();
                Logging.Info($"  [Master] LB={LB:F4}  θ={thetaVal:F4}");
                Logging.Info($"  [Master] Y: {string.Join(", ", yValues.Select(kv => $"{kv.Key}={Math.Round(kv.Value)}  "))}");

                // ── Step 2：子問題以 Y* 重建（ResetConstraint 核心用途）──
                _sub.RebuildWithY(yValues);
                bool subOk = _sub.Solve();

                if (!subOk)
                {
                    Logging.Info("  [Sub] INFEASIBLE（此範例設計上不會發生）");
                    break;
                }

                double subObj = _sub.GetObjectiveValue();
                double fixCost = _data.Sources.Sum(s => _data.FixedCost[s] * yValues[s]);
                double incumbentCost = fixCost + subObj;
                if (incumbentCost < UB) UB = incumbentCost;

                Logging.Info($"  [Sub]    SubObj={subObj:F4}  FixedCost={fixCost:F4}  UB={UB:F4}");
                Logging.Info($"  [Gap]    UB-LB={UB - LB:F6}");

                // ── Step 3：收斂判斷 ──────────────────────────────────
                if (UB - LB <= epsilon)
                {
                    Logging.Info($"  ✅ 收斂！Iteration={iter}  Gap={UB - LB:F8}");
                    optimal = true;
                    break;
                }

                // ── Step 4：取 dual 值 ───────────────────────────────
                var supplyDuals = _data.Sources.ToDictionary(s => s, s => _sub.GetSupplyDual(s));
                var demandDuals = _data.Dests.ToDictionary(d => d, d => _sub.GetDemandDual(d));

                Logging.Info($"  [Duals] Supply: {string.Join("  ", supplyDuals.Select(kv => $"v[{kv.Key}]={kv.Value:F4}"))}");
                Logging.Info($"  [Duals] Demand: {string.Join("  ", demandDuals.Select(kv => $"u[{kv.Key}]={kv.Value:F4}"))}");

                // ── Step 5：加入 Benders Optimality Cut ─────────────
                // θ + Σ supply[s]*(-v[s])*Y[s]  ≥  Σ demand[d]*u[d]
                _master.AddOptimalityCut(supplyDuals, demandDuals, iter);
                Logging.Info($"  [Cut]   BendersCut_{iter} 加入主問題");

                // ── 示範 MergeModel：每輪將子問題限制式合併回主問題備份 ──
                // （實際 Benders 不需此步驟，此處純示範 API）
                OptEngine mergeTarget = new OptEngine(GetConfig());
                mergeTarget.Build();
                _sub.MergeModel(_sub, mergeTarget);
                Logging.Info($"  [API]   MergeModel 示範完成");
                mergeTarget.Dispose();

                // ── 示範 CreateGreatEqualThread（自身引擎，同組變數）──
                // 將子問題的需求 D1 下界（≥ demand[D1]）以 Thread API 記錄
                // 注意：同組變數第 2 次呼叫被 dedup 攔截，屬預期行為
                foreach (var d in _data.Dests)
                {
                    foreach (var s in _data.Sources)
                        _sub.AddLHS(1, new VariableX_Supply { Source = s, Dest = d });
                    bool threadOk = _sub.CreateGreatEqualThread(
                        _data.Demand[d], $"ThreadDemand@{d}", _sub, _sub);
                    Logging.Info($"  [API]   CreateGreatEqualThread Demand@{d}: {threadOk}");
                }
            }

            // ─────────────────────────────────────────────────────────
            // 最終結果
            // ─────────────────────────────────────────────────────────
            if (optimal)
            {
                Logging.Info("\n══ 最終求解結果 ══");
                var yFinal = _master.GetYValues();
                Logging.Info($"  開設貨源：{string.Join(", ", yFinal.Where(kv => Math.Round(kv.Value) > 0.5).Select(kv => kv.Key))}");
                Logging.Info($"  固定成本：{_data.Sources.Sum(s => _data.FixedCost[s] * yFinal[s]):F4}");

                // 用最終 Y 求解子問題取得流量
                _sub.RebuildWithY(yFinal);
                _sub.Solve();
                Logging.Info($"  運輸成本：{_sub.GetObjectiveValue():F4}");
                Logging.Info($"  總成本  ：{UB:F4}  （預期 26）");

                PrintFlowSolution(yFinal);
            }

            totalTimer.Stop();
            Logging.Info($"\n[完成] 總時間：{totalTimer.Elapsed}");
            return optimal;
        }

        private void PrintProblemData()
        {
            Logging.Info("── 問題資料 ──");
            Logging.Info($"  貨源：{string.Join(", ", _data.Sources.Select(s => $"{s}(supply={_data.Supply[s]}, fc={_data.FixedCost[s]})"))}");
            Logging.Info($"  目的：{string.Join(", ", _data.Dests.Select(d => $"{d}(demand={_data.Demand[d]})"))}");
            Logging.Info($"  運輸成本：{string.Join(", ", _data.parameter_Cost.Select(p => $"{p.Source}->{p.Dest}={p.QTY}"))}");
        }

        private void PrintFlowSolution(Dictionary<string, double> yFinal)
        {
            Logging.Info("── 流量解 ──");
            foreach (var s in _data.Sources)
                foreach (var d in _data.Dests)
                {
                    double flow = _sub.GetVariableValue(
                        new VariableX_Supply { Source = s, Dest = d }.ToString());
                    if (flow > 1e-6)
                        Logging.Info($"  X[{s},{d}] = {flow:F4}");
                }
        }

        public void Dispose()
        {
            _master?.Dispose();
            _sub?.Dispose();
        }
    }
}
