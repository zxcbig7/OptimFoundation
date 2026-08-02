---
title: OptModel 降格為模型定義，OptProject / OptExperiment 成為對稱的兩個執行環境
status: draft
created: 2026-08-01
updated: 2026-08-01
modules: [framework-core, framework-cplex]
depends_on: 2026-08-01-optimfoundation-dual-config.md
---

# Runner Symmetry — OptModel / OptProject / OptExperiment

## Summary

把今天的 `OptModel`（其實是 runner）改名為 `OptProject`，`OptModel` 這個名字讓給**模型定義物件**（變數 / 目標式 / 限制式，跟在哪裡跑無關）。同時新增 `OptExperiment` runner，支援 **n 組 solver 設定 × m 個模型**的交叉實驗。

一個模型定義好之後，**要在 `OptProject` 跑還是在 `OptExperiment` 跑，只是換一個 runner**。專案端不再手寫實驗迴圈，`ExperimentRunner.cs` / `RunExperiment()` 整個消失。

本規格**只動框架 solution 內**（`$OPT/OptimFoundation/OptimFoundation/`）。`AI-Modeling` 端的遷移見 [`2026-08-01-optim-docs-and-projects-migration.md`](2026-08-01-optim-docs-and-projects-migration.md)。

## Motivation / Why

1. **手寫實驗迴圈到處都是**：每個專案都有一份 `ExperimentRunner.cs`，內容高度重複（建 config → new engine → 建模 → `Trial.Capture` → `Save`）。框架裡的 `Templates/Template_CPLEX/CrossExperiment.cs` 已經是 n×m 的手寫雛形，證明需求真實存在但沒有被框架吸收。
2. **experiment 路徑繞過 `OptModel`，專案級行為整組不會發生**：solve 走 `OptModel.Execute()`（設 log 檔名、清舊檔、設模型名），experiment 自己 `new OptEngine` 什麼都沒做。兩條路徑的行為差異全靠人記得補，補漏了不會報錯。
3. **名字錯置**：今天的 `OptModel` 不是模型，是求解管線。真正的「模型」（變數 + 目標式 + 限制式）沒有對應的物件，只能以三個散落的 delegate 存在於 `OptModel` 私有欄位裡，無法被第二個環境重用。
4. **「目標式必須先於限制式」是靠註解維持的**：寫反了不報錯，只會讓 soft constraint 的 penalty 靜靜掛空。拆成 `AddObjective` / `AddConstraints` 之後由框架階段順序保證。

## Scope

### In Scope

- 新增 `OptModel`（模型定義）：`.AddVariables()` + `.AddObjective()` + `.AddConstraints()`
- 今天的 `OptModel`（runner）**改名** `OptProject`，改為吃 `OptModel` 實例
- 新增 `OptExperiment`（實驗 runner）：n 設定 × m 模型交叉展開
- `OnSolved` 由模型移到 runner（只有 `OptProject` 有）
- `CplexConfig.Clone()` / `ProjectConfig.Clone()`（`MemberwiseClone` 實作 + 反射對等測試）
- `DataContext` 建構完成後**凍結**：建模階段寫入即丟例外
- 遷移框架 solution 內所有呼叫點；刪除 `Templates/Tutorial/ExperimentRunner.cs`、改寫 `Templates/Template_CPLEX/CrossExperiment.cs`

### Out of Scope

- **`AI-Modeling` 端的一切**（`Projects/*`、`Template/`、`dlls/`、所有 `.md`）→ 遷移規格
- **`OptimFoundation.Gurobi`**：不在開發 scope。Gurobi 無 `OptModel`/`OptProject`，本規格不為它新增任何東西；只需保持編譯通過
- **平行執行 n×m**：本輪序列跑。平行涉及 CPLEX 授權席次與 static 狀態（`Logging` / `FolderDir`），另議
- **實驗結果的自動分析 / 選最佳**：`OptExperiment.Run()` 只負責跑完與輸出，不做判斷

## User Stories / Use Cases

