---
title: Experiment 套件化 tuning 實驗記錄環境（solver-agnostic）
status: shipped
created: 2026-06-18
updated: 2026-06-19
modules: [core, cplex, gurobi, solver]
---

# Experiment — Solver-Agnostic Tuning 實驗記錄環境

## Summary

在 `OptimFoundation.Core` 提供一套**套件化的單次求解記錄器**：開發者把它套進任何「求解一次」的既有 project，
跑完後自動抓取**這一 run 的完整設定快照（foundation + solver 參數）**與**收斂數據（求解時間、bound、gap、node 數、軌跡）**，
累積成一份 `Experiment`，輸出 **CSV + JSON**。CSV 給人做 tuning 對照，JSON 給後續 LLM-based tuning 當結構化參考。
設計為 solver-agnostic：透過 `ISolverEngine` / 抽象 config 介面同時支援 Cplex / Gurobi / Solver。

## Motivation / Why

現況下要比較不同 solver 設定的效果，只能人工翻 CPLEX log、各自手抄目標值與時間，無法系統化累積、也無法餵給 LLM 學習「設定 → 結果」對應。

研究三個 engine 後發現兩個結構缺口（本規格要一併補上）：

### Config 抽象缺口
`ISolverConfig` 只抽象 5 個共用參數（`TimeLimit` / `MipGap` / `Threads` / `LogToConsole` / `LogFilePath`）。
以下「各家都有、但沒抽象」的 tuning 旋鈕散落在各自的 concrete config，無法跨引擎統一掃描：

| 概念 | CPLEX | Gurobi | Solver | 抽象狀態 |
|---|---|---|---|---|
| 隨機種子 | `randomSeed` | `Seed` | — | 缺 |
| 解品質側重 | `mipEmphasis` | `MipFocus` | — | 缺 |
| 可行容忍 | `epRHS` | `FeasibilityTol` | `Epsilon` | 缺 |
| 最佳容忍 | `epOpt` | `OptimalityTol` | — | 缺 |
| 根演算法 | `algorithm` | `Method` | — | 半套（Cplex adapter 有 `RootAlgorithm`，介面未列） |
| Presolve | （CPLEX 有，未封裝） | `Presolve` | — | 缺 |
| Heuristic 強度 | （CPLEX 有，未封裝） | `Heuristics` | — | 缺 |
| 記憶體上限 | `workMemory` | `SoftMemLimit` | — | 缺 |
| export LP/MPS/Sol | `exportLP…` | `ExportLp…` | `exportLP…` | 三套重複定義 |

### Telemetry 缺口（收斂數據）
`EngineBase` 求解後沒有統一暴露收斂數據，導致「抓這一 run 的跑的數據」做不到：

| 指標 | 現況 | 各引擎可取來源 |
|---|---|---|
| Status / 目標值 | 有 | — |
| BestBound / MIPGap | 欄位存在，但 **CPLEX `Solve()` 未回填**（只 log），不一致 | CPLEX `GetBestObjValue()` / `GetMIPRelativeGap()` |
| 求解 wall time | 無 | CPLEX `GetCplexTime()` 差值 / Gurobi `Model.Runtime` / Solver result |
| node 數 | 無 | CPLEX `GetNnodes()` / Gurobi `Model.NodeCount` |
| iteration 數 | 無 | CPLEX `GetNiterations()` / Gurobi `Model.IterCount` |
| 收斂軌跡 | 無 | solver callback（CPLEX `MIPInfoCallback`、Gurobi `GRBCallback`）或 log parse |
| constraint count | private（`_constraints.Count`） | EngineBase |

## Scope

### In Scope

- `OptimFoundation.Core` 新增實驗記錄領域型別：`Experiment`（補完）、`Trial`、`ConfigSnapshot`、`SolveMetrics`、`ConvergencePoint`
- 套件化的單次擷取 API：`Trial.Capture(engine, label, solveAction)` —— 不接管 engine 生命週期，只讀取設定與結果
- 統一 telemetry 面：`ISolverEngine` 新增 `SolveMetrics LastMetrics`，三個 `OptEngine` 各自在 `Solve()` 回填
- 統一 tunable config 面：新增 `ITunableConfig`（與 `ISolverConfig` 並存，不破壞既有），三個 config 實作
- ConfigSnapshot 以「抽象旋鈕 + reflection 補抓 concrete 專屬欄位」雙軌，確保不漏設定
- 持久化：`CsvExperimentWriter`（每 Trial 一列，扁平欄位）+ `JsonExperimentWriter`（巢狀、含軌跡，LLM 友善），append 同名實驗檔
- `FolderDir` 新增 `Experiment` 資料夾

