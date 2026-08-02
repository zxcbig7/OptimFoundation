---
title: "CodeMap — 2026-08-02"
toc:
  depth_from: 1
  depth_to: 3
  ordered: false
---

[TOC]

## 審查範圍

> ==Effort==: **high**　|　==Base==: 全 repo 體檢（非 diff）　|　==Sync==: 2026-08-02 事實同步（dual config、runner symmetry、framework-controlled freeze、Tutorial/FJSP project references）

!!! note 使用說明
    本檔為 Code Review 地圖，由 `/code-review` 自動產生。
    Phase 2 的所有 Review 評語都會引用本檔的 §section。

!!! note 目錄結構前提
    本檔位於 git repo 根（`OptimizationFramework/OptimFoundation/OptimFoundation/`）。其外層 `OptimFoundation/` 僅為資料夾外殼、非 repo。
    `Templates/` 與 `tests/` 已收進 git repo、sln 含全部 9 專案（4 src + 4 Templates + 1 tests）。
    下表路徑一律相對 **git repo 根**（`src/...`、`Templates/...`、`tests/...`）。

## File Index

### src（四專案：Core / Cplex / Gurobi / Generators）

| 檔案 | 行數 | 專案 | 關鍵 Symbol |
|------|-----|------|-------------|
| `src/OptimFoundation.Core/DesignBases.cs` | 104 | Core | `ModelElementBase`, `ConstraintBase`, `ParameterBase`, `VariableBase` |
| `src/OptimFoundation.Core/SetBase.cs` | 177 | Core | `ISetBrick`（marker）, `SetBase<T>`（`IReadOnlyList<T>`；`Load(IDataSource, name=null)` paved path、`LoadInline/LoadFrom/LoadCsv`、四道防呆） |
| `src/OptimFoundation.Core/IO/IDataSource.cs` | 45 | Core | `IDataSource`（名稱可定址：`LoadParam<T>(file=null)`/`LoadSet`；CSV/InMemory）, `ISolutionSink` |
| `src/OptimFoundation.Core/IO/SetNaming.cs` | 26 | Core | `SetNaming`（internal；set 名慣例單一真相：`Logical`/`File`——三來源用檔名形式 `Set_{X}`，邊界轉位址） |
| `src/OptimFoundation.Core/IO/CsvDataSource.cs` | 86 | Core | `CsvDataSource : IDataSource`（ctor 建 Data/、set 走 `SetNaming.File`、`LoadTable`→raw DataTable）, `CsvSolutionSink : ISolutionSink` |
| `src/OptimFoundation.Core/IO/DbDataSource.cs` | 82 | Core | `DbDataSource`（**query-only，不實作 IDataSource**）：`LoadParam`/`LoadSet`/`LoadTable`(raw DataTable) 皆明寫 SQL；無 tablePrefix/dataId/resolver |
| `src/OptimFoundation.Core/IO/InMemoryDataSource.cs` | 48 | Core | `InMemoryDataSource : IDataSource`（demo / 測試用，`AddParameters`/`AddSet` 鏈式；set key 走 `SetNaming.Logical`） |
| `src/OptimFoundation.Core/EngineBase.cs` | 903 | Core | `EngineBase<TModel,TVar,TExpr,TConstr>`（`ISolverEngine`, `ITrajectorySource`）— 三引擎共同父類 |
| `src/OptimFoundation.Core/Enums.cs` | 38 | Core | `VarType`, `ConstraintSense`, `ObjectiveSense` |
| `src/OptimFoundation.Core/Experiment.cs` | 77 | Core | `Experiment`（`AddTrial`, `Save`, `Load`） |
| `src/OptimFoundation.Core/ISolverEngine.cs` | 95 | Core | `ISolverConfig`（只含 solver contract）, `ISolverEngine`, `SolveStatus`, `ISpecialConstraints<TVar,TExpr>` |
| `src/OptimFoundation.Core/Config/ProjectConfig.cs` | 38 | Core | `ProjectConfig`（專案名、保留期、solver log、LP/MPS/Sol 匯出、輸出身分；`Clone()`） |
| `src/OptimFoundation.Core/DataContext.cs` | 221 | Core | `DataContext` 資料註冊 / 驗證；`Freeze` + `GuardMutation` 僅保護 framework-controlled mutation API |
| `src/OptimFoundation.Core/OptData.cs` | 25 | Core | `OptData.Load`：factory → initialize / validate → freeze → return |
| `src/OptimFoundation.Core/ITrajectorySource.cs` | 20 | Core | `ITrajectorySource` |
| `src/OptimFoundation.Core/ITunableConfig.cs` | 33 | Core | `ITunableConfig`（跨引擎 tuning 旋鈕抽象） |
| `src/OptimFoundation.Core/VariableBuilder.cs` | 143 | Core | `VariableBuilder`（`GenVarCombinations`, `ConvertSetsToStringLists`, `GetVarNames<T>`, `BuildVars<T>`） |
| `src/OptimFoundation.Core/IO/CsvCtrl.cs` | 352 | Core | `CsvCtrl`（`BuildParameter<T>(fileName=null)`（預設檔名 = 型別名）, `ReadIntSet/ReadDoubleSet/ReadStrSet/ReadDateSet`, `ReadTable`→DataTable, `ReadMatrixCsv`, `WriteSolution`, `WriteSet`/`WriteParam<T>`→Data/（讀寫對稱，兩階段資料流的輸出端）） |
| `src/OptimFoundation.Core/IO/DBCtrlBase.cs` | 105 | Core | `DBCtrlBase : IDbCtrl`（abstract；`NonQuery` 預設委派 `Execute`） |
| `src/OptimFoundation.Core/IO/IDbCtrl.cs` | 50 | Core | `IDbCtrl : IDisposable`（含 `NonQuery`；亂碼註解已修復為完整中文 doc comment） |
| `src/OptimFoundation.Core/Experiments/ConfigSnapshot.cs` | 68 | Core | `ConfigSnapshot`（`From(ISolverConfig)`） |
| `src/OptimFoundation.Core/Experiments/CsvExperimentWriter.cs` | 110 | Core | `CsvExperimentWriter : IExperimentWriter` |
| `src/OptimFoundation.Core/Experiments/IExperimentWriter.cs` | 29 | Core | `ExpWriterType`, `IExperimentWriter` |
| `src/OptimFoundation.Core/Experiments/JsonExperimentWriter.cs` | 44 | Core | `JsonExperimentWriter : IExperimentWriter` |
| `src/OptimFoundation.Core/Experiments/SolveMetrics.cs` | 57 | Core | `SolveMetrics`, `ConvergencePoint` |
| `src/OptimFoundation.Core/Experiments/Trial.cs` | 59 | Core | `Trial`（`Trial.Capture(engine, label, solveAction, note)`） |
| `src/OptimFoundation.Core/Infrastructure/ClassInfo.cs` | 56 | Core | `ClassInfo`（SQL command 產生器） |
| `src/OptimFoundation.Core/Infrastructure/FolderDir.cs` | 122 | Core | `FolderDir`, `FolderDir.ProjFolder`；含保留期清理 `PurgeOutputs`/`PurgeOlderThan` |
| `src/OptimFoundation.Core/Infrastructure/ReflectionHelper.cs` | 87 | Core | `ReflectionHelper`（`GetMemberNames`, `GetMemberTypes`, `GenerateSQLCols`） |
| `src/OptimFoundation.Core/Logging/Logging.cs` | 118 | Core | `Logging`（static；**全域可變靜態狀態**，見 Review Scope Notes） |
| `src/OptimFoundation.Cplex/CplexConfig.cs` | 197 | Cplex | `CplexConfig : ISolverConfig, ITunableConfig`；只留 solver 旋鈕；`Clone()` |
| `src/OptimFoundation.Cplex/OptEngine.cs` | 1142 | Cplex | `OptEngine : EngineBase<Cplex,INumVar,ILinearNumExpr,IRange>`；ctor 可同時接 solver / project config |
| `src/OptimFoundation.Cplex/OptModel.cs` | 60 | Cplex | `OptModel` 純模型定義：`AddVariables` / `AddObjective` / `AddConstraints`，固定三階段順序 |
| `src/OptimFoundation.Cplex/OptProject.cs` | 119 | Cplex | `OptProject : IDisposable`；單次 runner、雙 config、housekeeping、`OnSolved` / `Execute` |
| `src/OptimFoundation.Cplex/OptExperiment.cs` | 114 | Cplex | `OptExperiment`；m×n + explicit cell、final label 去重、預設 log/export OFF、`Run` / save |
| `src/OptimFoundation.Core/IO/OracleDBCtrl.cs` | 528 | Core | `OracleDBCtrl : DBCtrlBase`（2026-07-10 由原 Db.Oracle 專案併入） |
| `src/OptimFoundation.Generators/AutoSetsGenerator.cs` | 856 | Generators | `AutoSetsGenerator : IIncrementalGenerator`（Roslyn source generator；消費者：三個 Templates（Analyzer DLL）與 sibling AI-Modeling 的 HospitalRostering 系列；另負責為 DataContext 子類 emit 註冊碼） |
| `src/OptimFoundation.Generators/IsExternalInit.cs` | 6 | Generators | `IsExternalInit`（netstandard2.0 polyfill） |
| `src/OptimFoundation.Gurobi/GurobiConfig.cs` | 46 | Gurobi | `GurobiConfig : ISolverConfig, ITunableConfig` |
| `src/OptimFoundation.Gurobi/OptEngine.cs` | 430 | Gurobi | `OptEngine`：**同檔內兩個同名 `public class OptEngine`**，以 `#if GUROBI_INSTALLED`/`#else` 互斥（真實實作 vs. 無 DLL 時的 throw stub） |

