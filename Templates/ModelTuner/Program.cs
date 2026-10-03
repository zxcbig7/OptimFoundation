using ModelTuner;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

// ModelTuner：讀取既有模型檔（.sav / .lp / .mps），執行 Phase 3 的參數比較實驗。
// 模型從 Instances/ 讀取，調參期間保持檔案不變；調參時只修改 productionBaseline 與 exp 區塊（範圍見 README）。
//   dotnet run → production：用 productionBaseline 求解 Instances/tune 的每個模型
//   dotnet run -- lock → S0：把模型檔的 SHA-256 雜湊值記錄到 instances.lock，供後續檢查檔案是否改變
//   dotnet run -- exp <N> → 執行下方 R<N> 實驗，完成後備存結果到 Experiments/，並產生統計報告
//   dotnet run -- holdout <N> <label> → 用保留的 seed 和模型，比較 R<N> 基準設定與 label 指定的已選設定
//   dotnet run -- facts <N> → 從備存的實驗結果重新產生 TUNING-FACTS 與統計摘要
//   dotnet run -- cplex-tune [秒] → S2.5：使用 CPLEX 內建工具，對全部調參用模型提出參數建議

// ── 1. 準備模型檔與量測設定；實驗期間保持模型檔不變 ──
const string ProjectName = "ModelTuner";
int[] tuningSeeds = { 11, 22, 33, 44, 55 };
int[] holdoutSeeds = { 66, 77, 88 };

var workspace = TunerWorkspace.Open(new OptProject(ProjectName));
string mode = args.Length > 0 ? args[0] : "production";

// ── 2. 正式設定 productionBaseline：實驗用 Clone() 複製；確認新設定較好後，只更新這裡 ──
// 求解停止條件與執行環境記錄在 TuningHistory.md；任一項變更後，須重跑 S1 規模測試與 R0 基準量測。
// 目前正式設定的來源紀錄：
//   來源實驗：initial
//   採用的 Trial：initial
//   採用日期：-
//   設定差異：-
var productionBaseline = new CplexConfig
{
    MipGap = 1e-4,
    TimeLimit = 60,
    Threads = 4,
    ParallelMode = 1,
    Seed = 11,
};

switch (mode)
{
    case "lock":
        return workspace.WriteLock();
    case "facts":
        if (args.Length < 2)
        {
            TunerCli.PrintUsage();
            return 2;
        }
        return RoundFacts.Report(workspace, RoundFacts.ResolveExperimentName(workspace, args[1]));
    case "cplex-tune":
        if (!TunerCli.TryParseTune(args, out double budgetSeconds, out int repeat)) return 2;
        if (!workspace.VerifyLock(required: true)) return 3;
        return CplexTuner.Run(workspace, productionBaseline, budgetSeconds, repeat);
}

// ── 3. 實驗：每個已執行的 R<N> 區塊永久保留（§3.3.1），當次只跑指定的那一輪 ──
if (mode is "exp" or "holdout")
{
    if (!TunerCli.TryParseRound(args, out int roundNo)) return 2;
    if (!workspace.VerifyLock(required: true)) return 3;

    TuningRound round;
    if (roundNo == 0)
    {
        // R0 — ModelTuner-tuning-r0
        // 基準量測：保持 baseline 設定不變，只更換 5 個調參用 seed；label 自動加上 -s<seed>。
        round = new TuningRound(workspace, roundNo, "R0 基準：baseline × 5 seeds，當對照組並看瓶頸剖面", tuningSeeds, holdoutSeeds)
            .Add("r0-baseline", productionBaseline.Clone());
    }
    // R1 範本：複製成新的 else if 區塊；每輪只調整一個搜尋策略參數。
    // 實驗完成後，把每組 config 的完整設定值直接寫入初始化區塊並保留，避免日後更新 productionBaseline 時連帶改變舊實驗。
    // else if (roundNo == 1)
    // {
    //     // R1 — ModelTuner-tuning-r1
    //     var baseline = productionBaseline.Clone();
    //     var emphasis = baseline.Clone();
    //     emphasis.Emphasis = 2;
    //     round = new TuningRound(workspace, roundNo, "R1：<假設一句話>", tuningSeeds, holdoutSeeds)
    //         .Add("r1-baseline", baseline)
    //         .Add("r1-Emphasis=2", emphasis);
    // }
    else
    {
        Logging.Error($"[ROUND_BLOCK_MISSING] Program.cs 沒有 R{roundNo} 區塊 | value={roundNo} reason=define_round_block_first result=aborted");
        return 2;
    }

    return mode == "exp" ? round.Run() : round.RunHoldout(args.Length > 2 ? args[2] : "");
}

// ── 4. 正式求解：採用新設定後，也從這裡執行一次確認結果 ──
if (mode != "production")
{
    Console.Error.WriteLine($"參數錯誤：未知模式 '{mode}'。");
    TunerCli.PrintUsage();
    return 2;
}
if (workspace.Tune.Count == 0)
{
    Logging.Error("[INSTANCE_SET_EMPTY] Instances/tune 沒有模型檔（.sav / .lp / .mps，可含 .gz / .bz2）| reason=no_model_file result=aborted");
    return 2;
}

if (!workspace.VerifyLock(required: false)) return 3;

int unsolved = 0;
foreach (var instance in workspace.Tune)
{
    // 每個模型使用不同的專案名稱，讓 log 與 .sol 檔名不同，避免同一秒完成時互相覆蓋。
    using var project = new OptProject($"{ProjectName}-{instance.Name}")
        .LoadConfig(new ProjectConfig { EnableSolverLog = true, ExportSol = true });
    if (!project.Solve(OptModel.ReadModel(instance.FullPath, instance.Name), productionBaseline)) unsolved++;
    Logging.Info(RoundFacts.DescribeProduction(instance, project.Trial.Metrics));
}
return unsolved == 0 ? 0 : 1;