### Out of Scope

- 內建 grid-search / 自動超參數搜尋編排器（多組設定掃描由開發者自行迴圈呼叫 `Capture`；本規格只提供薄的 `RunSweep` helper，非必要）
- 視覺化 dashboard / 繪圖
- 即時 LLM 線上自動調參（JSON 格式預留給之後，本規格不串 LLM）
- Markdown 報告輸出（本期只 CSV + JSON）
- 多機 / 分散式實驗聚合

## User Stories / Use Cases

1. As a 開發者, I want to 在既有 `RosteringProblem.Execute()` 外包一層 `Trial.Capture`，so that 跑一次就把當下的 solver 設定與收斂數據存下來，不用改既有求解邏輯。
2. As a 開發者, I want to 用迴圈跑不同 `mipEmphasis` 各一次、累積到同一個 `Experiment`，so that 我能在 CSV 裡排序比較求解時間與 gap。
3. As a 開發者, I want to 同一份實驗記錄在 Cplex 與 Gurobi 都產出相同 schema 的欄位，so that 跨引擎比較。
4. As a LLM tuning 流程, I want to 讀結構化 JSON（設定 → 指標 → 軌跡），so that 之後能據此推薦下一組設定。

## Acceptance Criteria

- [ ] `new Experiment(name, desc)` 後可 `AddTrial`，`Trials` 型別為 `List<Trial>`（補完現有 stub）
- [ ] `Trial.Capture(engine, label, () => engine.Solve())` 回傳一個 `Trial`，內含 `ConfigSnapshot` 與 `SolveMetrics`，且**不會 Dispose engine**
- [ ] `SolveMetrics` 至少含：`Status`、`ObjectiveValue`、`BestBound`、`MipGap`、`WallTimeMs`、`NodeCount`、`IterationCount`、`VarCount`、`ConstraintCount`
- [ ] 求解結果為 `TimeLimit` / `Feasible`（非 Optimal）時仍完整記錄 best feasible + gap，`Trial` 不被視為失敗
- [ ] CPLEX `Solve()` 回填 `LastMetrics`（含 wall time、node 數），不再只印 log
- [ ] `ISolverEngine.LastMetrics` 在三個 engine 都實作（Gurobi/Solver 缺的欄位填 `null`/`NaN` 而非丟例外）
- [ ] `ITunableConfig` 在 `CplexConfig` / `GurobiConfig` / `SolverConfig` 都實作，抽象旋鈕對映到各自 concrete 欄位
- [ ] `ConfigSnapshot` 同時記錄抽象旋鈕值與 reflection 抓到的 solver 專屬欄位（key-value）
- [ ] `experiment.Save()` 產出 `experiments/<name>.csv` 與 `experiments/<name>.json`；再跑一次同名實驗為 **append** 而非覆寫
- [ ] CSV 每個 Trial 一列，欄位涵蓋 label + 所有抽象旋鈕 + 所有指標；JSON 為 trial 陣列，含巢狀 config 與 `convergence[]`
- [ ] 收斂軌跡 `ConvergencePoint[]`（`TimeMs`/`Objective`/`Bound`/`Gap`）：CPLEX/Gurobi 透過 callback 擷取；若 callback 未啟用則為空陣列且不報錯
- [ ] `dotnet build` 全綠（net48），既有 `OptimFoundation.Cplex.Tests` 不被破壞

## Module Interactions

- **Core**
  - `Experiments/Experiment.cs`（補 `Trials` 型別 + `Save` / `Load`）
  - `Experiments/Trial.cs`、`ConfigSnapshot.cs`、`SolveMetrics.cs`、`ConvergencePoint.cs`
  - `Experiments/IExperimentWriter.cs`、`CsvExperimentWriter.cs`、`JsonExperimentWriter.cs`
  - `ITunableConfig.cs`（新介面）、`ISolverEngine.cs`（加 `LastMetrics`）、`EngineBase.cs`（加 `protected SolveMetrics _metrics` + 公開 `ConstraintCount`）
  - `Infrastructure/FolderDir.cs`（加 `Experiment` 資料夾）
