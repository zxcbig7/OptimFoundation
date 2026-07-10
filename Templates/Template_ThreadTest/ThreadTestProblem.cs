using System.Diagnostics;
using ILOG.Concert;
using OptimFoundation.Cplex;
using OptimFoundation.Core;
using ThreadTest.Constraints;
using ThreadTest.Data;
using ThreadTest.VariableClass;
using ThreadTest.VariablesClass;

namespace ThreadTest
{
    public class ThreadTestProblem : IDisposable
    {
        public OptEngine optEngine;
        public Dataload dataload;

        public Stopwatch buildModelTimer = new Stopwatch();
        public Stopwatch totalTimer = new Stopwatch();
        public TimeSpan totalTimeSpan = new TimeSpan();

        private bool _isSuccess;
        private string _projectName => GetType().Name;

        public ThreadTestProblem()
        {
            dataload = new Dataload();
            _isSuccess = false;
            Logging.SetLogFileName(_projectName);
        }

        private CplexConfig GetConfig() => new CplexConfig
        {
            epGap = 1e-4,
            timeLimit = 60,
            workThreads = 4,
            enableLog = true,
            exportLP = false
        };

        public bool Execute()
        {
            totalTimer.Restart();

            // ══════════════════════════════════════════════════
            // Phase 1：標準 LP 求解（Transportation Problem）
            // Sources: A(supply=10), B(supply=8)
            // Dests:   D1(demand=7), D2(demand=11)
            // Cost:    A-D1=2, A-D2=3, B-D1=1, B-D2=4
            // 最佳解: A→D2=10, B→D1=7, B→D2=1  ObjVal = 3*10+1*7+4*1 = 41
            // ══════════════════════════════════════════════════
            Logging.Info("══ Phase 1：標準 LP 求解 ══");

            optEngine = new OptEngine(GetConfig());
            optEngine.Build();
            optEngine.SetModelName(_projectName);

            buildModelTimer.Restart();
            new VariableCreate(dataload, optEngine).Build();
            new BuildModel(dataload, optEngine).Build();
            buildModelTimer.Stop();
            Logging.Info("【建構完成】", buildModelTimer);

            _isSuccess = optEngine.Solve();
            Logging.Info($"[Phase 1] Status={(_isSuccess ? "Optimal" : "Failed")}  ObjVal={(_isSuccess ? optEngine.GetObjectiveValue() : 0)}");

            // ══════════════════════════════════════════════════
            // Phase 2：ResetConstraint → 修改需求 → 重建 → 再求解
            // 驗證 ResetConstraint() 清空目標式與限制式但保留變數
            // ══════════════════════════════════════════════════
            Logging.Info("══ Phase 2：ResetConstraint ══");

            optEngine.ResetConstraint();                // 清空目標式與限制式
            dataload.Demand["D1"] = 9;                  // D1 需求從 7 → 9
            new BuildModel(dataload, optEngine).Build(); // 用新需求重建

            _isSuccess = optEngine.Solve();
            Logging.Info($"[Phase 2] Status={(_isSuccess ? "Optimal" : "Failed")}  ObjVal={(_isSuccess ? optEngine.GetObjectiveValue() : 0)}");

            // ══════════════════════════════════════════════════
            // Phase 3：CopyModel
            // 複製 optEngine 的目標式與第一個變數到新引擎
            // ══════════════════════════════════════════════════
            Logging.Info("══ Phase 3：CopyModel ══");

            OptEngine copiedEngine = optEngine.CopyModel(optEngine);
            Logging.Info($"[Phase 3] CopyModel 完成，copiedEngine != null: {copiedEngine != null}");
            copiedEngine?.Dispose();

            // ══════════════════════════════════════════════════
            // Phase 4：CreateGreatEqualThread / CreateLessEqualThread / CreateEqualThread
            //
            // 說明：以 optEngine 同時作為 controller（this）、targetEngine、sourceEngine。
            // 實際 Benders 場景中，targetEngine 為子問題引擎（提供 pool 變數），
            // sourceEngine 為主問題引擎（提供 CPLEX 模型建立限制式）。
            // 此處三者相同，用以驗證 API 在同一模型內不拋例外。
            //
            // 建立的 IRange 物件存入 _constraints，尚未加入 CPLEX 模型。
            // 需透過 MergeModel（Phase 5）才實際加入模型。
            // ══════════════════════════════════════════════════
            Logging.Info("══ Phase 4：Thread 限制式 ══");

            // CreateGreatEqualThread：A 的出貨量總和 ≥ 5
            optEngine.AddLHS(1, new VariableX_Supply { Source = "A", Dest = "D1" });
            optEngine.AddLHS(1, new VariableX_Supply { Source = "A", Dest = "D2" });
            bool geResult = optEngine.CreateGreatEqualThread(5, "Thread_GE_A", optEngine, optEngine);
            Logging.Info($"[Phase 4] CreateGreatEqualThread: {geResult}（expected: true）");

            // CreateLessEqualThread：B 的出貨量總和 ≤ 8
            optEngine.AddLHS(1, new VariableX_Supply { Source = "B", Dest = "D1" });
            optEngine.AddLHS(1, new VariableX_Supply { Source = "B", Dest = "D2" });
            bool leResult = optEngine.CreateLessEqualThread(8, "Thread_LE_B", optEngine, optEngine);
            Logging.Info($"[Phase 4] CreateLessEqualThread: {leResult}（expected: true）");

            // CreateEqualThread：D1 的入貨量總和 = 9
            optEngine.AddLHS(1, new VariableX_Supply { Source = "A", Dest = "D1" });
            optEngine.AddLHS(1, new VariableX_Supply { Source = "B", Dest = "D1" });
            bool eqResult = optEngine.CreateEqualThread(9, "Thread_EQ_D1", optEngine, optEngine);
            Logging.Info($"[Phase 4] CreateEqualThread: {eqResult}（expected: true）");

            // Dedup 測試：相同變數組合再次呼叫，應被 _verifyConstraints 擋下不重複加入
            optEngine.AddLHS(1, new VariableX_Supply { Source = "A", Dest = "D1" });
            optEngine.AddLHS(1, new VariableX_Supply { Source = "A", Dest = "D2" });
            bool dedupResult = optEngine.CreateGreatEqualThread(5, "Thread_GE_A_Dup", optEngine, optEngine);
            Logging.Info($"[Phase 4] Dedup 測試（同變數組合）: {dedupResult}（expected: true，但不新增限制式）");

            // ══════════════════════════════════════════════════
            // Phase 5：MergeModel
            // 將 optEngine._constraints 中的 thread 限制式加入 targetEngine 的 CPLEX 模型
            // ══════════════════════════════════════════════════
            Logging.Info("══ Phase 5：MergeModel ══");

            OptEngine mergeTarget = new OptEngine(GetConfig());
            mergeTarget.Build();
            OptEngine mergedEngine = optEngine.MergeModel(optEngine, mergeTarget);
            Logging.Info($"[Phase 5] MergeModel 完成，mergedEngine != null: {mergedEngine != null}");
            mergeTarget.Dispose();

            // ══════════════════════════════════════════════════
            // Phase 6：ResetThreadConstraint
            // 從 this 的 CPLEX 模型中移除兩個子引擎的限制式，清空 _constraints
            // ══════════════════════════════════════════════════
            Logging.Info("══ Phase 6：ResetThreadConstraint ══");

            OptEngine threadEngine1 = new OptEngine(GetConfig());
            OptEngine threadEngine2 = new OptEngine(GetConfig());
            threadEngine1.Build();
            threadEngine2.Build();

            optEngine.ResetThreadConstraint(threadEngine1, threadEngine2);
            Logging.Info($"[Phase 6] ResetThreadConstraint 完成");

            threadEngine1.Dispose();
            threadEngine2.Dispose();

            // ══════════════════════════════════════════════════
            // Phase 7：VariableMerge
            // 將 INumVar 集合加入 targetEngine 的 CPLEX 模型
            // 注意：VariableMerge 使用 Model.Abs()，語意上是「引用」而非「複製」
            // 空集合示範 API 可呼叫不拋例外
            // ══════════════════════════════════════════════════
            Logging.Info("══ Phase 7：VariableMerge ══");

            OptEngine vmTarget = new OptEngine(GetConfig());
            vmTarget.Build();
            var emptyVarSet = new HashSet<INumVar>();
            OptEngine vmResult = optEngine.VariableMerge(vmTarget, emptyVarSet);
            Logging.Info($"[Phase 7] VariableMerge 完成，result != null: {vmResult != null}");
            vmTarget.Dispose();

            // ══════════════════════════════════════════════════
            totalTimeSpan = totalTimer.Elapsed;
            totalTimer.Stop();
            Logging.Info($"[完成] 全部 7 個 Phase 驗證結束");
            return _isSuccess;
        }

        public void Dispose()
        {
            optEngine?.Dispose();
        }
    }
}