### csproj / sln（src 五專案 + 根 sln）

| 檔案 | 專案 | 關鍵設定 |
|------|------|----------|
| `OptimFoundation.sln` | — | 全部 9 專案：`Core`/`Cplex`/`Gurobi`/`Generators` + `Tutorial`/`FJSP_BASIC_BRICK`/`Template_CPLEX`/`Sudoku_SHC279` + `OptimFoundation.Cplex.Tests`（`dotnet build` 全綠是基準） |
| `src/OptimFoundation.Core/OptimFoundation.Core.csproj` | Core | `net8.0`；`PackageReference` NLog 5.3.4、Oracle.ManagedDataAccess.Core 23.9.1、System.Text.Json 8.0.5 |
| `src/OptimFoundation.Cplex/OptimFoundation.Cplex.csproj` | Cplex | `net8.0`；`ProjectReference` → Core；ILOG 走 `$(CplexDir)` property（預設 `C:\IBM\ILOG\CPLEX_Studio2211`） |
| `src/OptimFoundation.Generators/OptimFoundation.Generators.csproj` | Generators | `netstandard2.0`；`IsRoslynComponent`；`PackageReference` Microsoft.CodeAnalysis.CSharp 4.8.0；消費者：三個既有 Templates（`Templates/dlls/` Analyzer DLL）+ Sudoku（Analyzer ProjectReference）+ AI-Modeling |
| `src/OptimFoundation.Gurobi/OptimFoundation.Gurobi.csproj` | Gurobi | `net8.0`；`ProjectReference` → Core；`Reference` 條件式 `Exists('$(GUROBI_HOME)\bin\Gurobi110.NET.dll')` |
| `Templates/Tutorial/Tutorial.csproj` | Tutorial（已入 sln） | Core/Cplex **ProjectReference**；ILOG + generator 仍走 `Templates/dlls/`；三模式 CSV / inmemory / experiment |
| `Templates/FJSP_BASIC_BRICK/FJSP_BASIC_BRICK.csproj` | FJSP（已入 sln） | Core/Cplex **ProjectReference**；ILOG + generator 仍走 `Templates/dlls/` |

