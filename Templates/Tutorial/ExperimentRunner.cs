using Tutorial.Data;
using Tutorial.Model;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial
{
    /// <summary>
    /// 實驗模式（Phase 3 Tuning 的工具）：同一模型 × 多組 solver 設定，每次 solve 擷取成一筆 Trial。
    /// Trial.Capture 不接管 engine 生命週期，只讀設定（ConfigSnapshot）與結果（SolveMetrics + 收斂軌跡）。
    /// exp.Save() 輸出 Experiments/{name}.csv（1 列/trial 摘要）+ .json（權威，含軌跡）+ -trajectory.csv（畫收斂曲線用），
    /// 同名實驗舊 Trial 會併入累積——調校歷史不會被蓋掉。
    /// </summary>
    public static class ExperimentRunner
    {
        public static void Run(Dataload data)
        {
            var exp = new Experiment("tutorial-productmix", "同一模型 × 三組 MIP emphasis 對照");

            exp.AddTrial(RunTrial(data, "balanced", config => config.Emphasis = 0));
            exp.AddTrial(RunTrial(data, "feasible-first", config => config.Emphasis = 1));
            exp.AddTrial(RunTrial(data, "optimal-first", config => config.Emphasis = 2));

            exp.Save();
            Logging.Info($"[Experiment] 3 Trial → Experiments/{exp.Name}.csv / .json / -trajectory.csv");
        }

        // 跑一次：套用設定 → 開全新 engine → 模型積木 Build（與 solve 模式共用同一顆 TutorialModel）→ Solve 擷取成 Trial
        private static Trial RunTrial(Dataload data, string label, Action<CplexConfig> tune)
        {
            var config = new CplexConfig { epGap = 1e-6, timeLimit = 120, enableLog = false };
            tune(config);

            using var engine = new OptEngine(config);
            engine.Build();
            new TutorialModel(data).Build(engine);

            var trial = Trial.Capture(engine, label, () => engine.Solve());
            var metrics = trial.Metrics;
            Logging.Info($"[Experiment] {label}: status={metrics.Status} obj={metrics.ObjectiveValue:G6} time={metrics.RunTimeMs:F0}ms");
            return trial;
        }
    }
}