1. As a 建模者, I want to 定義一次模型、想在專案跑就 `OptProject`、想比較就 `OptExperiment`, so that 我不必為了跑實驗再抄一份建模碼。
2. As a 跑 tuning 的人, I want to 寫 `baseline.Clone()` 疊出下一組設定、哪組贏就讓它變新 baseline, so that 我能一輪一輪往上疊而不必重打全部欄位。
3. As a 比較 formulation 的人, I want to `AddModel(model1).AddModel(model2)` 就自動跑出 2×n 的交叉表, so that 我不用手寫巢狀迴圈。
4. As a 維護者, I want to 「目標式先於限制式」由框架保證, so that 沒有人能靠寫錯順序讓 soft constraint 的 penalty 靜靜失效。

## Acceptance Criteria

- [ ] AC1：`new OptModel("X").AddVariables(…).AddObjective(…).AddConstraints(…)` 可組出模型定義，且**不接觸任何 engine**（純註冊）
- [ ] AC2：`OptProject` 與 `OptExperiment` 吃同一個 `OptModel` 實例，跑出的模型（變數數、限制式數、目標值）完全一致
- [ ] AC3：框架保證執行順序 **variables → objective → constraints**，與註冊順序無關；刻意反序註冊仍得到相同模型
- [ ] AC4：`OptExperiment` 的 `AddModel × AddConfig` 展開為全笛卡兒，Trial label 格式 `"{模型名} | {設定名}"`
- [ ] AC5：`OptExperiment.AddTrial(model, config)` 可明寫單一 cell，與笛卡兒展開並存不衝突
- [ ] AC6：`OptProject` 有 `OnSolved`，`OptExperiment` **沒有**（編譯期就找不到這個方法）
- [ ] AC7：`CplexConfig.Clone()` / `ProjectConfig.Clone()` 回傳強型別；反射測試證明 clone 與原件的**每個** public field/property 相等
- [ ] AC8：`DataContext` 凍結後寫入任一欄位 → 丟明確例外（含欄位名）；`OptData.Load` 回傳的物件即為凍結狀態
- [ ] AC9：`OptExperiment` 預設 solver log OFF、不匯出 LP/MPS/Sol、不做 housekeeping；`OptProject` 預設維持今天行為
- [ ] AC10：`dotnet build OptimFoundation.sln` 0 error、xUnit 全綠（含本規格新增的測試）
- [ ] AC11：`Templates/Sudoku_SHC279`、`Templates/FJSP_BASIC_BRICK`、`Templates/Tutorial` 遷移後 `dotnet run` 的 Status 與目標值與遷移前一致
- [ ] AC12：`Templates/Template_CPLEX/CrossExperiment.cs` 改用 `OptExperiment` 後，6 個 Trial 的 label 與目標值與手寫版一致
- [ ] AC13：`AI-Modeling/dlls/` **未被更動**、8 個練習專案結果仍與 `baseline/BASELINE.md` 一致（本規格對 AI-Modeling 零影響）

## Module Interactions

- **OptimFoundation.Cplex**
  - 新增 `OptModel.cs`（模型定義，取代原檔內容）
  - 原 `OptModel.cs` 的 runner 邏輯 → `OptProject.cs`
  - 新增 `OptExperiment.cs`
  - `CplexConfig.cs`：加 `Clone()`
- **OptimFoundation.Core**
  - `ProjectConfig.cs`（Spec 1 產出）：加 `Clone()`
  - `DataContext.cs`：加凍結機制
  - `Experiment.cs` / `Trial.cs`：**不改**，`OptExperiment` 內部使用它們產出結果
- **框架 solution 內遷移對象**
  - `Templates/FJSP_BASIC_BRICK/Program.cs`、`Templates/Sudoku_SHC279/Program.cs`、`Templates/Tutorial/Program.cs`、`Templates/Template_CPLEX/Program.cs`：`new OptModel(name)` → 拆模型定義 + `new OptProject(model)`
  - `Templates/Tutorial/ExperimentRunner.cs`：**整檔刪除**
  - `Templates/Template_CPLEX/CrossExperiment.cs`、`ExperimentDemo.cs`：改用 `OptExperiment`
  - `Templates/Template_CPLEX/RosteringProblem.cs`、`RosteringProblemOptModel.cs`：手寫 `Execute()` 後路架構，改為引用新 `OptModel`
  - `tests/**`：跟著新 API 改

