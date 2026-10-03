---
title: 專案範圍與統一執行路徑（OptProject 升格為專案，Solve / Experiment 共用 OptEngine.RunModel）
status: implementing
created: 2026-09-28
updated: 2026-10-01
modules: [core, cplex, experiments, templates, docs, ai-modeling]
related: 2026-09-18-model-source-duality-and-profile.md
---

# 專案範圍與統一執行路徑

## Summary

`OptProject` 從「單次求解的執行器」升格為「專案」：持有名稱、FolderDir 全部資料夾、log 與保留期。專案有兩種用法，底層共用同一條 internal 執行路徑 `OptEngine.RunModel`（一個 `OptModel` 配一組 `CplexConfig` 在全新 engine 上跑一次，記成 `Trial`）：

- `project.Solve(model, config)`：跑 1 次，保留 engine 交出解，也留下 Trial 紀錄
- `project.Experiment(name)`：N 個 model × M 組 config 交叉比較，只留 Trial

公開型別只有 `OptProject`、`OptExperiment`、`OptEngine`（加上既有的 `OptModel` 與 `CplexConfig`），不另設 runner 型別。`ProjectConfig` 瘦身：專案身分（名稱、保留期）移到 `OptProject` 建構子，`ProjectConfig` 只留輸出開關。設定命名統一：設定類別一律 `XxxConfig`，吃設定的方法一律 `LoadConfig`。

## 目標架構

```
OptProject：專案（名稱、FolderDir 全部資料夾、log、保留期）
├─ Solve(model, config)：跑 1 次 → 保留 engine 交出解 + 留 Trial
└─ Experiment(name)：N model × M config → 只留 Trial

兩者底層都是 OptEngine.RunModel：
new OptEngine(config.Clone(), projectConfig.Clone()) → Build → OptModel.ApplyTo → beforeSolve → Trial.Capture(Solve)
```

分界規則：會改變模型長相的放在 `OptModel`（model pipeline）；只影響怎麼跑、記錄什麼的放在 run pipeline（`OptEngine.RunModel` 與呼叫它的 Solve / Experiment）。`RunModel` 不區分模型來源（pipeline 建構或匯入後追加），來源二態由 `OptModel` 負責。

## Motivation / Why

以 commit `cf8c92a` 為準：

1. **「跑一次」寫了三份，已經漂移**。`OptProject.ExecuteCore`、`OptExperiment.RunCore`、`Templates/ModelTuner/Tuning/TuningRound.Warmup` 各自 `new OptEngine → Build → ApplyTo → Solve`。專案名規則不同（OptProject：ctor → cfg → model 名；OptExperiment：cfg → 實驗名，`OptExperiment.cs:130` 註解卻寫一致）；config 一邊 Clone 一邊不 Clone；OptProject 沒有求解前 hook，ModelInspector 只好把 `EnableTrajectory` 塞進 `AddVariables`（`Templates/ModelInspector/Program.cs:47-49`）。
2. **「Project」一詞兩義**。`ProjectConfig` 混了專案身分（ProjectName、RetentionDays）與單次輸出開關（EnableSolverLog、Export*）；RetentionDays 只有 OptProject 讀；`ExportIIS` 沒有任何讀取點。
3. **Experiment 不知道自己屬於哪個專案**。6 個 template 的 exp 分支都沒把 ProjectConfig 傳給 OptExperiment，靠手寫 `Logging.SetLogFileName("X_exp")` 補；ModelTuner 另寫 `TunerWorkspace.ProjectName`。
4. **正式求解會刪實驗紀錄**。OptProject 開跑時清 `FolderDir._outputs`，其中含 `Experiment/`（`FolderDir.cs:34`）。
5. **Solve 沒有紀錄**。`$FW/MILP Model/CLAUDE.md` 要求 tuning 閉環到 production 驗證，但正式求解不產 Trial，ModelTuner 只能自己撈 `Engine.LastMetrics`（`Templates/ModelTuner/Program.cs:111`）。
6. **資料夾延遲建立**。使用者要求專案建立時 FolderDir 全部資料夾一次建好，不論有沒有用到。

## Scope

### In Scope

- Core：`ProjectConfig` 瘦身（移除 ProjectName、RetentionDays，只留輸出開關）、`EngineBase.Configuration(ISolverConfig)` 改名 `LoadConfig`、`FolderDir.CreateAll()`、保留期清理排除 `Experiment/`、`Trial.Capture` 新增 `captureTrajectory` 參數
- Cplex：`OptProject` 改為專案並直接提供 `Solve`；`OptEngine` 新增 internal `RunModel`（唯一執行路徑）；`OptExperiment` 改由 `project.Experiment()` 建立並走 `RunModel`；`OptProject` / `OptExperiment` 以 `LoadConfig(ProjectConfig)` 吃設定
- Solve 留 Trial 並落檔（格式同實驗）
- Experiment 可關收斂軌跡（promotion / hold-out 驗證用）
- Solve 的 `beforeSolve` hook
- 7 個 templates 與 tests 遷移；刪除舊 API，不留 `[Obsolete]`
- 文件同步：CodeMap、`docs/`、template README、sibling `AI-Modeling` 的 coding / tuning skills 與 reference