### tests（repo `tests/`，已入 sln；157 tests 全綠，2026-08-01 `dotnet test` 實測）

| 檔案 | 專案 | 關鍵 Symbol |
|------|------|-------------|
| `tests/OptimFoundation.Cplex.Tests/OptimFoundation.Cplex.Tests.csproj` | Tests | `net8.0`；xunit 2.9.3；`ProjectReference` → Core、Cplex；ILOG 走 `$(CplexDir)` property |
| `tests/.../Unit/EngineBaseTests.cs` | Tests | `EngineBaseTests`（26 tests：BuildBVs / Pool / Soft constraints） |
| `tests/.../Unit/ModelElementBaseTests.cs` | Tests | `ModelElementBaseTests`（8 tests：`ToString`/`InitClassBySets`） |
| `tests/.../Unit/VariableBuilderTests.cs` | Tests | `VariableBuilderTests`（10 tests） |
| `tests/.../Unit/CsvCtrlTests.cs` | Tests | `CsvCtrlTests`（7 tests：CSV 讀寫） |
| `tests/.../Unit/CsvWriteRoundTripTests.cs` | Tests | `CsvWriteRoundTripTests`（15 tests：WriteSet/WriteParam ↔ 讀取端 round-trip、DateTime 防呆、GetProperties 對位） |
| `tests/.../Unit/DataSourceTests.cs` | Tests | `DataSourceTests`（10 tests：DbDataSource 按名對位） |
| `tests/.../Integration/ExperimentIntegrationTests.cs` | Tests | `ExperimentIntegrationTests`（2 tests：CSV/JSON 落地、Trajectory） |
| `tests/.../Integration/OptEngineIntegrationTests.cs` | Tests | `OptEngineIntegrationTests`（含 CPLEX file-only solver log 持久化驗證） |
| `tests/.../Unit/ObservabilityTests.cs` | Tests | fallback event、設定快照與 Oracle fail-fast 回歸測試 |
| `tests/.../Integration/SolverParamCoverageTests.cs` | Tests | `SolverParamCoverageTests`（1 test：逐一 tuning 參數覆蓋率） |
| `tests/.../Mocks/MockEngine.cs` | Tests | `MockEngine`（測試替身） |
| `tests/.../Mocks/TestModels.cs` | Tests | 測試用 Variable/Parameter 類別 |

> 測試覆蓋 `Core` + `Cplex`；`Gurobi`／`Generators` 與 `OracleDBCtrl`（真 Oracle 連線）**無任何測試**。

### Templates（repo `Templates/`；全部 net8.0，皆已入 sln）

四個 Templates 都使用 `DataContext` 資料防護。Sudoku 以 `Parameter_Given.csv` 儲存三維純 key parameter（Row/Column/Digit），解答再以一般 C# 規則獨立驗證。

| Template | 結構 | 依賴模式 | 備註 |
|------|------|------|------|
| `Templates/Tutorial/` | `Program.cs` + `Model/` + `Data/` + `SetClass/` + `ParameterClass/` + `VariableClass/` + `Constraint/` + `Generated/` | **ProjectReference → Core+Cplex**；ILOG / Analyzer → `Templates/dlls/` | ★ 權威教學範本；`OptModel` + `OptProject` / `OptExperiment`；`Parameter_Demand` 掛 `[FullGrid]` |
| `Templates/FJSP_BASIC_BRICK/` | `Program.cs` + `Model/` + `Constraint/` + `Data/` + `SetClass/` + `ParameterClass/` + `VariableClass/` + `Generated/` | **ProjectReference → Core+Cplex**；ILOG / Analyzer → `Templates/dlls/` | FJSP 題（積木式）；`timeLimit=90` 為實測可穩定達 `Optimal` 的最小值 |
| `Templates/Template_CPLEX/` | `Program.cs` + `RosteringProblem.cs` + `ExperimentDemo.cs` + `CrossExperiment.cs` + `Constraints/`×12 + `Data/` + `SetClass/` + `VariablesClass/`×11 | `ProjectReference` → Core+Cplex；ILOG → `$(CplexDir)`；Generators 走 repo 內 `Templates/dlls/` | 排班題完整範本；`Random(42)` 固定種子以求可重現；`Data/*.csv` 有 `CopyToOutputDirectory` |
| `Templates/Sudoku_SHC279/` | `Program.cs` + `Data/*.csv` + `SetClass/` + `ParameterClass/` + `VariableClass/` + `Constraint/`×5 + `Solution/` | `ProjectReference` → Core+Cplex；Generators → Analyzer ProjectReference；ILOG → `$(CplexDir)` | `Parameter_Given` 純 key 資料；Program 分別註冊 variables / objective / constraints；729 個 Binary 變數、351 條限制式；CPLEX 實跑 `Optimal` |

> **2026-08-01**：新增 `Sudoku_SHC279`，直接以 ProjectReference 驗證目前工作樹的 Core/Cplex。
> **2026-07-19 收斂**：`FJSP_BASIC`（非積木舊式）、`FeatureTest`、`Template_Gurobi`、`Template_ThreadTest` 已刪除。
> `Template_Solver` 已於 2026-07-10 刪除（依賴跨 repo Solver.dll，無法建置）；其拆檔結構已由 Template_CPLEX 繼承。

