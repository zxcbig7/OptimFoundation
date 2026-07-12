using System.Globalization;
using System.Text;
using FJSP_BASIC.Constraint;
using FJSP_BASIC.Data;
using FJSP_BASIC.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace FJSP_BASIC
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // 選用工具：`dotnet run -- gen-csv` 由生成器重產樣本輸入 Data/Parameter_ProcessTime.csv（可調規模）
            if (args.Length > 0 && args[0] == "gen-csv") { WriteSampleCsv(); return; }

            // 資料來源＝CSV：Dataload() 預設從 Data/Parameter_ProcessTime.csv 讀（foundation CsvCtrl，表頭按名對位）。
            // 換 DB / 記憶體只需 new Dataload(new DbDataSource(...))，模型與驗證 code 全不動。
            var dataload = new Dataload();

            // 同一個 FJSP 模型、兩種組裝寫法，各自求解並代回驗證解；解寫回 Solution/*.csv。
            SolveAndVerify("FJSP_Aggregated", dataload, BuildModelA);
            SolveAndVerify("FJSP_Explicit", dataload, BuildModelB);

            // 模型 C 預期 Infeasible：驗證點是 IIS 衝突分析輸出（IISs/*.ilp + 衝突名單），不是解。
            SolveExpectInfeasible("FJSP_InfeasibleIIS", dataload, BuildModelC);

            // 交叉實驗：兩個模型 × 三組 solver 設定。
            RunCrossExperiment(dataload);
        }

        // 由 seeded 生成器產出樣本輸入 CSV：參數檔（帶表頭 Lot,Operation,Eqp,QTY）+ 三個 set 檔（Set_{Name}.csv，一行一成員、無表頭）。
        // 這就是 repo 內 Data/ 各輸入檔的來源；輸出到執行目錄 Data/，換規模改這裡參數。
        static void WriteSampleCsv()
        {
            FolderDir.Data.CreateFolder();
            var rows = Dataload.GenerateInstance(lots: 6, operations: 3, eqps: 4, seed: 42);
            var sb = new StringBuilder("Lot,Operation,Eqp,QTY\n");
            foreach (var p in rows)
                sb.AppendLine($"{p.Lot},{p.Operation},{p.Eqp},{p.QTY.ToString("R", CultureInfo.InvariantCulture)}");
            File.WriteAllText(FolderDir.Data.GetFilePath("Parameter_ProcessTime.csv"), sb.ToString(), new UTF8Encoding(true));

            // Set_Operation 的行序 = 加工順序（Dataload 的 RoutePrecedence 依檔案行序索引）
            WriteSetCsv("Set_Lot.csv", rows.Select(p => p.Lot));
            WriteSetCsv("Set_Operation.csv", rows.Select(p => p.Operation));
            WriteSetCsv("Set_Eqp.csv", rows.Select(p => p.Eqp));
            Logging.Info($"[IO] 已產生 Data/Parameter_ProcessTime.csv（{rows.Count} 筆）+ Set_Lot / Set_Operation / Set_Eqp");
        }

        static void WriteSetCsv(string fileName, IEnumerable<string> members)
            => File.WriteAllLines(FolderDir.Data.GetFilePath(fileName), members.Distinct(), new UTF8Encoding(true));

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
            engine.BuildBVs<VariableB_Assign>(data.LotSet, data.OperationSet, data.EqpSet);
            engine.BuildBVs<VariableB_Precede>(data.LotSet, data.OperationSet, data.LotSet, data.OperationSet);
            engine.BuildCVs<VariableX_Start>(data.LotSet, data.OperationSet);
            engine.BuildCVs<VariableX_Complete>(data.LotSet, data.OperationSet);
            engine.BuildCVs<VariableX_Makespan>(data.Scope);

            new ObjectiveFunction(data.Scope, engine).Build();
            new Constraint_AssignOneEqp(data.LotSet, data.OperationSet, data.EqpSet, engine).Build();
            new Constraint_CompleteDef(data.LotSet, data.OperationSet, data.EqpSet, data.parameter_ProcessTime, engine).Build();
            new Constraint_RoutePrecedence(data.LotSet, data.OperationSet, engine).Build();
            new Constraint_NoOverlap(data.LotSet, data.OperationSet, data.EqpSet, data.BigM, engine).Build();
            new Constraint_MakespanDef(data.LotSet, data.OperationSet, data.Scope, engine).Build();
            new Constraint_MakespanWindow(data.Scope, data.MakespanFloor, data.MakespanDeadline, engine).Build();
            new Constraint_MakespanTargetSoft(data.Scope, data.SoftMakespanTarget, data.MakespanPenalty, engine).Build();
        }

        // 模型 C：模型 A + 一條保證 infeasible 的 Makespan 上限（= 理論下界 - 1），示範 IIS 衝突分析。
        static void BuildModelC(Dataload data, OptEngine engine)
        {
            new VariableCreate(data, engine).Build();
            new BuildModel(data, engine).Build();
            
            // 這條 Makespan 上限限制式會讓模型變成 infeasible，求解器會產生 IIS 衝突分析報告。
            new Constraint_MakespanInfeasibleCap(data.Scope, data.InfeasibleMakespanCap, engine).Build();
        }
        
        // ── 求解單一模型並驗證解 ─────────────────────────────────────────
        static void SolveAndVerify(string name, Dataload data, Action<Dataload, OptEngine> buildModel)
        {
            using var model = new OptModel(name)
                .UseConfig(() => new CplexConfig { epGap = 1e-4, timeLimit = 30, enableLog = false })
                .AddModel(engine => buildModel(data, engine))
                .OnSolved(engine =>
                {
                    data.WriteSolution(engine);                 // 印排程 + 逐條 constraint 代回驗證
                    WriteSolutionCsv(engine);                   // 解寫回 CSV（foundation ISolutionSink）
                });

            bool ok = model.Execute();
            Logging.Info($"[{name}] success={ok}, status={model.optEngine.Status}");
        }

        // 求解預期 infeasible 的模型：Solve 偵測到 Infeasible 會自動跑 conflict 分析並寫 IISs/{名稱}_IIS_{時間戳}.ilp，
        // 這裡把 IIS 衝突 constraint 名單印出（GetConflictConstraints 直接回傳 Solve 已算好的結果，不重跑 RefineConflict）。
        static void SolveExpectInfeasible(string name, Dataload data, Action<Dataload, OptEngine> buildModel)
        {
            using var model = new OptModel(name)
                .UseConfig(() => new CplexConfig { epGap = 1e-4, timeLimit = 30, enableLog = false })
                .AddModel(engine => buildModel(data, engine));

            bool ok = model.Execute();
            Logging.Info($"[{name}] success={ok}, status={model.optEngine.Status}（預期 Infeasible）");
            var conflicts = model.optEngine.GetConflictConstraints();
            Logging.Info($"[{name}] IIS 衝突 constraint 共 {conflicts.Count} 條：");
            foreach (var c in conflicts)
                Logging.Info($"[{name}]   - {c}");
        }

        // 解輸出＝CSV：foundation CsvSolutionSink → Solution/{變數型別}.csv（帶表頭，可被 CsvCtrl.BuildParameter 讀回）。
        // 換成 DB 輸出只需改用 OracleSolutionSink，求解端 code 不動。
        static void WriteSolutionCsv(OptEngine engine)
        {
            var sink = new CsvSolutionSink();
            sink.WriteSolution<VariableB_Assign>(engine);    // 每 (lot,op) 指派哪台機
            sink.WriteSolution<VariableX_Start>(engine);     // 各作業開始時間
            sink.WriteSolution<VariableX_Complete>(engine);  // 各作業完成時間
        }

        // ── 交叉實驗：兩個模型 × 三組 solver 設定，六次執行逐行明寫（不繞 loop）──
        static void RunCrossExperiment(Dataload data)
        {
            var exp = new Experiment("fjsp-cross", "兩個模型 × 三組 solver 設定的交叉實驗");

            // 三組設定：對 CplexConfig 的調參委派
            Action<CplexConfig> balanced = c => { c.Emphasis = 0; c.timeLimit = 180; };
            Action<CplexConfig> feasible = c => { c.Emphasis = 1; c.timeLimit = 180; };
            Action<CplexConfig> optimal = c => { c.Emphasis = 2; c.timeLimit = 180; };

            // 模型A 跑三種實驗
            exp.AddTrial(RunTrial(data, "A-Aggregated | balanced", BuildModelA, balanced));
            exp.AddTrial(RunTrial(data, "A-Aggregated | feasible", BuildModelA, feasible));
            exp.AddTrial(RunTrial(data, "A-Aggregated | optimal", BuildModelA, optimal));

            // 模型B 跑三種實驗
            exp.AddTrial(RunTrial(data, "B-Explicit | balanced", BuildModelB, balanced));
            exp.AddTrial(RunTrial(data, "B-Explicit | feasible", BuildModelB, feasible));
            exp.AddTrial(RunTrial(data, "B-Explicit | optimal", BuildModelB, optimal));

            // 模型C（保證 infeasible）也入實驗：看 Infeasible trial 在實驗紀錄裡如何呈現（obj=NaN、status=Infeasible）；
            // 每次 solve 都會自動跑 conflict 分析並各寫一份 IIS 檔
            exp.AddTrial(RunTrial(data, "C-InfeasibleIIS | balanced", BuildModelC, balanced));

            // Save() 會把同名實驗的舊 Trial 併進來（累積調校歷史），故檔案列數可能多於本次。
            // 收斂軌跡由框架 TrajectoryCsvWriter 輸出成 {name}-trajectory.csv（1 列/收斂點），可直接畫收斂曲線。
            exp.Save();
            Logging.Info($"[Cross] 本次 7 Trial（A×3 + B×3 + C×1）→ Experiments/{exp.Name}.csv / .json / -trajectory.csv");
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