### Out of Scope

- 模型來源拆型別（`BuiltModel` / `ImportedModel`）→ 2026-09-18 spec；`RunModel` 只吃 `OptModel`，之後拆型別不影響本設計
- 多執行緒同時跑多個專案（`Logging` / `FolderDir` 為 process 共用 static）；依序使用多個專案可以
- 原生 warm-up 支援：ModelTuner 的 Warmup 暫留 template 自己用 `OptEngine`
- `Experiment`（紀錄）與 `OptExperiment`（執行器）命名整併
- MIP start 從 `OptModel` 搬到 run（見 Open Questions）
- `ExportIIS` 接線
- FolderDir 路徑可設定（仍固定在執行檔目錄）

## User Stories / Use Cases

1. As 建模者，I want 在 `Program.cs` 建一個 `OptProject`，正式求解與實驗都從它出發，so that 名稱、log、輸出位置只設一次。
2. As tuning 執行者（人或 AI），I want 正式求解也留下與實驗同格式的 Trial，so that promotion 後的 production 驗證能直接和 experiment 的 Trial 對照。
3. As tuning 執行者，I want 關掉 experiment 的收斂軌跡，so that promotion / hold-out 驗證不受 callback 改變搜尋路徑的偏差影響。
4. As 框架維護者，I want 「跑一次」只有一份實作，so that Solve 與 Experiment 的行為不再漂移。
5. As 使用者，I want 專案建立時所有資料夾都在，so that 一眼看得到輸出會出現在哪。

## Acceptance Criteria

- [x] AC1：`new OptProject("X")` 後 FolderDir 7 個資料夾（Input / Output / Log / Model / IIS / Solution / Experiment）全部存在，不論後續是否用到
- [x] AC2：建立 OptProject 時執行保留期清理；超過天數的 Log / Model / Solution / IIS / Output 舊檔被刪，`Experiment/` 與 `Input/` 的舊檔保留；`retentionDays <= 0` 不清理
- [x] AC3：log 檔名只由框架決定：建立 OptProject 與 `project.Solve(...)` → `{Project}_*.txt`；`project.Experiment(name)` 建立時 → `{Project}-{name}_exp_*.txt`；templates 不再呼叫 `Logging.SetLogFileName`
- [x] AC4：Solve 與 Experiment 都走 `OptEngine.RunModel`（Build → ApplyTo → beforeSolve → Trial.Capture），建 engine 前都 Clone `CplexConfig` 與 `ProjectConfig`；不另設 runner 型別（`OptSolve` / `OptRun` 不存在）
- [x] AC5：`project.Solve(...)` 後 `project.Trial` 非 null，`Label = "solve"`、`Model = model.Name`、`TrialId = 1`，Metrics 有值且未開收斂軌跡；非 Optimal / Feasible 時回傳 false、`IsSuccess = false` 但 Trial 照記
- [x] AC6：每次 Solve 把 Trial 以實驗格式寫到 `Experiment/{Project}-{Model}-solve.csv` 與 `-meta.csv`（同名覆寫，與實驗語意一致）
- [x] AC7：`CaptureTrajectory(false)` 的 experiment，全部 Trial 的收斂軌跡為空；未呼叫時維持開啟
- [x] AC8：`beforeSolve` 在 `ApplyTo` 之後、Solve 之前執行，`onSolved` 只在成功後執行；ModelInspector 改用 `beforeSolve`，不再把 `EnableTrajectory` 塞進 `AddVariables`
- [x] AC9：`OutputOptions` 型別不存在；`ProjectConfig` 只含輸出開關、不進 `ConfigSnapshot`；吃設定的公開方法一律叫 `LoadConfig`（`UseOutput` / `UseConfig` / `Configuration` 不存在，由 `RunnerSymmetryTests.ConfigConsumers_AreAllNamedLoadConfig` 守住）
- [x] AC10：實驗輸出檔名為 `Experiment/{Project}-{name}.csv`（+ `-meta.csv` / `-trajectory.csv`）；ModelTuner 各輪檔名與遷移前一致
- [ ] AC11：`dotnet build OptimFoundation.sln` 0 error；tests 全綠；templates、docs、AI-Modeling 搜不到 `OutputOptions`、`UseOutput(`、`FromFile(`、`.ImportModel(`（CPLEX 原生呼叫除外）、`ExportModelFile`、`CreateGreatEqual`、`CreateLeSoft` / `CreateGeSoft` / `CreateEqSoft`、`DBCtrl`、`UseConfig(`、`.Configuration(`、`new OptProject(model`、`new OptExperiment(`，templates 除 ModelTuner 的 `CplexTuner`（TuneParam 不走 Solve / Experiment，保留自接 log）外搜不到 `SetLogFileName(`——OptimFoundation 內已達成，AI-Modeling 待辦

## Module Interactions

- **Core**
  - `Infrastructure/ProjectConfig.cs`：移除 `ProjectName` / `RetentionDays`，只留輸出開關
  - `Infrastructure/FolderDir.cs`：新增 `CreateAll()`；保留期清理清單移除 `Experiment`
  - `Experiments/Experiment.cs`：`Trial.Capture` 新增 `captureTrajectory`
  - `Infrastructure/Logging.cs`：只改註解（誰會呼叫 `SetLogFileName`）