### specs / README（git repo `OptimFoundation/OptimFoundation/`）

| 檔案 | 行數 | 主題 |
|------|-----|------|
| `specs/2026-07-10-architecture-consolidation.md` | 43 | 架構整頓基準：Db.Oracle 併 Core、Solver 刪除、版控歸位、TFM net48、csproj 慣例，status: shipped |
| `specs/2026-06-18-experiment-tuning-tracking.md` | 315 | Experiment 套件化 tuning 實驗記錄環境（solver-agnostic），status: shipped |
| `specs/cplex-project-dev-spec.md` | 295 | CPLEX 新建專案：三階段模型、雙 config、對稱 runner、依賴模式、Checklist |
| `specs/developer-guide.md` | 1343 | 框架開發手冊（給下游開發者）；**API 鏡像源頭**：改 public API 必同步 `../AI-Modeling/CPLEX_API_REFERENCE.md` |
| `specs/2026-07-13-optset-basic-objects.md` | 325 | OptSet 積木設計說明（design-approved）：`[OptSet<T>]`/`[OptParam<>]`/`[OptVar<>]` 泛型統一語法、SetBase 契約、命名推導、三檔位讀取——Phase 2 /sdd 靶心 |
| `specs/2026-07-16-io-interface.md` | 182 | **IO 介面權威規格（現況）**：IDataSource/ISolutionSink + Csv/InMemory/Db 三來源（DB query-only）、SetNaming 檔名形式統一、SetBase 載入 + 防呆、Dataload 範式、錯誤語意總表 |
| `specs/framework-dev-spec.md` | 328 | 框架三層架構總覽規格書；project layer 已同步新 model / runner / config API |
| `README.md` | 124 | 專案總覽、Packages 表、Quick Example、DLL 參考設定；**與實際 6 個 src 專案不一致**（見 Review Scope Notes） |

## Dependency Graph

```mermaid
flowchart TD
    subgraph SRC["src/（四專案，全 net8.0；Generators 為 netstandard2.0）"]
        Core["OptimFoundation.Core"]
        Cplex["OptimFoundation.Cplex"]
        Gurobi["OptimFoundation.Gurobi"]
        Generators["OptimFoundation.Generators<br/>(source generator)"]
    end

    subgraph TESTS["tests/（已入 sln，157 tests）"]
        CplexTests["OptimFoundation.Cplex.Tests"]
    end

    subgraph TPL["Templates/（已入 sln，全 net8.0）"]
        TplTutorial["Tutorial<br/>★ 權威範本<br/>(ProjectReference)"]
        TplFjsp["FJSP_BASIC_BRICK<br/>(ProjectReference)"]
        TplCplex["Template_CPLEX<br/>(ProjectReference)"]
        TplSudoku["Sudoku_SHC279<br/>(ProjectReference)"]
    end

    subgraph EXT["外部依賴"]
        ILOG["ILOG.Concert / ILOG.CPLEX<br/>($(CplexDir)，預設 C:\IBM\ILOG\CPLEX_Studio2211)"]
        GRB["Gurobi110.NET<br/>($(GUROBI_HOME) 條件式)"]
        OracleDll["Oracle.ManagedDataAccess.Core 23.9.1"]
        NLogPkg["NLog"]
        JsonPkg["System.Text.Json 8.0.5"]
        Roslyn["Microsoft.CodeAnalysis.CSharp"]
        XUnit["xunit / Microsoft.NET.Test.Sdk"]
    end

    Cplex -->|ProjectReference| Core
    Gurobi -->|ProjectReference| Core
    ProjectConfig["ProjectConfig<br/>project identity / outputs"] --> Core
    CplexConfig["CplexConfig<br/>solver knobs"] --> Cplex
    OptModel["OptModel<br/>model definition"] --> Cplex
    OptProject["OptProject<br/>single-run runner"] --> OptModel
    OptExperiment["OptExperiment<br/>m × n runner"] --> OptModel
    OptProject --> ProjectConfig
    OptExperiment --> ProjectConfig
    OptProject --> CplexConfig
    OptExperiment --> CplexConfig
    Core --> NLogPkg
    Core --> JsonPkg
    Core --> OracleDll
    TplTutorial -->|ProjectReference| Core
    TplTutorial -->|ProjectReference| Cplex
    TplFjsp -->|ProjectReference| Core
    TplFjsp -->|ProjectReference| Cplex
    TplFjsp -.->|"Analyzer DLL"| Generators
    TplSudoku --> Core
    TplSudoku --> Cplex
    TplSudoku --> Generators
    Cplex --> ILOG
    Gurobi -.->|條件式 Exists check| GRB
    Generators --> Roslyn

    CplexTests -->|ProjectReference| Core
    CplexTests -->|ProjectReference| Cplex
    CplexTests -.->|"$(CplexDir)"| ILOG
    CplexTests --> XUnit

    TplCplex -->|ProjectReference| Core
    TplCplex -->|ProjectReference| Cplex
    TplCplex -.->|"$(CplexDir)"| ILOG
```

## Symbol Index

### OptimFoundation.Core

#### `src/OptimFoundation.Core/DesignBases.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `ModelElementBase` | class (abstract) | 8 | 反射快取 `_propsCache`；`InitClassBySets`/`ToString` 是 Variable/Parameter 命名慣例的核心 |
| `ModelElementBase.InitClassBySets` | method | 27 | 依序把可變參數塞進 property，型別不符時嘗試 `Convert.ChangeType` |
| `ModelElementBase.ToString` | method | 50 | 產生 `TypeName@v1@v2...` 格式字串（變數/參數命名的權威來源） |
| `ConstraintBase` | class (abstract) | 66 | |
| `ParameterBase` | class (abstract) | 72 | |
| `VariableBase` | class (abstract) | 77 | |