## API Design

### `OptModel` — 模型定義（新語意）

```csharp
namespace OptimFoundation.Cplex
{
    /// <summary>
    /// 模型定義：這個模型有哪些變數、什麼目標式、哪些限制式。
    /// 與「在哪裡跑」無關——同一個實例可交給 OptProject（跑一次）或 OptExperiment（跑 n×m 次）。
    /// 純註冊，不持有 engine、不執行任何東西。
    /// </summary>
    public sealed class OptModel
    {
        /// <summary>模型名。用於 OptExperiment 的 Trial label（"{模型名} | {設定名}"）。NEVER 與專案名混用。</summary>
        public string Name { get; }

        public OptModel(string name = "Model");

        public OptModel AddVariables(Action<OptEngine> build);
        public OptModel AddObjective(Action<OptEngine> build);
        public OptModel AddConstraints(Action<OptEngine> build);

        /// <summary>依 variables → objective → constraints 的固定順序組進 engine。runner 呼叫，一般不直接用。</summary>
        internal void ApplyTo(OptEngine engine);
    }
}
```

**三個方法各自可呼叫多次**（依註冊順序在該階段內執行），但**階段順序固定**：variables → objective → constraints，與註冊先後無關。

### `OptProject` — 專案 runner

```csharp
public sealed class OptProject : IDisposable
{
    public OptProject(OptModel model, string projectName = null, int retentionDays = 30);

    public OptProject UseConfig(Func<ProjectConfig> configFactory);
    public OptProject UseConfig(Func<CplexConfig> configFactory);
    public OptProject OnSolved(Action<OptEngine> handler);

    public bool Execute();

    public OptEngine optEngine { get; }
    public bool IsSuccess { get; }
    public TimeSpan totalTimeSpan { get; }
}
```

行為 = Spec 1 定義的 `OptModel.Execute()`（解析設定 → log 檔名 → housekeeping → 建 engine → `model.ApplyTo(engine)` → Solve → OnSolved）。

### `OptExperiment` — 實驗 runner

```csharp
public sealed class OptExperiment
{
    public OptExperiment(string name, string description);

    public OptExperiment UseConfig(Func<ProjectConfig> configFactory);   // 所有 cell 共用

    public OptExperiment AddModel(OptModel model);                       // m
    public OptExperiment AddConfig(string label, CplexConfig config);     // n
    public OptExperiment AddTrial(OptModel model, string label, CplexConfig config);  // 明寫單格

    /// <summary>跑完全部 cell（笛卡兒展開 + 明寫的單格），回傳已 Save 的 Experiment。</summary>
    public Experiment Run();
}
```

- 預設 `ProjectConfig`：`EnableSolverLog = false`、三個 `Export* = false`、**不做 housekeeping**
- **沒有 `OnSolved`**（AC6）
- 每個 cell：`new OptEngine(config, projectConfig)` → `engine.Build()` → `model.ApplyTo(engine)` → `Trial.Capture(engine, label, () => engine.Solve())`
- 全部跑完呼叫 `Experiment.Save()`

### 呼叫端全貌

