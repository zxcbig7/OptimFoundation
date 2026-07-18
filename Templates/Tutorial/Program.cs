using System;
using Tutorial.Data;
using Tutorial.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace Tutorial
{
    // 三種模式（模型 code 一行不變，只換資料來源 / 外層流程）：
    //   dotnet run                -- CSV 來源（預設）：讀 Data/*.csv → solve → 讀解印計畫 → 解寫回 Solution/*.csv
    //   dotnet run -- inmemory    -- 記憶體來源：示範「換來源只換 IDataSource」
    //   dotnet run -- experiment  -- 實驗模式：同一模型 × 三組 solver 設定，Experiments/*.csv/json/-trajectory.csv
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "experiment") { ExperimentRunner.Run(new Dataload()); return; }

            // 換來源示範：InMemoryDataSource 與 CsvDataSource 同一介面，Dataload / 模型全不動
            var dataload = args.Length > 0 && args[0] == "inmemory"
                ? new Dataload(BuildInMemorySource())
                : new Dataload();

            SolveAndReport("Tutorial_ProductMix", dataload);
        }

        // ── 求解 + 讀解 + 寫解 ─────────────────────────────────────────
        static void SolveAndReport(string name, Dataload data)
        {
            var def = new Model.TutorialModel(data);   // 模型積木：整個模型包成一顆
            using var model = new OptModel(name)
                .UseConfig(() => new CplexConfig { epGap = 1e-6, timeLimit = 120, enableLog = false })
                .AddVariables(def.CreateVariables)
                .AddModel(def.CreateModel)
                .OnSolved(engine =>
                {
                    data.WriteSolution(engine);   // 印生產計畫（讀解 API）
                    WriteSolutionCsv(engine);     // 解寫回 CSV（foundation ISolutionSink）
                });

            bool ok = model.Execute();
            Logging.Info($"[{name}] success={ok}, status={model.optEngine.Status}");
        }

        // 解輸出＝CSV：CsvSolutionSink → Solution/{變數型別}.csv（帶表頭，可被 CsvCtrl.BuildParameter 讀回）。
        static void WriteSolutionCsv(OptEngine engine)
        {
            var sink = new CsvSolutionSink();
            sink.WriteSolution<VariableX_Produce>(engine);   // 連續：各班生產量
            sink.WriteSolution<VariableB_Setup>(engine);     // 二元：各班開線與否
            sink.WriteSolution<VariableI_Batch>(engine);     // 整數：各日批數
        }

        // ── 記憶體來源：demo / 測試用，數值與 Data/*.csv 相同（set 成員一律傳字串，ParseElement 依 T 轉型）──
        static InMemoryDataSource BuildInMemorySource()
        {
            var d1 = new DateTime(2026, 8, 1);
            var d2 = new DateTime(2026, 8, 2);
            return new InMemoryDataSource()
                .AddSet("Set_Product", new[] { "Desk", "Chair", "Table" })
                .AddSet("Set_Machine", new[] { "Cutting", "Assembly" })
                .AddSet("Set_Date", new[] { "2026-08-01", "2026-08-02" })   // DateTime set：字串由 ParseElement 轉
                .AddSet("Set_Shift", new[] { "1", "2" })                    // int set：字串由 ParseElement 轉
                .AddParameters(new[]
                {
                    new ParameterClass.Parameter_UnitProfit { Product = "Desk", QTY = 12 },
                    new ParameterClass.Parameter_UnitProfit { Product = "Chair", QTY = 8 },
                    new ParameterClass.Parameter_UnitProfit { Product = "Table", QTY = 15 },
                })
                .AddParameters(new[]
                {
                    new ParameterClass.Parameter_SetupCost { Product = "Desk", QTY = 50 },
                    new ParameterClass.Parameter_SetupCost { Product = "Chair", QTY = 30 },
                    new ParameterClass.Parameter_SetupCost { Product = "Table", QTY = 80 },
                })
                .AddParameters(new[]
                {
                    new ParameterClass.Parameter_BatchSize { Product = "Desk", QTY = 5 },
                    new ParameterClass.Parameter_BatchSize { Product = "Chair", QTY = 5 },
                    new ParameterClass.Parameter_BatchSize { Product = "Table", QTY = 1 },
                })
                .AddParameters(new[]
                {
                    new ParameterClass.Parameter_MachineHours { Product = "Desk", Machine = "Cutting", QTY = 2 },
                    new ParameterClass.Parameter_MachineHours { Product = "Desk", Machine = "Assembly", QTY = 3 },
                    new ParameterClass.Parameter_MachineHours { Product = "Chair", Machine = "Cutting", QTY = 1 },
                    new ParameterClass.Parameter_MachineHours { Product = "Chair", Machine = "Assembly", QTY = 2 },
                    new ParameterClass.Parameter_MachineHours { Product = "Table", Machine = "Cutting", QTY = 3 },
                    new ParameterClass.Parameter_MachineHours { Product = "Table", Machine = "Assembly", QTY = 4 },
                })
                .AddParameters(new[]
                {
                    new ParameterClass.Parameter_Demand { Product = "Desk", Date = d1, QTY = 5 },
                    new ParameterClass.Parameter_Demand { Product = "Desk", Date = d2, QTY = 5 },
                    new ParameterClass.Parameter_Demand { Product = "Chair", Date = d1, QTY = 10 },
                    new ParameterClass.Parameter_Demand { Product = "Chair", Date = d2, QTY = 10 },
                    new ParameterClass.Parameter_Demand { Product = "Table", Date = d1, QTY = 3 },
                    new ParameterClass.Parameter_Demand { Product = "Table", Date = d2, QTY = 3 },
                })
                .AddParameters(BuildCapacity(d1, d2));
        }

        // Capacity[Machine, Date, Shift]：日班(1)產能高、夜班(2)低——讓 Shift 維度有實質差異
        static System.Collections.Generic.List<ParameterClass.Parameter_Capacity> BuildCapacity(DateTime d1, DateTime d2)
        {
            var cap = new System.Collections.Generic.Dictionary<(string, int), double>
            {
                { ("Cutting", 1), 40 }, { ("Cutting", 2), 30 },
                { ("Assembly", 1), 60 }, { ("Assembly", 2), 45 },
            };
            var rows = new System.Collections.Generic.List<ParameterClass.Parameter_Capacity>();
            foreach (var machine in new[] { "Cutting", "Assembly" })
                foreach (var date in new[] { d1, d2 })
                    foreach (var shift in new[] { 1, 2 })
                        rows.Add(new ParameterClass.Parameter_Capacity { Machine = machine, Date = date, Shift = shift, QTY = cap[(machine, shift)] });
            return rows;
        }
    }
}