#### `src/OptimFoundation.Core/EngineBase.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `EngineBase<TModel,TVar,TExpr,TConstr>` | class (abstract) | 14 | Cplex/Gurobi/Solver 共同父類；集中建模統計與 log |
| `Config`/`Status`/`BestObjValue`/`MIPGap` | property | 21-24 | |
| `LastMetrics` | property | 27 | |
| `BuildCount` | class (private) | 32 | 變數/限制式的實際與預期計數 |
| `TryInvokeLong` | method | 46 | telemetry 反射失敗記錄 `TELEMETRY_READ_FAILED` |
| `EnableTrajectory` | method | 65 | virtual no-op，CPLEX override |
| `AddVariables`/`BatchBuild` | method | 162/171 | batch 後自動記錄每個變數型別 `count=實際/預期` |
| `ReadVar`/`GetAllVarNames` | method | 246/264 | |
| `SetVarLB`/`SetVarUB`/`SetVarRange` | method | 304/307/310 | |
| `VarSetsReset` | method | 317 | 同步重設變數統計 |
| `RecordVariableBuild`/`RecordConstraintBuild`/`LogBuildSummary` | method (private) | 339/351/376 | `Solve()` 前自動輸出變數與 constraint group 的實際/預期摘要 |
| `ClearPool` | method | 398 | |
| `AddLHS`（雙多載）/`AddRHS`（雙多載） | method | 427/441/447/461 | null term 記錄 `VARIABLE_NULL` |
| `CreateGreatEqual`/`CreateLessEqual`/`CreateEqual`（各雙多載）/`CreateRange` | method | 471-571 | 自動累計 actual/expected；empty/duplicate 不算 actual |
| `CreateMinimize`/`CreateMaximize` | method | 598/601 | 自動輸出統一的目標式開始/完成事件 |
| `CreateLeSoft`/`CreateGeSoft`/`CreateEqSoft` | method | 646/650/654 | virtual；soft constraint 與彈性變數納入自動統計 |
| `AddObjectiveTerm` | method | 727 | protected virtual |

#### `src/OptimFoundation.Core/Enums.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `VarType` | enum | 3 | Continuous/Integer/Binary |
| `ConstraintSense` | enum | 10 | |
| `ObjectiveSense` | enum | 17 | |

#### `src/OptimFoundation.Core/Experiment.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `Experiment` | class | 8 | |
| `Name`/`Description`/`CreatedAt`/`Trials` | property | 10-13 | |
| `AddTrial` | method | 26 | |
| `Save` | method | 35 | append 邏輯：Load JSON → 去重合併 → 覆寫 CSV+JSON |
| `Load` | method (static) | 56 | |

#### `src/OptimFoundation.Core/ISolverEngine.cs`（含 ISolverConfig / ISpecialConstraints）/ `ITrajectorySource.cs` / `ITunableConfig.cs`

| Symbol | 種類 | 行號 | 檔案 | 說明 |
|---|---|---|---|---|
| `ISolverConfig` | interface | 8 | ISolverEngine.cs | `TimeLimit` / `MipGap` / `Threads` / `ScaleWarnThreshold`；不含專案輸出行為 |
| `ISolverEngine` | interface | 25 | ISolverEngine.cs | `Config`/`Status`/`LastMetrics`/`Build`/`Solve`/`GetObjectiveValue`/`GetVariableValue`/`GetSolution` |
| `SolveStatus` | enum | 69 | ISolverEngine.cs | |
| `ISpecialConstraints<TVar,TExpr>` | interface | 53 | ISolverEngine.cs | SOS1/SOS2/Indicator/LazyConstraint |
| `ITrajectorySource` | interface | 9 | ITrajectorySource.cs | |
| `ITunableConfig` | interface | 7 | ITunableConfig.cs | 8 個抽象旋鈕；三個 concrete config 對映不一致（見 Review Scope Notes） |

#### `src/OptimFoundation.Core/Config/ProjectConfig.cs` / `DataContext.cs` / `OptData.cs`

| Symbol | 種類 | 行號 | 檔案 | 說明 |
|---|---|---|---|---|
| `ProjectConfig` | class (sealed) | 10 | ProjectConfig.cs | 專案身分 / retention / solver log / LP-MPS-Sol 匯出；不進 solver snapshot |
| `ProjectConfig.Clone` | method | 13 | ProjectConfig.cs | `MemberwiseClone()` 強型別 shallow clone |
| `DataContext` | class (abstract) | 87 | DataContext.cs | set / parameter 註冊、驗證 metadata |
| `Freeze` / `GuardMutation` | method | 142 / 145 | DataContext.cs | 僅保護 framework-controlled mutation API；不攔截 public field / mutable List 直接寫入 |
| `OptData.Load` | method (static) | 17 | OptData.cs | factory → `Initialize()` → `Freeze()` → return |

#### `src/OptimFoundation.Core/VariableBuilder.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `VariableBuilder` | class (static) | 10 | 含 `_ctorCache`（ConcurrentDictionary，thread-safe） |
| `GenVarCombinations` | method | 62 | |
| `ConvertSetsToStringLists` | method | 74 | 支援 DateTime/int/long/double/decimal/string/enum |
| `GetVarNames<TVariable>` | method | 112 | |
| `BuildVars<TVariable>` | method | 121 | |

#### `src/OptimFoundation.Core/Csv/CsvCtrl.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `CsvCtrl` | class (static) | 9 | |
| `ClearData` | method | 16 | |
| `ReadIntSet`/`ReadDoubleSet`/`ReadStrSet`/`ReadDateSet` | method | 31-34 | |
| `ReadMatrixCsv` | method | 85 | |

#### `src/OptimFoundation.Core/IO/DBCtrlBase.cs` / `IDbCtrl.cs`