- **Cplex**：`OptEngine.Solve()` 回填 `_metrics`（`GetCplexTime`/`GetNnodes`/`GetNiterations`/`GetMIPRelativeGap`/`GetBestObjValue`）；`CplexConfig` 實作 `ITunableConfig`；選用 `MIPInfoCallback` 擷取軌跡
- **Gurobi**：`OptEngine.Solve()` 回填（`Runtime`/`NodeCount`/`IterCount`/`MIPGap`/`ObjBound`）；`GurobiConfig` 實作 `ITunableConfig`；選用 `GRBCallback`
- **Solver**：`OptEngine.Solve()` 回填（result.`ObjectiveValue`/`BestBound`/`MipGap` + 自量 wall time）；`SolverConfig` 實作 `ITunableConfig`（缺的旋鈕回 `null`）
- **Infra / 第三方**：JSON 序列化器（net48 相容）—— 見 Open Questions

## API Design

### 抽象 tunable config 介面

```csharp
namespace OptimFoundation.Core
{
    // 與 ISolverConfig 並存，補齊跨引擎共通 tuning 旋鈕；null = 用 solver 預設
    public interface ITunableConfig
    {
        int?    Seed            { get; set; }  // CPLEX randomSeed / Gurobi Seed
        int?    Emphasis        { get; set; }  // CPLEX mipEmphasis / Gurobi MipFocus
        double? FeasibilityTol  { get; set; }  // CPLEX epRHS / Gurobi FeasibilityTol / Solver Epsilon
        double? OptimalityTol   { get; set; }  // CPLEX epOpt / Gurobi OptimalityTol
        int?    RootAlgorithm   { get; set; }  // CPLEX algorithm / Gurobi Method
        int?    Presolve        { get; set; }  // CPLEX PreInd / Gurobi Presolve
        double? HeuristicEffort { get; set; }  // Gurobi Heuristics(0~1) / CPLEX 對映
        double? MemoryLimitMb   { get; set; }  // CPLEX workMemory / Gurobi SoftMemLimit
    }
}
```

### 統一 telemetry

```csharp
namespace OptimFoundation.Core
{
    public sealed class SolveMetrics
    {
        public SolveStatus Status         { get; set; }
        public double      ObjectiveValue { get; set; }
        public double      BestBound      { get; set; }
        public double      MipGap         { get; set; }
        public double      WallTimeMs     { get; set; }
        public long?       NodeCount      { get; set; }
        public long?       IterationCount { get; set; }
        public int         VarCount       { get; set; }
        public int         ConstraintCount{ get; set; }
        public List<ConvergencePoint> Convergence { get; set; } = new List<ConvergencePoint>();
    }

    public sealed class ConvergencePoint
    {
        public double  TimeMs    { get; set; }
        public double  Objective { get; set; }
        public double  Bound     { get; set; }
        public double  Gap       { get; set; }
    }
}

// ISolverEngine 新增
public interface ISolverEngine : IDisposable
{
    // ...既有...
    SolveMetrics LastMetrics { get; }   // Solve() 後填入；未求解為 null
}
```

### 設定快照 + Trial 擷取（套件化用法）

```csharp
namespace OptimFoundation.Core
{
    public sealed class ConfigSnapshot
    {
        public string Solver { get; set; }                              // "Cplex" / "Gurobi" / "Solver"
        public Dictionary<string, object> Tunable { get; set; }         // 抽象旋鈕
        public Dictionary<string, object> SolverSpecific { get; set; }  // reflection 抓 concrete 欄位
        public static ConfigSnapshot From(ISolverConfig config);        // TODO
    }

    public sealed class Trial
    {
        public string         Label     { get; set; }
        public DateTime       RunAt     { get; set; }
        public ConfigSnapshot Config    { get; set; }
        public SolveMetrics   Metrics   { get; set; }
        public string         Note      { get; set; }

        // 套件化單次擷取：讀 engine.Config → 跑 solveAction → 讀 engine.LastMetrics
        // 不 Dispose engine（生命週期由呼叫端持有）
        public static Trial Capture(ISolverEngine engine, string label,
                                    System.Func<bool> solveAction, string note = null);
    }
}
```

