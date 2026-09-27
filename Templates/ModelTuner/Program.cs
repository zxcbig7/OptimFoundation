using ModelTuner;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

// ModelTuner：吃既有模型檔（.sav / .lp / .mps）的 Phase 3 tuning 殼。
// 模型不是 C# 組出來的，而是 Instances/ 裡凍結的檔案；本檔可寫的只有 productionBaseline 與 exp 區塊（見 README 白名單）。
//   dotnet run → production：productionBaseline 解 Instances/tune 每個檔
//   dotnet run -- lock → S0 凍結：模型檔指紋寫進 instances.lock
//   dotnet run -- exp <N> → 跑下方 R<N> 區塊，完成後自動 archive 到 Experiments/ 並產 facts
//   dotnet run -- holdout <N> <label> → R<N> 的 baseline 與 champion 在 holdout seeds / instances 重跑
//   dotnet run -- facts <N> → 從 archive 重產 TUNING-FACTS 與彙總
//   dotnet run -- cplex-tune [秒] → S2.5：CPLEX 內建 tuning tool 吃全部 tune instance

// ── 1. 材料：凍結的模型檔與量測設定 ──
const string ProjectName = "ModelTuner";
int[] tuningSeeds = { 11, 22, 33, 44, 55 };
int[] holdoutSeeds = { 66, 77, 88 };

var workspace = TunerWorkspace.Open(ProjectName);
string mode = args.Length > 0 ? args[0] : "production";

// ── 2. production baseline：整個專案只有這一顆，experiment 一律從它 Clone()，promotion 只寫回這裡 ──
// 停止契約 / 環境契約以 TuningHistory.md 契約區塊為準；變更任一項 = 重跑 S1 sizing 與 R0
// baseline provenance:
//   來源 experiment: initial
//   champion Trial: initial
//   promotion 日期: -
//   diff: -
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
        // 校準輪：只有 baseline，變的只有 seed（runner 展開 5 個 tuning seed，label 自動加 -s<seed>）
        round = new TuningRound(workspace, roundNo, "R0 校準：baseline × 5 seeds，量 θ 與瓶頸剖面", tuningSeeds, holdoutSeeds)
            .Add("r0-baseline", productionBaseline.Clone());
    }
    // R1 範本：複製成新的 else if 區塊；一輪只改一顆搜尋策略旋鈕。
    // 跑完後把每顆 config 的完整有效值改寫成字面值 initializer（baseline 之後會被 promotion 改掉），之後 NEVER 改寫或刪除。
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

// ── 4. production：promotion 後的出口驗證也走這裡 ──
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
    using var project = new OptProject(OptModel.FromFile(instance.FullPath, instance.Name), $"{ProjectName}-{instance.Name}")
        .UseConfig(() => new ProjectConfig { EnableSolverLog = true, ExportSol = true })
        .UseConfig(() => productionBaseline.Clone());
    if (!project.Execute()) unsolved++;
    Logging.Info(RoundFacts.DescribeProduction(instance, project.Engine.LastMetrics));
}
return unsolved == 0 ? 0 : 1;