- **Cplex**
  - `OptProject.cs`：改寫為專案；`LoadConfig`、`Solve`、最近一次 Solve 的結果屬性、`Dispose`、`Experiment`
  - `OptEngine.cs`：新增 internal `RunModel`、`ModelApplyElapsed`；建構子仍吃 `ProjectConfig`（參數改名 `projectConfig`）
  - `OptEngine.Configuration.cs`：`Configuration` → `LoadConfig`；log tag 維持 `[Project Setting]`
  - `OptExperiment.cs`：建構子改 internal；新增 `LoadConfig`、`CaptureTrajectory`、`Name` / `FullName`；每格走 `RunModel`
- **Templates**：Tutorial、TSP_MultiDimSet、Sudoku_SHC279、RosteringProblem、FJSP_BASIC_BRICK、ModelInspector、ModelTuner
- **tests**：ExperimentIntegrationTests、ModelImportIntegrationTests、ModelStatsIntegrationTests、OptEngineIntegrationTests、SolutionPipelineIntegrationTests、SolverParamCoverageTests、SolverParamValueMatrixTests、RunnerSymmetryTests；新增 ProjectScopeIntegrationTests
- **docs**：`CodeMap.md`、`docs/architecture-building-blocks.md`、`docs/index.html`、`docs/tutorial-optmodel-callbacks.html`、`README.md`、`CLAUDE.md`、`Templates/*/README.md`
- **AI-Modeling**：`.claude/skills/coding/`、`.agents/skills/coding/`、tuning skills、`.claude/reference/CodeMap.md` 等引用舊 API 的檔案

呼叫流程：

```
Program.cs
└─ new OptProject(name) → SetLogFileName(name) → FolderDir.CreateAll → PurgeAllOutputs
   ├─ .Solve(model, config, onSolved, beforeSolve)
   │    → new OptEngine(config.Clone(), projectConfig.Clone()) → RunModel("solve", trajectory off)
   │    → Experiment.Save({Project}-{Model}-solve) → onSolved(engine)（成功才跑）
   └─ .Experiment(name) → SetLogFileName({Project}-{name}_exp) → OptExperiment.Run
        → 每格 new OptEngine(...) → RunModel(label) → Experiment.Save({Project}-{name})
```

## API Design

```csharp
namespace OptimFoundation.Core
{
    // ProjectConfig 瘦身：只剩輸出開關（ProjectName / RetentionDays 移到 OptProject 建構子）
    public sealed class ProjectConfig
    {
        public bool EnableSolverLog { get; set; } = true;
        public bool ExportLP { get; set; }
        public bool ExportMPS { get; set; }
        public bool ExportSol { get; set; }
        public bool ExportIIS { get; set; } // 目前未接線
        public ProjectConfig Clone();
        public static ProjectConfig Quiet(); // EnableSolverLog = false，其餘關
    }

    public class FolderDir
    {
        public static void CreateAll(); // 新增
        public static int PurgeAllOutputs(int retentionDays); // 不再清 Experiment/
    }

    public sealed class Trial
    {
        public static Trial Capture(ISolverEngine engine, string label, Func<bool> solveAction,
            string note = null, bool captureTrajectory = true); // 新增 captureTrajectory
    }
}

namespace OptimFoundation.Cplex
{
    public sealed class OptProject : IDisposable
    {
        public OptProject(string name, int retentionDays = 30);
        public string Name { get; }
        public int RetentionDays { get; }

        // 正式求解；LoadConfig 只影響 Solve，實驗有自己的 LoadConfig
        public OptProject LoadConfig(ProjectConfig config); // 預設 new ProjectConfig()
        public bool Solve(OptModel model, CplexConfig config,
            Action<OptEngine> onSolved = null, Action<OptEngine> beforeSolve = null);

        // 最近一次 Solve 的結果；下一次 Solve 或 Dispose 時釋放前一個 engine
        public OptEngine Engine { get; }
        public bool IsSuccess { get; }
        public Trial Trial { get; }
        public TimeSpan TotalElapsed { get; }
        public TimeSpan BuildModelElapsed { get; }
        public void Dispose();

        public OptExperiment Experiment(string name, string description = null);
    }

    public sealed class OptExperiment
    {
        public string Name { get; } // 使用者給的實驗名
        public string FullName { get; } // {Project}-{Name}，決定輸出檔名
        public OptExperiment LoadConfig(ProjectConfig config); // 預設 ProjectConfig.Quiet()
        public OptExperiment CaptureTrajectory(bool enabled); // 預設 true
        public OptExperiment AddModel(OptModel model);
        public OptExperiment AddConfig(string label, CplexConfig config);
        public OptExperiment AddTrial(OptModel model, string label, CplexConfig config);
        public Experiment Run();
    }

    public partial class OptEngine
    {
        public OptEngine(CplexConfig config, ProjectConfig projectConfig);
        public override void LoadConfig(ISolverConfig cfg); // 原 Configuration(ISolverConfig)

        // Solve 與 Experiment 共用的唯一執行路徑；呼叫端負責建 engine 與 Dispose
        internal Trial RunModel(OptModel model, string label, bool captureTrajectory,
            Action<OptEngine> beforeSolve, out bool solved);
        internal TimeSpan ModelApplyElapsed { get; }
    }
}
```