### Experiment 持久化

```csharp
public class Experiment
{
    public string      Name        { get; set; }
    public string      Description { get; set; }
    public DateTime    CreatedAt   { get; set; }
    public List<Trial> Trials      { get; set; }

    public void AddTrial(Trial trial);
    public void Save();                       // → experiments/<Name>.csv + .json（append）
    public static Experiment Load(string name); // 讀回既有 JSON（累積用）
}
```

## Data Model

無 DB。輸出檔案 schema：

**CSV**（`experiments/<name>.csv`，每 Trial 一列）
```
RunAt,Label,Solver,Seed,Emphasis,FeasibilityTol,OptimalityTol,RootAlgorithm,Presolve,HeuristicEffort,MemoryLimitMb,Status,ObjectiveValue,BestBound,MipGap,WallTimeMs,NodeCount,IterationCount,VarCount,ConstraintCount,Note
```

**JSON**（`experiments/<name>.json`，LLM 友善）
```json
{
  "name": "rostering-tuning",
  "description": "比較 mipEmphasis",
  "createdAt": "2026-06-18T...",
  "trials": [
    {
      "label": "emphasis=2",
      "runAt": "2026-06-18T...",
      "config": {
        "solver": "Cplex",
        "tunable": { "Seed": 1, "Emphasis": 2, "FeasibilityTol": 1e-6 },
        "solverSpecific": { "workThreads": 8, "polishAfterTime": null }
      },
      "metrics": {
        "status": "Feasible", "objectiveValue": 1234.0, "bestBound": 1200.0,
        "mipGap": 0.027, "wallTimeMs": 41230, "nodeCount": 18044,
        "iterationCount": 220331, "varCount": 5120, "constraintCount": 980,
        "convergence": [ { "timeMs": 1200, "objective": 1500, "bound": 1100, "gap": 0.36 } ]
      },
      "note": null
    }
  ]
}
```

## Edge Cases & Error Handling

- **單次求解即記錄（套件定位）**：`Capture` 只讀設定 + 跑一次 + 讀指標，**不接管也不 Dispose** 呼叫端 engine；多組掃描由開發者自行用新 engine 迴圈。
- **非 Optimal**：`TimeLimit` / `Feasible` 仍記錄 best feasible、bound、gap、Status，不丟例外、不視為失敗。
- **Infeasible / Unbounded / 無解**：`ObjectiveValue`/`Gap` 填 `NaN`，`Status` 照實記錄；`GetObjectiveValue` 不被呼叫以免丟例外。
- **跨引擎缺欄位**：某 engine 取不到 `NodeCount`/`IterationCount` → 填 `null`（CSV 留空），不報錯。
- **可重現性**：`ConfigSnapshot` 連 `Seed` 與所有 concrete 欄位（reflection）一起存；append 模式保留歷史不覆寫。
- **append 併發**：`Save()` 對輸出檔上鎖（`lock` 或 `FileShare`）；同名實驗以 `Load` → 合併 → 覆寫整檔（非逐列 append，避免 JSON 結構破壞）。
- **net48 / Nullable disable**：型別設計避免依賴 C# 8 nullable 語意。
- **callback 未啟用**：`Convergence` 為空陣列，`Save` 照常。

## Non-Functional Requirements

- **Performance**：指標擷取在 `Solve()` 後讀 solver 屬性，開銷可忽略；callback 軌跡為選用、可關閉。
- **相容性**：net48；不破壞既有 `ISolverConfig` 與三個 engine 既有行為（新介面/屬性為加法）。
- **依賴最小**：JSON 序列化盡量不引入大型相依（見 Open Questions）。
- **Observability**：沿用既有 `Logging`；`Save` 後 log 輸出檔案路徑與 Trial 數。

## Resolved Decisions（2026-06-18 approved）

