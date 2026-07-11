using FJSP_BASIC.Constraint;
using FJSP_BASIC.Data;
using FJSP_BASIC.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var dataload = new Dataload();

            // 同一個 FJSP 模型、兩種組裝寫法，各自求解並代回驗證解。
            SolveAndVerify("FJSP_Aggregated", dataload, BuildModelA);
            SolveAndVerify("FJSP_Explicit", dataload, BuildModelB);

            // 交叉實驗：兩個模型 × 兩組 solver 設定。
            RunCrossExperiment(dataload);
        }

        // ── 模型組裝：每個模型只在這裡建一次，Solve 與實驗共用同一份 ──────────

        // 模型 A：把變數與限制式的建構收斂到 VariableCreate / BuildModel 兩個 "組合包class"。
        static void BuildModelA(Dataload data, OptEngine engine)
        {
            new VariableCreate(data, engine).Build();
            new BuildModel(data, engine).Build();
        }

        // 模型 B：不經 組合包class，逐條把變數與限制式直接建到 engine 上。
        static void BuildModelB(Dataload data, OptEngine engine)
        {
            engine.BuildBVs<VariableB_Assign>(data.Lot, data.Operation, data.Eqp);
            engine.BuildBVs<VariableB_Precede>(data.Lot, data.Operation, data.Lot, data.Operation);
            engine.BuildCVs<VariableX_Start>(data.Lot, data.Operation);
            engine.BuildCVs<VariableX_Complete>(data.Lot, data.Operation);
            engine.BuildCVs<VariableX_Makespan>(data.Scope);

            new ObjectiveFunction(data.Scope, engine).Build();
            new Constraint_AssignOneEqp(data.Lot, data.Operation, data.Eqp, engine).Build();
            new Constraint_CompleteDef(data.Lot, data.Operation, data.Eqp, data.parameter_ProcessTime, engine).Build();
            new Constraint_RoutePrecedence(data.Lot, data.Operation, engine).Build();
            new Constraint_NoOverlap(data.Lot, data.Operation, data.Eqp, data.BigM, engine).Build();
            new Constraint_MakespanDef(data.Lot, data.Operation, data.Scope, engine).Build();
            new Constraint_MakespanWindow(data.Scope, data.MakespanFloor, data.MakespanDeadline, engine).Build();
            new Constraint_MakespanTargetSoft(data.Scope, data.SoftMakespanTarget, data.MakespanPenalty, engine).Build();
        }

        // ── 求解單一模型並驗證解 ─────────────────────────────────────────
        static void SolveAndVerify(string name, Dataload data, Action<Dataload, OptEngine> buildModel)
        {
            using var model = new OptModel(name)
                .UseConfig(() => new CplexConfig { epGap = 1e-4, timeLimit = 30, enableLog = false })
                .AddModel(engine => buildModel(data, engine))
                .OnSolved(engine => data.WriteSolution(engine));

            bool ok = model.Execute();
            Logging.Info($"[{name}] success={ok}, status={model.optEngine.Status}");
        }

        // ── 交叉實驗：兩個模型 × 三組 solver 設定，六次執行逐行明寫（不繞 loop）──
        static void RunCrossExperiment(Dataload data)
        {
            var exp = new Experiment("fjsp-cross", "兩個模型 × 三組 solver 設定的交叉實驗");

            // 三組設定：對 CplexConfig 的調參委派
            Action<CplexConfig> balanced = c => c.Emphasis = 0;
            Action<CplexConfig> feasible = c => c.Emphasis = 1;
            Action<CplexConfig> optimal = c => c.Emphasis = 2;

            // 模型A 跑三種實驗
            exp.AddTrial(RunTrial(data, "A-Aggregated | balanced", BuildModelA, balanced));
            exp.AddTrial(RunTrial(data, "A-Aggregated | feasible", BuildModelA, feasible));
            exp.AddTrial(RunTrial(data, "A-Aggregated | optimal", BuildModelA, optimal));

            // 模型B 跑三種實驗
            exp.AddTrial(RunTrial(data, "B-Explicit | balanced", BuildModelB, balanced));
            exp.AddTrial(RunTrial(data, "B-Explicit | feasible", BuildModelB, feasible));
            exp.AddTrial(RunTrial(data, "B-Explicit | optimal", BuildModelB, optimal));

            // Save() 會把同名實驗的舊 Trial 併進來（累積調校歷史），故檔案列數可能多於本次。
            // 收斂軌跡由框架 TrajectoryCsvWriter 輸出成 {name}-trajectory.csv（1 列/收斂點），可直接畫收斂曲線。
            exp.Save();
            Logging.Info($"[Cross] 本次 6 Trial → Experiments/{exp.Name}.csv / .json / -trajectory.csv");
        }

        // 跑一次：套用設定 → 開全新 engine → 建模 → Solve，擷取成 Trial。
        static Trial RunTrial(Dataload data, string label, Action<Dataload, OptEngine> buildModel, Action<CplexConfig> tune)
        {
            var config = new CplexConfig { epGap = 1e-4, timeLimit = 10, enableLog = false };
            tune(config);

            using var engine = new OptEngine(config);
            engine.Build();
            buildModel(data, engine);

            var trial = Trial.Capture(engine, label, () => engine.Solve());
            var m = trial.Metrics;
            Logging.Info($"[Cross] {label}: status={m.Status} obj={m.ObjectiveValue:G6} time={m.WallTimeMs:F0}ms traj={m.Convergence?.Count ?? 0}pts");
            return trial;
        }
    }
}