`Program.cs` 遷移後（Tutorial）：

```csharp
using var project = new OptProject("Tutorial");

if (isExperiment)
{
    project.Experiment("tuning-r1", "同一模型 × 三組 MIP emphasis 對照")
        .AddModel(model)
        .AddConfig("balanced", balanced)
        .AddConfig("feasible-first", feasibleFirst)
        .AddConfig("optimal-first", optimalFirst)
        .Run();
    return 0;
}

project.LoadConfig(projectConfig);
bool solved = project.Solve(model, productionBaseline,
    onSolved: engine => TutorialSolution.ReadAndValidate(engine, data).Print());
return solved ? 0 : 1;
```

### Breaking Changes

| 舊 | 新 |
| --- | --- |
| `new OptProject(model, projectName, retentionDays)` + `Execute()` | `new OptProject(name, retentionDays).Solve(model, config)` |
| `OptProject.UseConfig(Func<ProjectConfig>)` | 名稱與保留期進 `OptProject` 建構子；輸出開關 `OptProject.LoadConfig(ProjectConfig)` |
| `OptProject.UseConfig(Func<CplexConfig>)` | `Solve(model, config)` 參數 |
| `OptProject.OnSolved(handler)` | `Solve(..., onSolved: handler)` 參數 |
| `OptProject.Execute()` | `OptProject.Solve(...)`（回傳 bool） |
| `new OptExperiment(name, description)` | `project.Experiment(name, description)` |
| `OptExperiment.UseConfig(Func<ProjectConfig>)` | `OptExperiment.LoadConfig(ProjectConfig)` |
| `ProjectConfig`（含 ProjectName、RetentionDays） | `ProjectConfig`（只留輸出開關） |
| `new OptEngine(config, ProjectConfig)` + `EngineBase.Configuration(ISolverConfig)` | `new OptEngine(config, ProjectConfig)`（型別不變）+ `EngineBase.LoadConfig(ISolverConfig)` |
| 實驗檔名 = 實驗名 | `{Project}-{實驗名}` |
| 保留期清理在 `Execute()` 開頭 | 在 `new OptProject(...)` 時 |

## Data Model

無 DB。檔案配置：

| 路徑 | 產生者 | 保留期清理 |
| --- | --- | --- |
| `Experiment/{Project}-trial.csv` / `-meta.csv` / `-summary.csv` / `-trajectory.csv`（累積檔，修訂 12） | `OptExperiment.Run`（Experiment 欄 = 實驗名）、`OptProject.Solve`（Experiment 欄 = `solve`，不寫 `-summary.csv`） | 不清 |
| `Log/{Project}_{ts}.txt` | `new OptProject`、`OptProject.Solve` | 清 |
| `Log/{Project}-{name}_exp_{ts}.txt` | `project.Experiment(name)` | 清 |
| `Model/`、`Solution/`、`IIS/`、`Output/` | 依 ProjectConfig / solution sink | 清 |

engine 名稱（LP / MPS / Sol 檔名前綴）：Solve → `{Project}`；Experiment 單一模型 → `{Project}-{label}`，多模型 → `{Project}-{Model}-{label}`（與現行一致）。

Solve 的 Trial：`ExperimentId` = 開跑時間 `yyyyMMdd-HHmmss`（同一秒再開一批加 `-2`，修訂 12）、`TrialId = 1`、`Label = "solve"`、`Model = model.Name`。

## Edge Cases & Error Handling

- 專案名空白 → `PROJECT_INVALID`（reason=`name_is_empty`）Error Log 後 throw；含非法檔名字元 → 同 code（reason=`invalid_file_name_char`）
- 資料夾建立失敗（權限）→ `PROJECT_INIT_FAILED` Error Log 後 rethrow
- `Solve` 的 model / config 為 null → `SOLVE_INVALID`；`LoadConfig(null)` → `PROJECT_INVALID` / `EXPERIMENT_INVALID`；Error Log 後 throw
- 同一專案重複 Solve → 先 Dispose 前一個 engine；`Engine` / `IsSuccess` / `Trial` 永遠指最近一次，開跑時先清成 false / null
- Solve 例外 → 公開邊界 `SOLVE_EXECUTION_FAILED` 記一次後原樣 rethrow；engine 在 `Build()` 前就指派給 `project.Engine`，ModelInspector 仍能在例外後讀取
- 先建 Experiment、中途做 Solve、再 `Run()` → `Run()` 開頭再設一次實驗 log 名（同名 no-op）；Solve 開頭切回專案 log
- 依序使用兩個 OptProject → 各自設 log 名，檔名以專案名區隔，可以正常運作；多執行緒同時跑不支援
- 收斂軌跡的 MIPInfoCallback 會改變搜尋路徑且傾向變慢（2026-09-28 實測）→ Solve 一律不開（要開自己在 beforeSolve 呼叫 `EnableTrajectory`）；Experiment 預設開、可關
- `ExportIIS` 目前沒有讀取點 → 原樣保留在 ProjectConfig，XML doc 註明未接線