```csharp
// ── ① 材料：資料與設定都在外面宣告一次 ──
var data = OptData.Load(() => new Dataload());

var projectConfig = new ProjectConfig { ProjectName = "Rostering" };

var baseline = new CplexConfig { epGap = 0.03, timeLimit = 60, workThreads = 8 };
var emphasis = baseline.Clone(); emphasis.Emphasis = 2;
var threads = baseline.Clone(); threads.Threads = 2;

// ── ② 模型 ──
var model1 = new OptModel("Model1")
    .AddVariables(e => CreateVariables(data, e))
    .AddObjective(e => BuildObjective(data, e))
    .AddConstraints(e => BuildConstraints(data, e));

var model2 = new OptModel("Model2")
    .AddVariables(e => CreateVariables(data, e))
    .AddObjective(e => BuildObjective(data, e))
    .AddConstraints(e => { BuildConstraints(data, e); new Constraint_TEST(e, data.Date).Build(); });

// ── ③ 環境一：專案 ──
using var project = new OptProject(model1)
    .UseConfig(() => projectConfig)
    .UseConfig(() => baseline)
    .OnSolved(e => data.WriteToCSV(e));
bool ok = project.Execute();

// ── ③ 環境二：實驗（同一批材料）──
var result = new OptExperiment("rostering-cross", "Model1 vs Model2 × 3 組設定")
    .UseConfig(() => projectConfig)
    .AddModel(model1).AddModel(model2)
    .AddConfig("baseline", baseline)
    .AddConfig("emphasis", emphasis)
    .AddConfig("threads", threads)
    .Run();   // → 6 個 Trial → Experiments/rostering-cross.csv + .json
```

### `Clone()`

```csharp
// CplexConfig.cs / ProjectConfig.cs
public CplexConfig Clone() => (CplexConfig)MemberwiseClone();
public ProjectConfig Clone() => (ProjectConfig)MemberwiseClone();
```

MUST 用 `MemberwiseClone()`，**NEVER 手寫逐欄複製** —— Why: `CplexConfig` 有 40+ 欄位，手寫版在未來任何人新增旋鈕時會靜默漏掉，迭代數輪後才發現設定沒帶下去且無任何錯誤訊息。

MUST 配一個反射測試比對 clone 與原件的每個 public field/property 相等——連「未來有人加了 reference type 欄位導致 shallow copy 失效」也擋得下來。

### `DataContext` 凍結

```csharp
// OptData.Load 回傳前呼叫；之後任何欄位寫入丟例外
public abstract class DataContext
{
    internal void Freeze();
    protected void GuardMutation(string member);   // 凍結後被呼叫即 throw
}
```

例外訊息 MUST 含欄位名與「模型建構階段不得修改資料」的說明。

## Edge Cases & Error Handling

- **`OptModel` 三個方法都沒呼叫** → `ApplyTo` 什麼都不做，engine 空模型，`Solve()` 回 Optimal 目標值 0。MUST 在 `Run()`/`Execute()` 開頭檢查並 `Logging.Warn`
- **同一個 `OptModel` 被兩個 runner 使用** → 合法且是設計目的。`OptModel` 不持有 engine 狀態，可重複 `ApplyTo`
- **`OptExperiment` 沒 `AddModel` 或沒 `AddConfig`** → 笛卡兒積為空。若同時也沒 `AddTrial` → 丟例外（跑一個沒有 cell 的實驗必是寫錯）
- **同名 `AddConfig` label 重複** → 丟例外。Why: label 是 `Experiment.Save()` 的去重鍵之一，重複會讓兩個 cell 互相覆蓋
- **`AddTrial` 與笛卡兒展開產生同一組 (model, config)** → 兩個 cell 都跑，label 不同即可並存；label 相同則依上一條丟例外
- **迭代式 tuning 的 label 撞名**：`Experiment.Save()` 跨 run append、去重鍵 `RunAt + Label`。第 3 輪的 `"baseline"` 已是第 2 輪贏家、與第 1 輪設定不同卻同名。資料未丟（每個 trial 的 `ConfigSnapshot` 完整），糊掉的只有 label → **規範層**要求 `AddConfig("r3-baseline", …)` 或實驗名帶輪次；框架不強制
- **凍結後的合法寫入需求**（例如 `WriteToCSV` 想暫存統計）→ 那類欄位不該放 `DataContext`。若真有需求，另開 mutable 的 result 物件，NEVER 解除凍結
- **`OptProject` 未 `using`** → CPLEX native 記憶體不回收（與今天相同）。`OptExperiment` 每個 cell 內部自行 `using`，呼叫端不需要

## Non-Functional Requirements

