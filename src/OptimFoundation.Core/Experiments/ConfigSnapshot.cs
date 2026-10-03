using System.Collections.Generic;
using System.Reflection;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 保存一次求解使用的非 null 設定。未列出的參數使用求解器預設值，
    /// 只保留有設定的項目，方便比較並縮小實驗檔案。
    /// </summary>
    public sealed class ConfigSnapshot
    {
        /// <summary>求解器名稱，取自 config 型別的 namespace 末段（OptimFoundation.Cplex → "Cplex"）。</summary>
        public string Solver { get; set; }

        /// <summary>各求解器共用的參數（TimeLimit / MipGap / Threads / Seed / Emphasis 等），只保存非 null 的值。</summary>
        public Dictionary<string, object> Tunable { get; set; } = new Dictionary<string, object>();

        /// <summary>此求解器設定物件中所有非 null 的公開欄位與屬性，包含 Tunable 已列出的參數。</summary>
        public Dictionary<string, object> SolverSpecific { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 複製 ISolverConfig 的共用參數，再透過 reflection 讀取實際設定類別的 public field/property。
        /// </summary>
        public static ConfigSnapshot From(ISolverConfig config)
        {
            var snapshot = new ConfigSnapshot();
            if (config == null) return snapshot;

            // 求解器名稱取設定類別 namespace 的最後一段，例如 OptimFoundation.Cplex 取 "Cplex"。
            string ns = config.GetType().Namespace ?? "";
            int dot = ns.LastIndexOf('.');
            snapshot.Solver = dot >= 0 ? ns.Substring(dot + 1) : ns;

            // 保存共用參數；null 表示未設定，不寫入紀錄。
            Put(snapshot.Tunable, "TimeLimit", config.TimeLimit);
            Put(snapshot.Tunable, "MipGap", config.MipGap);
            Put(snapshot.Tunable, "Threads", config.Threads);
            Put(snapshot.Tunable, "Seed", config.Seed);
            Put(snapshot.Tunable, "Emphasis", config.Emphasis);
            Put(snapshot.Tunable, "FeasibilityTol", config.FeasibilityTol);
            Put(snapshot.Tunable, "OptimalityTol", config.OptimalityTol);
            Put(snapshot.Tunable, "RootAlgorithm", config.RootAlgorithm);
            Put(snapshot.Tunable, "Presolve", config.Presolve);
            Put(snapshot.Tunable, "HeuristicEffort", config.HeuristicEffort);
            Put(snapshot.Tunable, "MemoryLimitMb", config.MemoryLimitMb);

            // 讀取設定類別的公開欄位與可讀屬性，補上 ISolverConfig 未列出的求解器參數。
            var type = config.GetType();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                Put(snapshot.SolverSpecific, f.Name, f.GetValue(config));
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                try { Put(snapshot.SolverSpecific, p.Name, p.GetValue(config)); }
                catch (System.Exception ex)
                {
                    Logging.Warn($"[CONFIG_SNAPSHOT_SKIPPED] 設定快照略過屬性 | property={p.Name} type={type.FullName} reason={ex.GetBaseException().Message} result=omitted");
                }
            }
            return snapshot;
        }

        /// <summary>只將非 null 的值寫入字典；null 代表使用求解器預設值。</summary>
        private static void Put(Dictionary<string, object> target, string key, object value)
        {
            if (value != null) target[key] = value;
        }
    }
}
