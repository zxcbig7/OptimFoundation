using System;

using OptimFoundation.Core;
using OptimFoundation.Cplex;

using SandBox.Data;
using SandBox.Constraints;
using SandBox.VariablesClass;

namespace SandBox
{
    /// <summary>
    /// Model1 vs Model2 交叉實驗：兩個模型 × 兩組 solver 設定，各跑一次 Trial.Capture，
    /// 累積成 Experiment 匯出 Experiments/&lt;name&gt;.csv + .json 供對照。
    /// Model1 = 基礎模型；Model2 = 基礎模型 + Constraint_TEST。
    /// 架構仿 FJSP_BASIC 的 RunCrossExperiment / RunTrial（build 委派 + Trial.Capture）。
    ///
    /// 執行：dotnet run -- cross
    /// </summary>
    public static class CrossExperiment
    {
        // ── 兩個模型：build 委派（同 FJSP 的 Action<Dataload, OptEngine>）──
        static void BuildModel1(Dataload data, OptEngine engine)
        {
            new VariableCreate(data, engine).Build();
            new BuildModel(data, engine).Build();
        }

        static void BuildModel2(Dataload data, OptEngine engine)
        {
            new VariableCreate(data, engine).Build();
            new BuildModel(data, engine).Build();
            new Constraint_TEST(data.Date, data.Employee, data.parameter_ShiftDemand, engine).Build();
        }

        public static void Run()
        {
            Logging.SetLogFileName("CrossExperiment");

            // 資料只讀一次，跨所有 Trial 共用（模型只讀不改）
            var data = new Dataload();

            var exp = new Experiment("rostering-cross", "Model1 vs Model2 × 兩組 solver 設定的交叉實驗");

            // 兩組設定：對 CplexConfig 的調參委派
            Action<CplexConfig> baseline = c => { c.timeLimit = 180; };
            Action<CplexConfig> emphasis = c => { c.timeLimit = 180; c.Emphasis = 2; };
            Action<CplexConfig> threads = c => { c.timeLimit = 180; c.Threads = 2; };

            // 模型 × 設定 交叉，逐行明寫（不繞 loop，對照清楚）
            exp.AddTrial(RunTrial(data, "Model1 | baseline", BuildModel1, baseline));
            exp.AddTrial(RunTrial(data, "Model1 | emphasis", BuildModel1, emphasis));
            exp.AddTrial(RunTrial(data, "Model1 | threads", BuildModel1, threads));
            exp.AddTrial(RunTrial(data, "Model2 | baseline", BuildModel2, baseline));
            exp.AddTrial(RunTrial(data, "Model2 | emphasis", BuildModel2, emphasis));
            exp.AddTrial(RunTrial(data, "Model2 | threads", BuildModel2, threads));

            exp.Save();
            Logging.Info($"[Cross] 6 Trial（Model1×3 + Model2×3）→ Experiments/{exp.Name}.csv / .json");
        }

        // 跑一次：套設定 → 開全新 engine → 建模 → Solve，擷取成 Trial
        static Trial RunTrial(Dataload data, string label, Action<Dataload, OptEngine> buildModel, Action<CplexConfig> tune)
        {
            var config = new CplexConfig { epGap = 0.03, timeLimit = 60, workThreads = 10, enableLog = false };
            tune(config);

            using var engine = new OptEngine(config);
            engine.Build();
            buildModel(data, engine);

            var trial = Trial.Capture(engine, label, () => engine.Solve());
            var m = trial.Metrics;
            Logging.Info($"[Cross] {label}: Status={m.Status} Obj={m.ObjectiveValue:G6} Gap={m.MipGap:P2} Time={m.RunTimeMs:F0}ms");
            return trial;
        }
    }
}