| Symbol | 種類 | 行號 | 檔案 | 說明 |
|---|---|---|---|---|
| `DBCtrlBase` | class (abstract) | 6 | DBCtrlBase.cs | `: IDbCtrl` |
| `NonQuery` | method | 21 | DBCtrlBase.cs | virtual，預設委派 `Execute` 並丟棄筆數 |
| `Dispose` | method | 24 | DBCtrlBase.cs | virtual，呼叫 `Close()` |
| `IDbCtrl` | interface | 12 | IDbCtrl.cs | `: IDisposable`；完整中文 doc comment |

#### `src/OptimFoundation.Core/Experiments/*.cs`

| Symbol | 種類 | 行號 | 檔案 | 說明 |
|---|---|---|---|---|
| `ConfigSnapshot` | class (sealed) | 10 | ConfigSnapshot.cs | `Solver`/`Tunable`/`SolverSpecific` |
| `ConfigSnapshot.From` | method (static) | 19 | ConfigSnapshot.cs | reflection 抓 concrete config 專屬欄位；getter 失敗時記錄 `CONFIG_SNAPSHOT_SKIPPED` |
| `CsvExperimentWriter` | class (sealed) | 11 | CsvExperimentWriter.cs | `: IExperimentWriter`；輸出 UTF-8 **with BOM**（供 Excel zh-TW，見說明） |
| `Write`/`Read` | method | 22/67 | CsvExperimentWriter.cs | `Read` 恆回 null（CSV 非權威來源，只寫不讀） |
| `IExperimentWriter` | interface | 14 | IExperimentWriter.cs | |
| `ExpWriterType` | enum | 4 | IExperimentWriter.cs | CSV/JSON |
| `JsonExperimentWriter` | class (sealed) | 13 | JsonExperimentWriter.cs | `: IExperimentWriter`；輸出 UTF-8 **無 BOM**（與 Csv 版 BOM 策略不同，屬刻意設計） |
| `SolveMetrics` | class (sealed) | 9 | SolveMetrics.cs | |
| `ConvergencePoint` | class (sealed) | 26 | SolveMetrics.cs | |
| `Trial` | class (sealed) | 8 | Trial.cs | |
| `Trial.Capture` | method (static) | 24 | Trial.cs | 不接管/不 Dispose engine，呼叫端持有生命週期 |

#### `src/OptimFoundation.Core/Infrastructure/*.cs`

| Symbol | 種類 | 行號 | 檔案 | 說明 |
|---|---|---|---|---|
| `ClassInfo` | class | 6 | ClassInfo.cs | SQL insert/create 語句產生器 |
| `FolderDir` | class | 6 | FolderDir.cs | 靜態 `ProjFolder` 實例：Data/Solution/Log/Model/IIS/Sol/Experiment |
| `FolderDir.PurgeOutputs` | method (static) | 20 | FolderDir.cs | 清各輸出資料夾（不含 Data）超過 N 天舊檔；`retentionDays<=0` 關閉。由 `OptProject.Execute()` 依有效設定呼叫；實驗不做 housekeeping |
| `FolderDir.ProjFolder` | class (nested) | 28 | FolderDir.cs | `ProjectPath` 綁 `AppDomain.CurrentDomain.BaseDirectory` |
| `FolderDir.ProjFolder.PurgeOlderThan` | method | 72 | FolderDir.cs | 刪本資料夾中 LastWriteTime 早於 N 天前的檔；使用中/無權限會彙總記錄 `OUTPUT_PURGE_SKIPPED` |
| `ReflectionHelper` | class (static) | 11 | ReflectionHelper.cs | `OracleTypeMap` 私有對照表；`GetMemberNames`/`GetMemberTypes`/`GenerateSQLCols` |

#### `src/OptimFoundation.Core/Logging/Logging.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `Logging` | class (static) | 8 | **全域可變靜態狀態**：`_fileWriter`/`_logFile` 可變且無 DI，多執行緒（如 Template_ThreadTest）共用同一個 log 檔／`Console.OutputEncoding` |
| `FileWriter` | property (static, private) | 27 | lazy：首次寫入才開檔，避免孤兒 log；呼叫端須持有 `_lock` |
| `Write` | method (static, private) | 40 | `yyyy-MM-dd HH:mm:ss \| LEVEL \| message`；秒精度，不自動附 namespace |
| `Info`/`Debug`/`Warn`/`Error` | method (static) | 51-54 | |
| `SetLogFileName` | method (static) | 63 | sanitize 非法檔名字元後換 log 檔路徑（lazy，寫入時才開檔） |
| `WriteToFile` | method (static) | 76 | |
| `ClearLogs` | method (static) | 82 | 先 dispose 目前 writer 再刪檔 |

### OptimFoundation.Cplex

#### `src/OptimFoundation.Cplex/CplexConfig.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `CplexConfig` | class (sealed) | 9 | `: ISolverConfig, ITunableConfig`；只含 camelCase CPLEX 旋鈕與 interface adapters |
| `Clone` | method | 12 | `MemberwiseClone()` 強型別 shallow clone |
| `TimeLimit`/`MipGap`/`Threads`/`RootAlgorithm` | property | 148-154 | delegate 到對應 solver 欄位 |
| `NodeAlgorithm`/`PreIndicator` | property | 159/162 | CPLEX 延伸項 |
| `Seed`/`Emphasis`/`FeasibilityTol`/`OptimalityTol`/`MemoryLimitMb` | property | 168-180 | `ITunableConfig` 對映 |
| `Presolve` | property | 185 | int?↔bool? 轉換（0/非0 ↔ PreIndicator） |
| `HeuristicEffort` | property | 195 | solver tuning property |