- **JSON 序列化器**：採 `System.Text.Json` NuGet（net48 相容、輕量）。**版本須用 8.0.0**（非 8.0.5）—— net8 消費端（如 Template）的 shared framework 提供 assembly 8.0.0.0，綁 8.0.5（assembly 8.0.0.5）會在 net8 app 載入失敗。options 設 `UnsafeRelaxedJsonEscaping`（中文直出）+ `AllowNamedFloatingPointLiterals`（非 Optimal 的 NaN）。
- **收斂軌跡**：本期「final-point 指標」必做；「逐點軌跡」為選用旗標 `captureTrajectory`，**只實作 CPLEX**。但抽象面需預留可擴展性：定義 `ITrajectorySource` / `EngineBase` 上的 `virtual` hook，讓 Gurobi/Solver 之後接得上（本期不實作其 body）。
- **`Emphasis` 跨引擎語意**：快照只記原始整數值 + solver 名，不做語意正規化。

### 軌跡可擴展設計

```csharp
namespace OptimFoundation.Core
{
    // 各 engine 選擇性實作；未實作者回 false / 空，呼叫端不報錯
    public interface ITrajectorySource
    {
        bool   SupportsTrajectory { get; }       // 預設 false
        void   EnableTrajectory();               // Capture 前呼叫；未支援為 no-op
        IReadOnlyList<ConvergencePoint> Trajectory { get; }  // Solve() 後填，未支援為空
    }
}
// 本期：OptEngine(Cplex) 實作（MIPInfoCallback）；Gurobi/Solver 宣告 SupportsTrajectory=false（後補）
```

## Implementation Plan

### Stub 階段（先做，只簽名 + TODO，跑 build）
- [ ] Core 新增 `Experiments/` 型別：`Trial`、`ConfigSnapshot`、`SolveMetrics`、`ConvergencePoint`、`IExperimentWriter` + 兩個 writer（body `throw new NotImplementedException()`）
- [ ] 補 `Experiment.cs`：`Trials` 型別、`AddTrial`、`Save`/`Load` 簽名
- [ ] 新增 `ITunableConfig.cs`；`ISolverEngine` 加 `LastMetrics { get; }`；`EngineBase` 加欄位與公開 `ConstraintCount`
- [ ] 三個 `OptEngine` 加 `LastMetrics` 實作 stub；三個 config `: ITunableConfig` 並補屬性 stub
- [ ] `FolderDir` 加 `Experiment` 資料夾
- [ ] `dotnet build` 確認結構連得起來（net48）

### 逐層實作
- [x] CPLEX `Solve()` 回填 `SolveMetrics`（wall time / node / iter / gap / bound；node·iter 用 reflection helper，取不到回 null）
- [x] Gurobi `Solve()` 回填；Solver `Solve()` 回填（自研 solver 無 node/iter → null）
- [x] `ConfigSnapshot.From`（抽象旋鈕 + 共用 ISolverConfig + reflection 補 concrete）
- [x] `Trial.Capture`（不 Dispose engine；非 Optimal 仍記錄）
- [x] `CsvExperimentWriter` + `JsonExperimentWriter`；`Experiment.Save/Load`（JSON 權威來源、RunAt+Label 去重 append）
- [x] `ITunableConfig` delegate 到三個 config 的 concrete 欄位（CPLEX/Gurobi 完整；Solver 僅 FeasibilityTol↔Epsilon）
- [x] 額外修正：CPLEX `SetObjective` 改覆寫語意（先移除舊目標式），讓軟性 penalty 多次重設目標式可正確運作
- [x] callback 軌跡：CPLEX `MIPInfoCallback`（取樣＝incumbent 改善 或 ≥200ms；上限 2000 點；lock 保護；opt-in 因會關 dynamic search）
- [x] 範例：`Templates/Template_CPLEX/ExperimentDemo.cs`（掃 3 組設定、`dotnet run -- experiment` 觸發；實跑驗證產出 CSV/JSON）
- [x] Tests：`EngineBaseTests`（soft 通用行為）+ `OptEngineIntegrationTests`（軟性 Ge 功能）+ `ExperimentIntegrationTests`（Capture→Save→CSV/JSON、軌跡收集）

## References

- 既有半成品：`src/OptimFoundation.Core/Experiment.cs`
- 求解器設定：`src/OptimFoundation.Cplex/CplexConfig.cs`、`OptimFoundation.Gurobi/GurobiConfig.cs`、`OptimFoundation.Solver/GurobiConfig.cs`
- 引擎：`src/OptimFoundation.Cplex/OptEngine.cs`、`EngineBase.cs`、`ISolverEngine.cs`
- 開發說明書：`specs/cplex-project-dev-spec.md`
