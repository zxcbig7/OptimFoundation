using FJSP_BASIC_BRICK.ParameterClass;
using FJSP_BASIC_BRICK.SetClass;
using FJSP_BASIC_BRICK.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK.Data
{
    // 資料層唯一入口：ctor 就是「寫讀檔的家」——每行一句、顯式，特例就地指定檔名。
    // 換來源：CSV↔InMemory 只換傳入的 IDataSource（DB query-only 用型別化 DbDataSource，見 Tutorial 範本）。
    public class Dataload
    {
        // Set 積木（[OptSet]）：就是 Set，可直接 [i] / Count / foreach / LINQ / 餵 BuildVars
        public Set_Lot LOT = new();
        public Set_Operation OPERATION = new();   // 行序 = 加工順序（RoutePrecedence 依此索引）
        public Set_Eqp EQP = new();

        public List<Parameter_ProcessTime> parameter_ProcessTime = new();

        // BigM = Σ_{lot,op} max_eqp ProcessTime（最壞情況全序列排程長度），由數據推導、NEVER 寫死
        public double BigM => parameter_ProcessTime
            .GroupBy(p => new { p.Lot, p.Operation })
            .Sum(g => g.Max(p => p.QTY));

        // 保證 infeasible 的 Makespan 上限（模型 C 示範 IIS 用）：
        // 任一 lot 的 makespan 下界 = Σ_op min_eqp ProcessTime（逐道序列、每道取最快機台），
        // 取所有 lot 下界的最大值再 -1 → 上限嚴格低於理論下界，必定無解；由數據推導、NEVER 寫死
        public double InfeasibleMakespanCap => parameter_ProcessTime
            .GroupBy(p => new { p.Lot, p.Operation })
            .Select(g => new { g.Key.Lot, MinTime = g.Min(p => p.QTY) })
            .GroupBy(x => x.Lot)
            .Max(g => g.Sum(x => x.MinTime)) - 1;

        // 示範用純量參數（Range / Soft demo）；放大實例後 makespan 大很多，Range 上界改用 BigM 保證可行
        public double MakespanFloor = 0; // Range 下界（非綁定；makespan 天生 ≥ 0）
        public double MakespanDeadline => BigM; // Range 上界 = 最壞情況上界，保證可行、非綁定
        public double SoftMakespanTarget = 10; // Soft 目標（放大後很可能被違反 → penalty 現形）
        public double MakespanPenalty = 2; // Soft 每單位違反的懲罰（進目標式）

        // 預設資料來源＝CSV
        public Dataload() : this(new CsvDataSource()) { }

        public Dataload(IDataSource source)
        {
            LOT.Load(source);
            OPERATION.Load(source);
            EQP.Load(source);
            parameter_ProcessTime = source.LoadParam<Parameter_ProcessTime>();
            ValidateSetsCoverParameters();
        }

        // 載入即 fail fast：parameter 出現的值必須 ⊆ 對應 set
        private void ValidateSetsCoverParameters()
        {
            var missing = new List<string>();
            missing.AddRange(parameter_ProcessTime.Select(p => p.Lot).Distinct().Where(v => !LOT.Contains(v)).Select(v => $"Lot '{v}'"));
            missing.AddRange(parameter_ProcessTime.Select(p => p.Operation).Distinct().Where(v => !OPERATION.Contains(v)).Select(v => $"Operation '{v}'"));
            missing.AddRange(parameter_ProcessTime.Select(p => p.Eqp).Distinct().Where(v => !EQP.Contains(v)).Select(v => $"Eqp '{v}'"));
            if (missing.Count > 0)
                throw new InvalidDataException($"[Dataload] Parameter_ProcessTime 出現不在 set 檔的值：{string.Join("、", missing)}");
        }

        // 生成 N lots × M ops × K machines 的放大版 FJSP 實例（seeded 決定論）。
        // 用途：Program 的 `gen-csv` 以此產出樣本輸入 Data/Parameter_ProcessTime.csv（要換規模改參數）。
        public static List<Parameter_ProcessTime> GenerateInstance(int lots, int operations, int eqps, int seed)
        {
            var rng = new Random(seed);
            var rows = new List<Parameter_ProcessTime>();
            for (int l = 1; l <= lots; l++)
                for (int o = 1; o <= operations; o++)
                    for (int e = 1; e <= eqps; e++)
                        rows.Add(new Parameter_ProcessTime
                        {
                            Lot = $"LOT{l}",
                            Operation = $"OP{o}",
                            Eqp = $"EQP{e}",
                            QTY = rng.Next(2, 10)   // 2..9 小時
                        });
            return rows;
        }

        /// <summary>解出後印出排程 + 依解驗證協定把解代回每條 constraint 檢查。</summary>
        public void WriteSolution(OptEngine engine)
        {
            // 批量取解：一次抓整個型別的解值 Dictionary（key = 完整變數 key），取代逐 key 呼叫
            var assign = engine.GetSetVarValues<VariableB_Assign>();
            var start = engine.GetSetVarValues<VariableX_Start>();
            var complete = engine.GetSetVarValues<VariableX_Complete>();
            var makespanVar = engine.GetSetVarValues<VariableX_Makespan>();

            // Soft 讓目標式 = Makespan + penalty·違反量，故 makespan 改從變數讀（GetObjectiveValue 已含 penalty）
            double makespan = makespanVar["VariableX_Makespan"];   // scalar：key 無 @ 索引
            double objective = engine.GetObjectiveValue();
            double violation = Math.Max(0, makespan - SoftMakespanTarget);
            double penaltyCost = violation * MakespanPenalty;

            Logging.Info($"===== FJSP_BASIC_BRICK 解 =====");
            Logging.Info($"Makespan = {makespan:0.##} 小時（真實排程長度，取自 VariableX_Makespan）");
            Logging.Info($"Objective = {objective:0.##}（= Makespan {makespan:0.##} + soft penalty {penaltyCost:0.##}）");
            Logging.Info($"Soft 目標 Makespan ≤ {SoftMakespanTarget:0.##}：違反 {violation:0.##} × penalty {MakespanPenalty:0.##} = {penaltyCost:0.##}");
            Logging.Info($"Range 窗 [{MakespanFloor:0.##}, {MakespanDeadline:0.##}]：{(makespan >= MakespanFloor - 1e-6 && makespan <= MakespanDeadline + 1e-6 ? "滿足" : "違反")}");

            foreach (var lot in LOT)
            {
                foreach (var op in OPERATION)
                {
                    string assignedEqp = EQP.FirstOrDefault(e => assign[$"VariableB_Assign@{lot}@{op}@{e}"] > 0.5) ?? "?";
                    double s = start[$"VariableX_Start@{lot}@{op}"];
                    double c = complete[$"VariableX_Complete@{lot}@{op}"];
                    Logging.Info($"{lot} {op} → {assignedEqp}  [{s:0.##}, {c:0.##}]");
                }
            }

            VerifySolution(engine, makespan, objective, penaltyCost);
        }

        /// <summary>可行性代回：逐條 constraint 用解值檢查 LHS op RHS，全過才算會動。</summary>
        private void VerifySolution(OptEngine engine, double makespan, double objective, double penaltyCost)
        {
            const double eps = 1e-6;
            int failCount = 0;
            void Check(bool ok, string rule)
            {
                if (!ok) { failCount++; Logging.Info($"[VERIFY FAIL] {rule}"); }
            }

            var assignVals = engine.GetSetVarValues<VariableB_Assign>();
            var startVals = engine.GetSetVarValues<VariableX_Start>();
            var completeVals = engine.GetSetVarValues<VariableX_Complete>();

            var start = new Dictionary<(string, string), double>();
            var complete = new Dictionary<(string, string), double>();
            var assigned = new Dictionary<(string, string), string>();

            foreach (var lot in LOT)
            {
                foreach (var op in OPERATION)
                {
                    start[(lot, op)] = startVals[$"VariableX_Start@{lot}@{op}"];
                    complete[(lot, op)] = completeVals[$"VariableX_Complete@{lot}@{op}"];

                    // AssignOneEqp：恰好指派一台
                    var eqps = EQP.Where(e => assignVals[$"VariableB_Assign@{lot}@{op}@{e}"] > 0.5).ToList();
                    Check(eqps.Count == 1, $"AssignOneEqp@{lot}@{op}：指派了 {eqps.Count} 台");
                    if (eqps.Count == 1) assigned[(lot, op)] = eqps[0];

                    // CompleteDef：Complete = Start + ProcessTime
                    if (eqps.Count == 1)
                    {
                        var procTime = parameter_ProcessTime
                            .FirstOrDefault(p => p.Lot == lot && p.Operation == op && p.Eqp == eqps[0])?.QTY ?? 0.0;
                        Check(Math.Abs(complete[(lot, op)] - start[(lot, op)] - procTime) < 1e-4,
                            $"CompleteDef@{lot}@{op}：{complete[(lot, op)]} ≠ {start[(lot, op)]} + {procTime}");
                    }
                }

                // RoutePrecedence：下一道 ≥ 前一道完成
                for (int i = 0; i + 1 < OPERATION.Count; i++)
                    Check(start[(lot, OPERATION[i + 1])] >= complete[(lot, OPERATION[i])] - eps,
                        $"RoutePrecedence@{lot}@{OPERATION[i]}→{OPERATION[i + 1]}");

                // MakespanDef：Makespan ≥ 最後一道完成
                Check(makespan >= complete[(lot, OPERATION[OPERATION.Count - 1])] - eps, $"MakespanDef@{lot}");
            }

            // NoOverlap：同機台任兩作業時段不重疊
            var all = assigned.Keys.ToList();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    var opA = all[i]; var opB = all[j];
                    if (assigned[opA] != assigned[opB]) continue;
                    bool separated = complete[opA] <= start[opB] + eps || complete[opB] <= start[opA] + eps;
                    Check(separated,
                        $"NoOverlap@{opA.Item1}.{opA.Item2}×{opB.Item1}.{opB.Item2}@{assigned[opA]}：" +
                        $"[{start[opA]},{complete[opA]}] 與 [{start[opB]},{complete[opB]}] 重疊");
                }

            // MakespanWindow（Range，硬約束）：makespan 必須落在窗內
            Check(makespan >= MakespanFloor - eps && makespan <= MakespanDeadline + eps,
                $"MakespanWindow：{makespan} 不在 [{MakespanFloor}, {MakespanDeadline}]");

            // Soft 不判 FAIL（本來就允許違反）；改對帳目標式分解 objective = makespan + penalty·違反量
            Check(Math.Abs(objective - (makespan + penaltyCost)) < 1e-4,
                $"ObjectiveDecomp：obj {objective} ≠ makespan {makespan} + penalty {penaltyCost}");

            Logging.Info(failCount == 0
                ? "[VERIFY] 全部 constraint 代回檢查 PASS（soft 允許違反，僅對帳）"
                : $"[VERIFY] {failCount} 條 FAIL");
        }
    }
}