#### `src/OptimFoundation.Cplex/OptEngine.cs`（1142 行）

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `OptEngine` | class | 20 | `: EngineBase<Cplex,INumVar,ILinearNumExpr,IRange>` |
| `OptEngine(CplexConfig)` / `OptEngine(CplexConfig, ProjectConfig)` | ctor | 42 / 45 | 舊 ctor 使用預設 project config；新 ctor 明確分層 |
| `EnableTrajectory` | method | 74 | |
| `TrajectoryCallback` | class (nested, private) | 80 | `Cplex.MIPInfoCallback` |
| `Configuration` | method | 131 | |
| `SetModelName` | method | 579 | |
| `AddVariable`/`AddVariables` | method | 591 / 608 | override |
| `LinearExpr`/`AddConstraint`/`AddRangeConstraint`/`SetObjective`/`SetVariableBounds` | method | 637 / 647 / 662 / 677 / 686 | override |
| `BuildCore`/`SolveCore`/`FlushSolverLog`/`GetObjectiveValue`/`GetVariableValue`/`Dispose` | method | 700 / 709 / 792 / 809 / 812 / 815 | solver log 依 project config 控制 console，框架 log 仍持久化 |
| `CreateGreatEqualThread` | method | 918 | 跨引擎執行緒同步約束，高複雜度／高風險 |
| `CopyModel`/`MergeModel` | method | 960 / 991 | 多 `OptEngine` 實例間複製/合併模型 |
| `GetConflictConstraints` | method | 1050 | |
| `TeeWriter` | class (nested, private) | 1090 | `TextWriter` |

#### `src/OptimFoundation.Cplex/OptModel.cs` / `OptProject.cs` / `OptExperiment.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `OptModel` | class (sealed) | 11 | 純模型定義，不持有 engine / config / runner 狀態 |
| `AddVariables` / `AddObjective` / `AddConstraints` | method | 27 / 34 / 41 | 可各註冊多次；`ApplyTo` 固定三階段順序 |
| `OptProject` | class (sealed) | 9 | `: IDisposable`；單一模型、雙 config、housekeeping、成功 callback |
| `UseConfig(ProjectConfig)` / `UseConfig(CplexConfig)` / `OnSolved` | method | 28 / 35 / 42 | 只有 project runner 有 `OnSolved` |
| `OptProject.Execute` | method | 64 | 解析 effective config → engine build → model apply → solve → callbacks |
| `OptExperiment` | class (sealed) | 9 | 笛卡兒積 + explicit cells；每 cell fresh engine；自動 save |
| `UseConfig` / `AddModel` / `AddConfig` / `AddTrial` | method | 36 / 43 / 51 / 62 | config label 不可重複 |
| `OptExperiment.Run` | method | 72 | label `model | config`；預設 log / exports OFF，無 housekeeping / `OnSolved` |

### OptimFoundation.Gurobi

#### `src/OptimFoundation.Gurobi/GurobiConfig.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `GurobiConfig` | class (sealed) | 5 | `: ISolverConfig, ITunableConfig` |
| `TimeLimit`/`MipGap`/`Threads`/`LogToConsole`/`LogFilePath` | property | 8-12 | |
| `Method`/`Presolve`/`MipFocus`/`Seed`/`FeasibilityTol`/`OptimalityTol`/`Heuristics`/`SoftMemLimit` | property | 15-22 | Gurobi 專屬 |
| `ExportLp`/`ExportMps`/`ExportSol`/`ProjectName` | property | 25-28 | |
| `LicenseId`/`WlsAccessId`/`WlsSecret` | property | 31-33 | **Gurobi 專有，CplexConfig/Solver 版 GurobiConfig 都沒有** |
| `Emphasis`/`RootAlgorithm`/`HeuristicEffort` | property | 37-39 | delegate（get/set 轉呼叫其他 property） |

#### `src/OptimFoundation.Gurobi/OptEngine.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `OptEngine`（真實實作） | class | 20 | `#if GUROBI_INSTALLED`；`: EngineBase<GRBModel,GRBVar,GRBLinExpr,GRBConstr>` |
| `Configuration`/`AddVariable`/`AddVariables`/`LinearExpr`/`AddConstraint`/`SetObjective`/`SetVariableBounds` | method | 35-171 | override |
| `Build`/`Solve`/`GetObjectiveValue`/`GetVariableValue`/`Dispose` | method | 181-249 | |
| `Update`/`CreateVar`/`Expr`/`AddLE`/`AddGE`/`AddEQ`/`Minimize`/`Maximize` | method | 262-281 | |
| `AddRangeConstraint`/`AddLhs`/`AddRhs`/`CommitLE`/`CommitGE`/`CommitEQ`/`CommitRange` | method | 283-371 | |
| `OptEngine`（stub） | class | 386 | `#else`；`: EngineBase<object,object,object,object>`，所有方法 `throw new NotSupportedException("Gurobi DLL 未安裝")` |

### OptimFoundation.Core — OracleDBCtrl（原 Db.Oracle 專案，2026-07-10 併入）

#### `src/OptimFoundation.Core/IO/OracleDBCtrl.cs`（namespace 保留 `OptimFoundation.Db.Oracle`）

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `OracleDBCtrl` | class (sealed) | 11 | `: DBCtrlBase`；建構子直接 `base(connectionString)` |
| `Open`/`Close` | method | 18-19 | 刻意留空（每次操作自建 connection，靠 Oracle Connection Pool） |
| `Query`/`Execute`/`QueryScalar<TResult>` | method | 21/31/40 | override |
| `CreateConnection`/`BuildCommand` | method (private) | 52/59 | |
| `BuildConnectionString` | method (static) | 72 | |
| `CheckHasTable`/`DropTable`/`DeleteTable`/`TruncateTable` | method | 86/117/129/140 | |
| `ReadStrSet`/`ReadDoubleSet`/`ReadIntSet`/`ReadDateSet`/`LoadSet` | method | 147-152 | |
| `ConvertToDbType` | method (static, internal) | 330 | 無效或不支援的轉型記錄 `ORACLE_CONVERSION_FAILED` 並 fail fast，禁止靜默寫入 `NULL` |