## Non-Functional Requirements

- **Performance**：Solve 不開軌跡、不多跑任何 solve；`CreateAll` 為 7 次 idempotent `CreateDirectory`
- **Observability**：建立專案時印 `[Project] Name=... RetentionDays=... Purged=N`；OptEngine 的輸出設定 log tag 維持 `[Project Setting]`；主動錯誤遵守 CLAUDE.md 的 Error Log 規則
- **Compatibility**：直接 breaking，不留 `[Obsolete]`；.NET 8；Generators 不受影響

## Open Questions

使用者指示直接動工，以下先依建議值實作，之後可調：

- [x] OQ1 Solve 的 Trial 落檔方式 → 採「每次覆寫 `Experiment/{Project}-{Model}-solve.*`」，沿用既有 writer 與同名覆寫語意；替代方案（append 一列累積 production 歷史）需要新 writer 模式，另案
- [ ] OQ2 MIP start（OptModel 第 4 段）屬 model 還是 run？現況放 model，實驗要比「有 / 無 MIP start」需建兩個 OptModel。本次不動
- [ ] OQ3 `ExportIIS` 要接線還是刪除？本次原樣搬移

## Implementation Plan

使用者指示直接動工，stub 與實作合併進行（新型別先建、build 通過後再填實作）。

### Stub 階段（先做）

- [x] 確認 CodeMap 與 commit `cf8c92a` 同步（全域規則：stub 前）
- [x] Core：`OutputOptions.cs`、`FolderDir.CreateAll()` 簽名、`Trial.Capture` 新參數
- [x] Cplex：`OptProject` 新建構子與 `Solve()` / `Experiment()`；`OptExperiment` 新成員
- [x] `dotnet build OptimFoundation.sln` 確認型別連得起來

### 逐層實作

- [x] Core：OutputOptions、CreateAll、清理排除 Experiment/、captureTrajectory（AC1、AC2、AC7）
- [x] Cplex：OptEngine 改吃 OutputOptions；`RunModel`（AC4）
- [x] OptProject 專案化 + Solve + Solve 落檔（AC3、AC5、AC6、AC8）
- [x] OptExperiment 掛到 project（AC3、AC7、AC10）
- [x] 遷移 tests 與 7 個 templates，新增 ProjectScopeIntegrationTests；刪除 ProjectConfig 與舊成員（AC9）
- [x] 文件：CodeMap、docs、README、CLAUDE.md、Logging / FolderDir 註解
- [ ] AI-Modeling 同步：DLL 更新 + `Template/Program.cs` + `Projects/*` + coding / tuning skills，同一批做（該 repo 有未 commit 的 api-guide 修改，待使用者決定時機）
- [x] 驗收：build、test、stale scan，派 verifier

### 實作中的決定與發現

