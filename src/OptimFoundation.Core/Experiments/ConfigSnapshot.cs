using System.Collections.Generic;
using System.Reflection;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 求解設定快照。雙軌：抽象旋鈕（<see cref="ITunableConfig"/> + 共用 ISolverConfig）+
    /// reflection 抓 concrete 專屬欄位，確保不漏任何設定，供之後重現與 LLM tuning 參考。
    /// </summary>
    public sealed class ConfigSnapshot
    {
        public string Solver { get; set; }
        public Dictionary<string, object> Tunable { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> SolverSpecific { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 從 ISolverConfig 建立快照：抽象旋鈕讀 ITunableConfig，專屬欄位用 reflection 列舉 public field/property。
        /// </summary>
        public static ConfigSnapshot From(ISolverConfig config)
        {
            var snapshot = new ConfigSnapshot();
            if (config == null) return snapshot;

            // Solver 名 = concrete config 的 namespace 末段（OptimFoundation.Cplex → "Cplex"）
            string ns = config.GetType().Namespace ?? "";
            int dot = ns.LastIndexOf('.');
            snapshot.Solver = dot >= 0 ? ns.Substring(dot + 1) : ns;

            // 共用旋鈕
            snapshot.Tunable["TimeLimit"] = config.TimeLimit;
            snapshot.Tunable["MipGap"] = config.MipGap;
            snapshot.Tunable["Threads"] = config.Threads;

            // 跨引擎抽象旋鈕
            if (config is ITunableConfig t)
            {
                snapshot.Tunable["Seed"] = t.Seed;
                snapshot.Tunable["Emphasis"] = t.Emphasis;
                snapshot.Tunable["FeasibilityTol"] = t.FeasibilityTol;
                snapshot.Tunable["OptimalityTol"] = t.OptimalityTol;
                snapshot.Tunable["RootAlgorithm"] = t.RootAlgorithm;
                snapshot.Tunable["Presolve"] = t.Presolve;
                snapshot.Tunable["HeuristicEffort"] = t.HeuristicEffort;
                snapshot.Tunable["MemoryLimitMb"] = t.MemoryLimitMb;
            }

            // Solver 專屬：reflection 列舉 public field + 可讀 property（補抓抽象面沒涵蓋的設定）
            var type = config.GetType();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                snapshot.SolverSpecific[f.Name] = f.GetValue(config);
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                try { snapshot.SolverSpecific[p.Name] = p.GetValue(config); }
                catch { /* 跳過讀取會丟例外的 property */ }
            }
            return snapshot;
        }
    }
}