### OptimFoundation.Generators

#### `src/OptimFoundation.Generators/AutoSetsGenerator.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `AutoSetsGenerator` | class (sealed) | 29 | `[Generator]`，`: IIncrementalGenerator`；消費者含 `Templates/FJSP_BASIC_BRICK` / `Tutorial`（Analyzer DLL）、Sudoku（Analyzer ProjectReference）與 sibling AI-Modeling |
| `VarType`（注入用，字串常數形式） | enum | 44 | 生成到使用端的 `OptimFoundation.Modeling` namespace |
| `OptVarAttribute` | class (sealed) | 47 | |
| `OptParamAttribute` | class (sealed) | 59 | |
| `Initialize` | method | 76 | `IIncrementalGenerator` 入口 |
| `EmitModel` | record (private, nested) | 246 | |

#### `src/OptimFoundation.Generators/IsExternalInit.cs`

| Symbol | 種類 | 行號 | 說明 |
|---|---|---|---|
| `IsExternalInit` | class (internal, static) | 4 | netstandard2.0 polyfill，允許 `init` 存取子 |

## Review Scope Notes

!!! warning 高風險區域（未解決）
    1. **同檔內兩個同名 public class**：`src/OptimFoundation.Gurobi/OptEngine.cs` 在 `#if GUROBI_INSTALLED`/`#else` 兩側各定義一個 `public class OptEngine`（真實實作 vs. 全 throw 的 stub），互斥編譯，屬刻意設計但審查時需注意兩份簽章是否保持同步。
    2. **`ITunableConfig` 實作對映不一致**：`CplexConfig`（含 `NodeAlgorithm`/`PreIndicator`，`Presolve` 是計算 property）與 `GurobiConfig`（含 `LicenseId`/`WlsAccessId`/`WlsSecret`）對旋鈕的對映方式（直接欄位 vs. delegate get/set）不統一，`ConfigSnapshot.From` 靠 reflection 補差異。
    3. **全域可變靜態狀態**：`OptimFoundation.Core.Logging`（`_fileWriter`/`_logFile`）與 `ModelElementBase`/`VariableBuilder` 的靜態快取字典為 process 全域共享；目前 experiment 序列執行，若未來平行化需先處理交錯輸出與換檔競爭。
    4. **Freeze 邊界**：`DataContext.Freeze` 只守 framework-controlled mutation API；既有 public field / mutable List 直接寫入無法由 base class 即時攔截。
    5. **測試覆蓋率缺口**：只有 `OptimFoundation.Cplex.Tests`（覆蓋 Core+Cplex）；`Gurobi`/`Generators` 與 `OracleDBCtrl`（真 Oracle 連線 runtime）無完整整合測試。
    6. **README.md 未提及 `OptimFoundation.Generators`**：Packages 表缺此專案。

!!! done 已解決（2026-07-13 事實同步）
    - ~~`OptimFoundation.Generators` 孤兒專案~~ → 已接線：FJSP_BASIC（Analyzer DLL）與 AI-Modeling HospitalRostering 系列消費
    - ~~全鏈 net48~~ → 2026-07-11 就地遷移 `net8.0`（Oracle 換 `Oracle.ManagedDataAccess.Core` 23.9.1；CPLEX/Gurobi managed wrapper 直接載），build 全綠 + 73 tests 通過

!!! done 已解決（2026-08-02）
    - ~~Tutorial / FJSP Core+Cplex 指向 `Templates/dlls/`，容易被 stale DLL 遮住 API drift~~ → 兩個 template csproj 已改為 Core+Cplex `ProjectReference`；只有 ILOG 與 generator analyzer 保留 DLL reference
    - ~~模型定義與執行 runner 混在單一型別~~ → `OptModel` 純定義，`OptProject` / `OptExperiment` 對稱分工

!!! done 已解決（2026-07-10 架構整頓，commits `d49aee4`→`dd876db`）
    - ~~跨 repo 建置依賴（MILP Solver/Solver.dll）~~ → OptimFoundation.Solver 與 Template_Solver 已刪除
    - ~~絕對路徑 HintPath（tests 的 C:\IBM\...）~~ → 統一 `$(CplexDir)` property
    - ~~ProjectReference vs HintPath 混用~~ → 全部 `ProjectReference`，solver DLL 走 property
    - ~~TargetFramework 三分天下~~ → 全鏈 `net48`（Generators 為 netstandard2.0，Roslyn 要求）
    - ~~Solver 專案檔名/類別名複製殘留~~ → 專案已刪除
    - ~~Templates 三套結構不對等~~ → Template_Solver 刪除後 CPLEX/Gurobi 同構（皆有 Constraints/Data/VariablesClass 拆檔）
    - ~~Templates 與 tests 不受版控 / repo 內過期副本~~ → 已收進 git repo，過期副本刪除
    - ~~sln 覆蓋不全~~ → 9 專案全入 sln
    - ~~README 幽靈 `OptimFoundation.Db` 列~~ → 已修正
    - ~~legacy packages.config / App.config / vendored packages/~~ → 已刪除
    - ~~System.Text.Json 8.0.0 已知弱點（NU1903）~~ → 升 8.0.5

!!! info 略過範圍
    - 所有 `obj/`、`bin/`、`.vs/` 目錄與其中的 generated `AssemblyInfo`/`AssemblyAttributes`
    - `docs/index.html`（單一靜態頁面，未展開內容）
