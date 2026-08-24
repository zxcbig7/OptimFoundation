using System.Collections.Generic;
using System.Reflection;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 求解設定快照：只記「真的有設」的旋鈕。
    ///
    /// 沒設的旋鈕值是 null，代表「不動它、用求解器自己的預設」——這是已知且固定的規則，
    /// 所以「沒列出來」跟「列出來寫 null」帶的資訊完全一樣。以前把 183 顆全寫進去，
    /// 其中 95% 是 null，每個 trial 重抄一次，檔案大得沒人看得下去。現在只記有值的。
    /// </summary>
    public sealed class ConfigSnapshot
    {
        /// <summary>求解器名稱，取自 config 型別的 namespace 末段（OptimFoundation.Cplex → "Cplex"）。</summary>
        public string Solver { get; set; }

        /// <summary>跨 solver 的共通旋鈕（TimeLimit / MipGap / Threads / Seed / Emphasis …）。只放有設定的。</summary>
        public Dictionary<string, object> Tunable { get; set; } = new Dictionary<string, object>();

        /// <summary>該求解器全部旋鈕裡「有設定」的那些（含與 Tunable 重疊的部分）。沒列到的就是沒設。</summary>
        public Dictionary<string, object> SolverSpecific { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 從 ISolverConfig 建立快照：抽象控制項目讀 ITunableConfig，專屬欄位用 reflection 列舉 public field/property。
        /// </summary>
        public static ConfigSnapshot From(ISolverConfig config)
        {
            var snapshot = new ConfigSnapshot();
            if (config == null) return snapshot;

            // Solver 名 = concrete config 的 namespace 末段（OptimFoundation.Cplex → "Cplex"）
            string ns = config.GetType().Namespace ?? "";
            int dot = ns.LastIndexOf('.');
            snapshot.Solver = dot >= 0 ? ns.Substring(dot + 1) : ns;

            // 共用旋鈕（null 就是沒設，不記）
            Put(snapshot.Tunable, "TimeLimit", config.TimeLimit);
            Put(snapshot.Tunable, "MipGap", config.MipGap);
            Put(snapshot.Tunable, "Threads", config.Threads);

            if (config is ITunableConfig t)
            {
                Put(snapshot.Tunable, "Seed", t.Seed);
                Put(snapshot.Tunable, "Emphasis", t.Emphasis);
                Put(snapshot.Tunable, "FeasibilityTol", t.FeasibilityTol);
                Put(snapshot.Tunable, "OptimalityTol", t.OptimalityTol);
                Put(snapshot.Tunable, "RootAlgorithm", t.RootAlgorithm);
                Put(snapshot.Tunable, "Presolve", t.Presolve);
                Put(snapshot.Tunable, "HeuristicEffort", t.HeuristicEffort);
                Put(snapshot.Tunable, "MemoryLimitMb", t.MemoryLimitMb);
            }

            // Solver 專屬：reflection 列舉 public field + 可讀 property（補抓抽象面沒涵蓋的設定）
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

        /// <summary>只在有值時才寫進字典。null = 沒設定 = 用求解器預設，不需要記。</summary>
        private static void Put(Dictionary<string, object> target, string key, object value)
        {
            if (value != null) target[key] = value;
        }
    }
}