- **對稱性是驗收重點**：AC2（同一模型兩個 runner 跑出相同結果）是本規格最核心的一條，任何「只有 project 有 / 只有 experiment 有」的行為差異都必須是**刻意設計**（AC6、AC9）並寫進文件
- **`OptExperiment` 序列執行**：n×m 個 cell 依序跑，不平行（見 Out of Scope）
- **框架唯讀天條的例外**：本規格刻意改框架本體，rebuild 但**不更新** `AI-Modeling/dlls/`（AC13）
- **命名 breaking change**：`OptModel` 語意翻轉是最容易誤解的一點，`<summary>` MUST 明寫「本型別是模型定義，不是 runner；找 runner 請看 `OptProject`」

## Open Questions

- [ ] Q1：`OptExperiment` 要不要支援「跑到某條件就停」（例如找到 gap < 1% 就不跑剩下的 cell）？暫定不做
- [ ] Q2：`OptModel.ApplyTo` 目前是 `internal`，跨組件的自訂 runner 就寫不出來。要不要開成 public？暫定 internal
- [ ] Q3：`OptProject` ctor 的 `projectName` / `retentionDays` 參數在 `ProjectConfig` 已能表達，是否該移除以免兩處來源？暫定保留（Spec 1 的 AC6 依賴 ctor 優先）

## Implementation Plan

### Stub 階段（先做）

- [ ] S1：新增 `OptModel.cs`（模型定義），三個 `Add*` 方法 + `ApplyTo` 留 TODO
- [ ] S2：`OptModel.cs` 原 runner 內容複製到 `OptProject.cs`，改吃 `OptModel` 實例，`Execute()` 內留 TODO
- [ ] S3：新增 `OptExperiment.cs`，fluent 方法齊全、`Run()` 留 TODO
- [ ] S4：`CplexConfig.Clone()` / `ProjectConfig.Clone()`
- [ ] S5：`dotnet build` 通過（舊 `OptModel` 暫時保留，行為未變）

### 逐層實作

- [ ] I1：`OptModel.ApplyTo` 實作固定階段順序（AC3）
- [ ] I2：`OptProject.Execute()` 接上 Spec 1 的設定解析 + `ApplyTo`
- [ ] I3：`OptExperiment.Run()` 實作笛卡兒展開 + `AddTrial` 單格 + `Trial.Capture` + `Save`
- [ ] I4：`OptExperiment` 預設 `ProjectConfig`（log OFF / 不匯出 / 不 housekeeping，AC9）
- [ ] I5：`DataContext` 凍結機制（AC8）
- [ ] I6：刪除舊 runner 版 `OptModel`；全 solution 改名為 `OptProject`
- [ ] I7：遷移 `Templates/*`（含刪 `Tutorial/ExperimentRunner.cs`、改寫 `CrossExperiment.cs`）
- [ ] I8：遷移 `tests/*`；補測試：AC3 反序註冊、AC7 clone 反射對等、AC8 凍結、AC2 兩 runner 一致
- [ ] I9：`dotnet build` + xUnit 全綠（AC10）
- [ ] I10：`Templates` 三個範本實跑，比對遷移前後（AC11、AC12）
- [ ] I11：確認 `AI-Modeling/dlls/` 未動、8 個練習專案仍符合 `baseline/BASELINE.md`（AC13）

## References

- 依賴：[`2026-08-01-optimfoundation-dual-config.md`](2026-08-01-optimfoundation-dual-config.md)（`ProjectConfig` 由該規格產出）
- 後續：[`2026-08-01-optim-docs-and-projects-migration.md`](2026-08-01-optim-docs-and-projects-migration.md)（文件 + AI-Modeling 專案遷移）
- n×m 手寫雛形（label 慣例來源）：`$OPT/OptimFoundation/OptimFoundation/Templates/Template_CPLEX/CrossExperiment.cs`
- 今天的 runner：`$OPT/.../src/OptimFoundation.Cplex/OptModel.cs`
- `Experiment` / `Trial` / `ConfigSnapshot`：`$OPT/.../src/OptimFoundation.Core/Experiment.cs`、`Experiments/`
- 基準值：`$OPT/AI-Modeling/baseline/BASELINE.md`
