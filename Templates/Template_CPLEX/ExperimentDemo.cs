using System;

using OptimFoundation.Core;
using OptimFoundation.Cplex;

using SandBox.Data;
using SandBox.Constraints;
using SandBox.VariablesClass;

namespace SandBox
{
    /// <summary>
    /// Experiment tuning 環境示範：同一個 Rostering 模型，掃不同 solver 設定，
    /// 每組設定跑一次 <see cref="Trial.Capture"/> 記錄「完整設定 + 收斂數據」，
    /// 累積成 <see cref="Experiment"/> 後輸出 Experiments/&lt;name&gt;.csv + .json。
    ///
    /// 執行：dotnet run -- experiment
    /// </summary>
    public static class ExperimentDemo
    {
        public static void Run()
        {
            Logging.SetLogFileName("ExperimentDemo");

            var exp = new Experiment(
                "rostering-tuning",
                "比較不同 mipEmphasis / randomSeed 對求解時間與 gap 的影響");

            // 要掃的設定（透過 ITunableConfig 抽象控制項目 tune，跨引擎一致）
            var variants = new (string label, Action<CplexConfig> tune)[]
            {
                ("baseline",          _ => { }),
                ("emphasis=feasible", c => c.Emphasis = 1),
                ("emphasis=optimal",  c => { c.Emphasis = 2; c.Seed = 7; }),
            };

            foreach (var (label, tune) in variants)
            {
                var config = new CplexConfig
                {
                    epGap = 0.03,
                    timeLimit = 60,
                    workThreads = 10,
                    enableLog = false,   // 掃描時關 solver log
                };
                tune(config);

                // 每個 Trial 用全新 engine（避免狀態跨 Trial 污染），求解後自然 Dispose
                var dataload = new Dataload();
                using var engine = new OptEngine(config);
                engine.Build();
                new VariableCreate(dataload, engine).Build();
                new BuildModel(dataload, engine).Build();

                // 套件化單次擷取：抓這一 run 的完整設定 + 收斂數據（CPLEX 自動含收斂軌跡）
                var trial = Trial.Capture(engine, label, () => engine.Solve());
                exp.AddTrial(trial);

                var m = trial.Metrics;
                Logging.Info(
                    $"[ExperimentDemo] {label}: Status={m.Status} Obj={m.ObjectiveValue:G6} " +
                    $"Gap={m.MipGap:P2} Time={m.WallTimeMs:F0}ms Nodes={m.NodeCount} " +
                    $"TrajPoints={m.Convergence.Count}");
            }

            exp.Save();   // → Experiments/rostering-tuning.csv + .json
            Logging.Info($"[ExperimentDemo] 完成：{exp.Trials.Count} 個 Trial 已寫入 Experiments/");
        }
    }
}