- 修訂（使用者要求）：第一版另設 public `OptSolve` 與 internal `OptRun`；使用者要求公開型別只保留 `OptProject` / `OptExperiment` / `OptEngine`，改為 `OptProject.Solve(...)` 直接回傳 bool、結果屬性指最近一次，共用執行路徑搬進 `OptEngine.RunModel`
- 修訂 2（使用者要求）：設定命名統一——設定類別一律 `XxxConfig`、吃設定的方法一律 `LoadConfig`。`OutputOptions` 改回 `ProjectConfig`（內容仍只有輸出開關）、`UseOutput` → `LoadConfig`、`EngineBase.Configuration(ISolverConfig)` → `LoadConfig`，log tag 回到 `[Project Setting]`。上方 Stub / 逐層實作清單中的 `OutputOptions` 為修訂前紀錄。ModelInspector 的 `InspectionOptions` 是命令列參數解析、不屬於框架設定，保留原名
- 修訂 3（使用者要求）：`OptModel.FromFile(fileName, name)` 改名 `OptModel.ReadModel(fileName, name)`，與 `ReadSolution` 同一組讀檔命名；簽名與行為不變。engine 層的 `OptEngine.ImportModel` 與 `ExportModelFile` 成對，保留原名（已由修訂 4 推翻）
- 修訂 4（使用者要求，命名盤點）：`OptEngine.ImportModel` → `ReadModel`、`ExportModelFile` → `ExportModel`（CPLEX 原生的 `Cplex.ImportModel` / `ExportModel` 不動；event code `MODEL_IMPORT_*` 不動）；`CreateGreatEqual` → `CreateGreaterEqual`（含 Thread 版）；`CreateLeSoft` / `CreateGeSoft` / `CreateEqSoft` → `CreateLessEqualSoft` / `CreateGreaterEqualSoft` / `CreateEqualSoft`；`VarSetsReset` → `ResetVarSets`；`VariableMerge` → `MergeVariables`；`DBCtrlBase` / `OracleDBCtrl` → `DbCtrlBase` / `OracleDbCtrl`。另修 `VariablePrefixNaming` 舊前綴 `VariableX_` / `VariableY_` 相容轉換沒把 Replace 結果接回的 bug，補 2 個 BuildVars 測試
- 修訂 5（使用者要求）：5 個建模範本（Tutorial、TSP_MultiDimSet、Sudoku_SHC279、RosteringProblem、FJSP_BASIC_BRICK）統一 CLI——預設正式求解、`-- exp`、新增 `-- read-model <file>`（`OptModel.ReadModel` → `project.Solve`，與正式求解共用 `projectConfig` / `productionBaseline`，不讀 CSV），`-- import` 改名 `-- import-data`。設定區塊移到材料之前（`0. 設定`），read-model 分支才能在載入 CSV 之前返回；Tutorial 移除未接線的 `model2`。Tutorial 實跑：正式求解與 read-model 讀回同一份 LP 皆 Optimal、ObjVal=510
- 修訂 6（使用者要求）：read-model 不應綁死「直接求解」。範本 CLI 改為兩軸自由組合——模型來源（預設 CSV 建構 / `read-model <file>`）× 執行方式（預設正式求解 / `exp`），例 `-- read-model <file> exp`。`Program.cs` 結構改為 `0. 設定` → `1. 模型來源`（`OptModel.ReadModel` 或 `BuildModel(data)`，canonical 建構抽成 static method）→ `2. 環境`（只拿 `model`）。read-model 的實驗名加模型名，避免覆寫 canonical 同一輪紀錄；正式求解沒有資料時不跑解驗證；FJSP 的 Phase 3 demo variant 需要資料，read-model 時只跑讀入的模型；Rostering 的 .sav 匯出只掛在 canonical。Tutorial 四種組合、TSP 預設 / read-model 實跑皆 Optimal 且目標值一致
- 修訂 7（使用者要求）：實驗輸出每一格都有值。主表缺值改寫標記：`off`（軌跡沒開）、`none`（有收集但沒發生：沒找到可行解、界從未變動、與基準無差異、沒有備註、求解前就失敗）、`n/a`（求解器不提供）、`baseline`（基準列的 `DiffKnobs`）；`BasedOn` 在基準列指向自己；`Seed` 改寫實際使用的種子（`SolveMetrics.Seed` 由 CPLEX `RandomSeed` 讀回，沒明設時是預設值）。軌跡開且有解時 engine 補一個求解結束的終點（presolve / root 就解完時 callback 一點都記不到，`TFeasMs` 因此是上界），新增 `SolveMetrics.TrajectoryEnabled` 分辨「沒開」與「開了但沒記到」。`-meta.csv` 空值寫 `none`、新增 `legend` 區段，schema v3；`-trajectory.csv` 的 NaN / Infinity 改寫 `#N/A`（Excel 畫圖略過、pandas 讀成 NaN）。新增測試 `Experiment_Csv_HasNoBlankCells`
- 修訂 8（使用者要求）：主表 `DiffKnobs` 改名 `ConfigChanges`（相對基準改了哪些設定）。新增模型結構欄，**一律取自 CPLEX 模型、不用框架建模統計**：`ModelType`（LP / MILP / IP / BP）、`VarCount`（Ncols）、`BinaryVarCount`、`IntegerVarCount`、`ContinuousVarCount`（不含 semi）、`SemiContinuousVarCount`、`SemiIntegerVarCount`、`ConstraintCount`（Nrows）、`QuadraticConstraintCount`、`IndicatorConstraintCount`、`SosCount`、`LazyConstraintCount`、`UserCutCount`；`SolveMetrics` 同名欄位由 `OptEngine.SolveCore` 讀 `ReadSolverModelCounts()` 填入，`VarCount` / `ConstraintCount` 也從框架計數改成 CPLEX 計數（沒被引用的變數 CPLEX 不收）。`ModelCounts` 補 semi / QC / indicator / SOS / lazy / user cut 分項。`-meta.csv` 的 `model.*` 同步列出，schema v4。新增測試 `Experiment_Csv_RecordsSolverModelStructure`
- 修訂 9（使用者要求）：tuning 用的統計值改由框架計算，不讓 LLM 自己算。統計放 `Experiment.cs`：新增 `ConfigSummary` 與 `Experiment.Summaries`（每次讀取從 `Trials` 重算），依批次、模型、設定（label 去掉結尾 `-s<seed>`）分組，算 sgm（shift 依基準設定實際 runtime 中位數：< 60 秒取 1 秒，否則 10 秒）、PAR10（10 × TimeLimit）、θ（逐 seed 取值；runtime / t_feas 為 (max − min) / sgm，endGap 為 max − min 百分點）、endGap 平均（沒可行解記 100）、找到可行解的 trial 數，以及相對同模型基準的改善量（一律正值 = 比基準好）。runtime 非 Optimal、t_feas 沒可行解以 PAR10 計入；需要 PAR10 卻沒設 TimeLimit、seed 少於 2 個、沒開軌跡時該欄算不出來，不用實際耗時硬湊。輸出放 `ExpCsvWriter.cs`：新增 `SummaryCsvWriter`，`Save` 多寫 `-summary.csv`（算不出寫 `n/a`、基準列改善量寫 `baseline`、軌跡沒開寫 `off`）。label 含 `warmup` 的暖機 trial 不進統計，主表的基準選擇（`FindBaselineIn`）也跳過暖機。`-meta.csv` schema v5（legend 的 `n/a` / `baseline` 說明補上 summary）。ModelTuner archive 必備檔加入 `-summary.csv`。新增 `ConfigSummaryTests`（4 個，逐項對手算公式），`Experiment_Csv_HasNoBlankCells` 一併檢查 `-summary.csv`
- 修訂 10（使用者要求，2026-09-30）：推翻修訂 9 的統計指標——sgm、PAR10、θ、改善量太複雜、沒人看得懂，改成「同一個 seed 直接跟基準比大小」。新增 internal `BaselineComparer`：對手是同一批、同一模型、同一個 seed 的基準 trial，依序比有沒有找到解 → 有沒有證明最佳 → 都證明最佳比時間 → 都沒證明比 gap，都沒找到解算平手；自己求解失敗算輸，基準求解失敗或沒有同 seed 的基準算無法比較。主表在 `Seed` 後新增 `VsBaseline` 欄（`baseline` / `win` / `lose` / `tie` / `n/a`）；`ConfigSummary` 拿掉 sgm / PAR10 / θ / 改善量相關屬性，改成 `Wins` / `Losses` / `Ties` / `NotCompared`（基準列為 null，CSV 寫 `baseline`），保留各狀態數與 `FoundIncumbent`。換不換設定由使用端決定（AI-Modeling tuning 規範：一個 seed 都不能輸且至少贏 3 個；hold-out 一個都不能輸）。`-meta.csv` schema v6，legend 新增 `win` / `lose` / `tie`。ModelTuner 的 `RoundFacts` 改讀 `VsBaseline` 計數並套同一規則，移除 θ / sgm / PAR10。`ConfigSummaryTests` 改寫成 5 個比大小的測試
- 修訂 11（使用者要求，2026-10-01）：數據只能取自 CPLEX 22.1.1 .NET 函式庫提供的工具，不自己寫程式計時。`RunTimeMs` 改用 `Cplex.GetCplexTime()` 在 Solve 前後各取一次相減（原本是 Stopwatch）；軌跡 callback 的 `TimeMs` 改用 MIPInfoCallback 內建的 `GetCplexTime() − GetStartTime()`；`OptProject.TotalElapsed` / `BuildModelElapsed` 保留，改用 CPLEX 時鐘（從 `RunModel` 建好 CPLEX 模型起算）。刪除修訂 7 加的「求解結束補一個軌跡終點」——那個點不是 CPLEX 回報的；callback 一次都沒被呼叫（LP、presolve / root 就解完）時軌跡為空，`TFeasMs` / `TStallMs` / `DeltaBound` 寫 `n/a`，也不產生 `-trajectory.csv`。順帶修正 `EnableTrajectory` 註解：MIPInfoCallback 不會關閉 dynamic search，但會改變搜尋路徑、通常變慢（2026-09-28 實測）。前處理資料（縮減後大小、前處理時間）CPLEX .NET 沒有 API（沒有 presolve callback，generic callback 也沒有前處理 context），依此原則不加。`-meta.csv` schema v7
- 修訂 12（使用者要求，2026-10-01）：「檔案依功能分、不要依 run 分，不同 run 同功能放同一個 csv、多一個欄位區分，還要多記錄時間」。實驗輸出改成每個專案四個累積檔：`{Project}-trial.csv`（原主表）、`-meta.csv`、`-summary.csv`、`-trajectory.csv`；`Experiment.Save()` 一律接在檔尾，同名實驗再跑一次多一批 RunId，舊列不改不刪。四個檔最前面都加 `RecordedAt`（寫入時間，同一次 Save 四檔同值）、`Experiment`（實驗名，不含專案名；正式求解是 `solve`）、`RunId` 三欄，軌跡另加 `TrialId` 對回主表。`Experiment` 建構子改成 `(project, name, description)`，新增 `Project` / `WriteSummary`；正式求解也進同一個 `-trial.csv` / `-meta.csv`，`WriteSummary = false` 不寫彙總（新增常數 `OptProject.SolveExperimentName`）。說明檔改成每批一份（含 schema、legend），`run` 區段的 key 不再帶 RunId，拿掉 `experiment.name` / `trialCount` / `writtenAt`（與新欄位重複）。寫檔規則集中在 internal `CumulativeCsv`：新檔寫 BOM + 表頭；表頭相同接檔尾（不再寫 BOM）；表頭不同時舊檔改名 `-old-<時間>` 保留、另開新檔並 WARN；寫不進去（例：Excel 開著）改寫 `-locked-<時間>` 並 WARN；沒有列不動檔案；整個 Save 沒有 trial 只 WARN。刪除修訂 11 的「沒有軌跡就刪掉同名舊 -trajectory.csv」。RunId 改由 internal `OptProject.NextRunId()` 發，同一個 process 同一秒內再開一批加 `-2`、`-3`，累積檔才能靠 Experiment + RunId 分辨每次執行。ModelTuner 的 archive 改成把 bin 的四個累積檔複製到 `Experiments/`（archive 只能變長：現有內容不是 bin 檔開頭就拒絕；bin 被清過先從 archive 還原），facts 以 Experiment = `tuning-r<N>` 的最新 RunId 篩一輪。`-meta.csv` schema v8
- 修訂 13（使用者要求，2026-10-02）：「重新評估每一個 exp 欄位對於調參的功用，參考性不大要下架」。主表 36 欄 → 20 欄，拿掉 16 欄：`BasedOn`（同一批每列一樣；基準是誰看 `-meta.csv` 的 `baseline.label`，而且 `VsBaseline` 是跟同一個 seed 的基準比，不是跟 BasedOn 那一筆比，留著反而誤導）、`RunAt`（批次時間看 RunId、寫入時間看 RecordedAt、順序看 TrialId）、`TrajectoryPoints`（有沒有軌跡已由 `TFeasMs` / `TStallMs` / `DeltaBound` 的 `off` / `n/a` 表示，點數本身只反映 callback 被呼叫的頻率）、12 個模型結構欄 `VarCount` … `UserCutCount`（同一模型每列一樣、Phase 3 模型凍結，數量只寫在 `-meta.csv` 的 `model.<Model>.*`；`ModelType` 留在主表，因為它決定這一列怎麼讀）、`Note`（標準路徑從不填，永遠是 none）。`-trajectory.csv` 拿掉 `RunAt`（TrialId 已對回主表）。`Trial.Note` 與 `Trial.Capture` 的 `note` 參數一併刪除，不留寫了也不會輸出的 API。`-summary.csv` 與 `-meta.csv` 不動（`FoundIncumbent` 雖然等於 Optimal + Feasible，但是情境 C 的直接答案，保留）。表頭改了，舊累積檔第一次寫入時照修訂 12 的規則改名 `-old-<時間>` 保留。`-meta.csv` schema v9
- 修訂 14（使用者要求，2026-10-02）：「欄位名稱要更直觀符合人類理解」「計時分成建模+求解 & 純求解兩種」「屬性一起改成同名」。主表 `MipGap` → `Gap`（跟 `CplexConfig.MipGap` 停止門檻撞名，`ConfigChanges` 寫 `MipGap=0.01` 時分不清設定還是結果）、`RunTimeMs` → `SolveTimeMs`（純求解）、`TFeasMs` → `FirstSolutionMs`、`TStallMs` → `LastBoundChangeMs`、`DeltaBound` → `BoundChange`，並在 `Gap` 後新增 `BuildAndSolveTimeMs`（建模 + 求解 = `RunModel` 套用 OptModel 的 CPLEX 時鐘時間 + `SolveTimeMs`；不含 beforeSolve、匯出檔案、IIS；自己呼叫 `Trial.Capture` 時為 null、CSV 寫 `n/a`），主表 21 欄。`-summary.csv` 的 `NoIncumbent` / `FoundIncumbent` / `OtherStatus` → `NoSolution` / `FoundSolution` / `Failed`。`-trajectory.csv` 的 `Label` / `TimeMs` / `Objective` / `Bound` → `TrialLabel` / `ElapsedMs` / `ObjectiveValue` / `BestBound`，跟主表同一個概念用同一個名字。對應的 `SolveMetrics`、`ConfigSummary`、`ConvergencePoint` 屬性同名改掉（`Trial.Label` 不動）；逐 seed 比大小仍比 `SolveTimeMs`（建模時間不受求解參數影響，不拿來比）。ModelTuner 的 facts 表欄 `mipGap` / `runTimeMs` → `gap` / `solveTimeMs`，ModelInspector 報表標籤同步。`Status = TimeLimit`（時間到且沒有解）來自 `SolveStatus` enum，這次不動。`-meta.csv` schema v10
- ModelTuner production 迴圈每個 instance 建一個 `OptProject($"{專案}-{instance}")`：Solve 的 engine 名是 `{專案}`，同一秒內解完的 instance 會讓 `.sol` 檔名撞在一起；照舊版以 instance 區分 log 與輸出。紀錄檔名因此是 `{專案}-{instance}-{instance}-solve`
- ModelTuner 的 `CplexTuner` 走 `TuneParam`，不是 Solve 也不是 Experiment，保留自己呼叫 `SetLogFileName`
- 既有問題（非本 spec 造成）：commit `cf8c92a` 把 `FolderDir.Data` 改名 `Input`，但 templates 的 csproj 仍把 CSV 複製到輸出的 `Data/`，`dotnet run` 會找不到 `Input/*.csv`。使用者決定：移除 5 個 template csproj 的自動複製，資料 IO 一律只走 FolderDir（輸入放執行檔目錄的 `Input/`），不處理其他路徑

## References

- 診斷與決策：2026-09-28 session（Project / Experiment 切分討論）
- 相關 spec：`2026-09-18-model-source-duality-and-profile.md`（模型來源軸）、`2026-09-26-model-stats-reconciliation.md`
- `$FW/MILP Model/CLAUDE.md`：Tuning MUST 閉環到 production
- 基準 commit：`cf8c92a`（build 0 error、253 tests passed）
