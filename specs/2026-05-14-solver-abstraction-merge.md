---
title: 整合 OMG_Foundation 與 OMG_MILP 進 OptimFoundation 並建立 Solver 抽象層
status: approved
created: 2026-05-14
updated: 2026-05-14
modules: [backend, db]
---

# OptimFoundation Solver 抽象化重構

## Summary

把現存三個 optimization framework 專案（`OMG_Foundation`、`OMG_MILP Development APIs`、`OptimFoundation`）的功能合併進 `OptimFoundation`，並重新設計為「solver-agnostic」抽象層：Core 不依賴任何 solver DLL，CPLEX 與 Gurobi 各自為可抽換的 engine 實作。設計階段同時支援兩者，但因 CPLEX 與 Gurobi DLL 無法共存，編譯時擇一。

## Motivation / Why

- 目前三個專案功能重疊但版本不一（OMG_MILP 是 May 2026 最新功能版、OptimFoundation 是 May 2026 重構版但缺 Gurobi/DB/Interfaces、OMG_Foundation 是 Jul 2025 完整版）
- 三版檔名/namespace 不一致（`OMG.CplexEngine` vs `Foundation.CplexEngine`、`CplexEngine.cs` vs `CPLEX_Engine.cs`）造成維護成本
- 既有 `Interfaces.cs` 抽象不完整，無法乾淨抽換 solver
- `OMG_Foundation(CPLEX only).cs` 是 Nov 2024 的單檔舊版（1013 行、三專案內容完全相同），但內含 `CSVControl.read_matrix_csv()` 等獨家功能未被新版檔吸收
- 物件耦合過深（如 `DBCtrl` 直接 reference 特定 solver type）

## Scope

### In Scope

- 統一三專案功能進 `OptimFoundation`，移除 `OMG_Foundation/`、`OMG_MILP Development APIs/` 兩專案資料夾（功能搬完後）
- 設計 solver 抽象層：`ISolverEngine` 介面 + `EngineBase<TModel, TVar, TExpr, TConstr>` 抽象基類
- 把專案拆為：`OptimFoundation.Core`（solver-agnostic）、`OptimFoundation.Cplex`、`OptimFoundation.Gurobi`（兩個 engine project，使用方擇一引用）
- DB 層保留 Oracle 特化（採 OMG_MILP 的 `DBCtrlBase` + Oracle `DBCtrl`）
- 把 `OMG_Foundation(CPLEX only).cs` 的獨家功能（`CSVControl.read_matrix_csv()`、`Solver<T>` 若有獨特行為）合進新檔後刪除該舊單檔
- Namespace 統一為 `OptimFoundation.*`
- `.csproj` / `.sln` 改名為 `OptimFoundation.*`

### Out of Scope

- Gurobi engine 的實作（只建 stub + interface，不裝 Gurobi DLL）
- 其他 solver（SCIP、HiGHS、COIN-OR）的實作（介面預留即可）
- 既有上層呼叫端的遷移（這份規格只動 framework 本身，呼叫端調整另開 spec）
- UI / 前端
- 單元測試補齊（之後逐層補）

## User Stories / Use Cases

1. **As a** optimization developer，**I want to** 引用 `OptimFoundation.Cplex` 一個 NuGet/project，**so that** 不需要連 Gurobi DLL 也能 build 出 CPLEX-only 應用。
2. **As a** developer，**I want to** 切換到 Gurobi，**so that** 只需把 `.Cplex` 換成 `.Gurobi` reference、不改業務 code 即可重新 build。
3. **As a** developer，**I want to** 在 Core 層寫 solver-agnostic 的 model 組裝邏輯，**so that** 演算法可重用於不同 solver。
4. **As a** maintainer，**I want to** 三個專案的功能集中在單一 repo，**so that** 不再有版本漂移問題。

## Acceptance Criteria

- [ ] `OptimFoundation.Core` project 完全不 reference `cplex*.dll`、`gurobi*.dll`，可獨立 build
- [ ] `OptimFoundation.Cplex` 實作 `ISolverEngine`（沿用 OMG_MILP 的 CPLEX engine 邏輯）
- [ ] `OptimFoundation.Gurobi` 只有 stub（interface + `throw new NotImplementedException()`），不需安裝 Gurobi DLL 也能 build（透過 conditional compile 或拆 project 達成）
- [ ] `OptimFoundation.Core` 含 `EngineBase<TModel, TVar, TExpr, TConstr>` 抽象基類與 `ISolverEngine` 介面
- [ ] DB 層 `DBCtrlBase`（Oracle-agnostic 邏輯） + `OracleDBCtrl`（Oracle 特化）拆分清楚
- [ ] Namespace 全部統一為 `OptimFoundation.{Core, Cplex, Gurobi, Db, Csv, Logging}`
- [ ] `OMG_Foundation(CPLEX only).cs` 已刪除；其獨家功能（`CSVControl.read_matrix_csv()` 等）已合進對應新檔
- [ ] `OMG_Foundation/` 與 `OMG_MILP Development APIs/` 兩資料夾可安全刪除（保留為 git history）
- [ ] `dotnet build` 在「只裝 CPLEX」與「都不裝 solver、只 build Core」兩情境下皆通過
- [ ] 產出 `MERGE-DECISIONS.md`：每個檔案來源（哪版本勝出、為什麼）、刪除哪些檔、合進哪些檔的對照表

## Module Interactions

### 新專案結構

```
OptimFoundation/
├── OptimFoundation.sln
├── src/
│   ├── OptimFoundation.Core/          # solver-agnostic
│   │   ├── ISolverEngine.cs           # 抽象介面
│   │   ├── EngineBase.cs              # 泛型抽象基類
│   │   ├── ISpecialConstraints.cs
│   │   ├── ISolverConfig.cs
│   │   ├── VariableManager.cs         # 採 OMG_MILP 版
│   │   ├── DesignBases.cs             # 採 OptimFoundation 版（最精簡）
│   │   ├── Core/
│   │   │   ├── FolderDir.cs
│   │   │   ├── ReflectionHelper.cs
│   │   │   └── ClassInfo.cs
│   │   ├── Csv/CSVCtrl.cs             # 採 OMG_MILP 版 + (CPLEX only) 的 read_matrix_csv
│   │   ├── Logging/Log.cs             # 採 OMG_MILP 版
│   │   └── Db/
│   │       ├── IDbCtrl.cs
│   │       └── DBCtrlBase.cs          # 採 OMG_MILP DBCtrlBase
│   ├── OptimFoundation.Db.Oracle/
│   │   └── OracleDBCtrl.cs            # Oracle 特化（採 OMG_MILP DBCtrl）
│   ├── OptimFoundation.Cplex/
│   │   ├── CplexConfig.cs             # 採 OMG_MILP CplexSovlerConfig（修拼字）
│   │   └── CplexEngine.cs             # implements ISolverEngine
│   └── OptimFoundation.Gurobi/
│       ├── GurobiConfig.cs
│       └── GurobiEngine.cs            # stub only
└── specs/
    └── 2026-05-14-solver-abstraction-merge.md
```

### 相依關係

- `Core` → 無 solver dependency
- `Db.Oracle` → `Core` + `Oracle.ManagedDataAccess`
- `Cplex` → `Core` + `ILOG.CPLEX`
- `Gurobi` → `Core` + (stub，不引 Gurobi DLL；implementation 階段才補)
- 使用方專案 → `Core` + `Db.Oracle` + 擇一 engine

## API Design

### `ISolverEngine`（精簡公用介面）

```csharp
namespace OptimFoundation.Core;

public interface ISolverEngine
{
    void Build();
    bool Solve();
    double GetObjectiveValue();
    double GetVariableValue(string name);
    void Dispose();
}
```

### `EngineBase<TModel, TVar, TExpr, TConstr>`（泛型抽象基類，沿用 OMG_MILP 設計）

```csharp
namespace OptimFoundation.Core;

public abstract class EngineBase<TModel, TVar, TExpr, TConstr> : ISolverEngine
{
    protected TModel Model;
    protected ISolverConfig Config;

    protected abstract TVar AddVariable(string name, double lb, double ub, VarType type);
    protected abstract TExpr LinearExpr(IEnumerable<(double coef, TVar var)> terms);
    protected abstract TConstr AddConstraint(TExpr lhs, ConstraintSense sense, double rhs);

    public abstract void Build();
    public abstract bool Solve();
    public abstract double GetObjectiveValue();
    public abstract double GetVariableValue(string name);
    public abstract void Dispose();
}

public enum VarType { Continuous, Integer, Binary }
public enum ConstraintSense { LE, EQ, GE }
```

### `ISolverConfig`

```csharp
public interface ISolverConfig
{
    double? TimeLimit { get; set; }
    double? MipGap { get; set; }
    int? Threads { get; set; }
    bool LogToConsole { get; set; }
}
```

### `IDbCtrl`

```csharp
public interface IDbCtrl : IDisposable
{
    void Open();
    void Close();
    DataTable Query(string sql, params (string name, object value)[] parameters);
    int Execute(string sql, params (string name, object value)[] parameters);
}
```

## Data Model

不動 DB schema。沿用既有 Oracle table 結構（OMG_MILP 的 DBCtrl 中已封裝），相關 schema 在 `OracleDBCtrl` 內部處理。

## Edge Cases & Error Handling

- **CPLEX 與 Gurobi DLL 同裝衝突**：透過拆 project + 個別引用 NuGet/DLL 隔離；使用方只引一個 engine project，build output 不會同時含兩家 DLL
- **`OMG_Foundation(CPLEX only).cs` 獨家功能漏搬**：合併前產出 `MERGE-DECISIONS.md` 對照表，逐項打勾
- **舊呼叫端使用 `OMG.CplexEngine.OptEngine`**：屬於 out-of-scope，但 namespace 改名要在 release notes 標註破壞性變更
- **DB 連線字串相容性**：`OracleDBCtrl` 沿用既有連線字串格式（SID/SERVICE_NAME 邏輯），無需 migration
- **Gurobi stub 被誤用**：在 stub method 內 `throw new NotImplementedException("Gurobi engine not yet implemented")` 並在 README 標註

## Non-Functional Requirements

- **Build**：Core project 不需任何 solver 環境變數即可 build；CI 可只 build Core 做型別檢查
- **Coupling**：Core 對外只暴露 `ISolverEngine` / `IDbCtrl` 介面；engine 具體型別不外洩
- **Naming**：檔名與 class 採 `PascalCase`（例：`CplexEngine.cs`、不再混用 `CPLEX_Engine.cs`、`OMG_Foundation(CPLEX only).cs` 等舊命名）
- **拼字修正**：把舊 code 的 `ISovlerConfig`、`ISpecialConstrints`、`CplexSovlerConfig` 拼字錯誤一併修正
- **Logging**：Engine 內部 log 透過 `Log`（OMG_MILP 版），不直接 `Console.WriteLine`

## Open Questions

- [ ] **單一 .csproj 還是拆多 project？** 規格目前設計為「拆多 project」（推薦，最自然解 DLL 衝突）。若你偏好維持「單一 .csproj + `#if CPLEX / #if GUROBI` conditional compile」也可，請確認。
- [ ] **Gurobi 是否要先擺 stub 接口？** 目前規格列為 stub-only（介面齊全、實作 throw）。若 Gurobi 永遠不會用可從 spec 拿掉。
- [ ] **`OMG_Foundation(CPLEX only).cs` 內的 `Solver<T>` class 是否仍在被外部呼叫？** 若是，需保留相容 wrapper；若否（合進新 engine 後即可），規格傾向直接刪除。

## Implementation Plan

### Stub 階段（先做，approve 後執行）

- [ ] 建立新資料夾結構 `OptimFoundation/src/{Core, Db.Oracle, Cplex, Gurobi}/`
- [ ] 新建 `OptimFoundation.sln` 含 4 個 project（Core、Db.Oracle、Cplex、Gurobi）
- [ ] `Core` 建立 `ISolverEngine.cs`、`EngineBase.cs`、`ISolverConfig.cs`、`ISpecialConstraints.cs`、`IDbCtrl.cs`（只有 interface + 抽象 method 簽名，body throw `NotImplementedException` 或留 `// TODO`）
- [ ] `Cplex/CplexEngine.cs` 建 class 繼承 `EngineBase<...>`，所有 override method body 為 `throw new NotImplementedException("TODO: 規格 API Design 段")`
- [ ] `Gurobi/GurobiEngine.cs` 同上，但連 ILOG using 都不放
- [ ] `Db.Oracle/OracleDBCtrl.cs` 建 class 實作 `IDbCtrl`，body TODO
- [ ] 跑 `dotnet build` 確認結構連得起來、4 個 project 互相引用正確
- [ ] 產出 `MERGE-DECISIONS.md` 初稿（檔案來源對照表）

### 逐層實作（stub 後依序）

- [ ] **Layer 1: 基礎模組搬入**（無 solver dependency）
  - [ ] `VariableManager.cs`（採 OMG_MILP 版）
  - [ ] `DesignBases.cs`（採 OptimFoundation 版）
  - [ ] `Core.cs` 拆成 `FolderDir.cs`、`ReflectionHelper.cs`、`ClassInfo.cs`（採 OptimFoundation 版含 logging）
  - [ ] `CSVCtrl.cs`（採 OMG_MILP + 合入 `read_matrix_csv`）
  - [ ] `Log.cs`（採 OMG_MILP 版）
- [ ] **Layer 2: DB**
  - [ ] `DBCtrlBase.cs`（採 OMG_MILP 新增的 base class）
  - [ ] `OracleDBCtrl.cs`（採 OMG_MILP 的 DBCtrl + OMG_Foundation 的 Oracle 連線細節）
- [ ] **Layer 3: CPLEX engine**
  - [ ] `CplexConfig.cs`（採 OMG_MILP `CplexSovlerConfig`，修正拼字）
  - [ ] `CplexEngine.cs`（採 OMG_MILP CPLEX engine 邏輯，套用新 namespace 與抽象基類）
- [ ] **Layer 4: Gurobi engine**
  - [ ] 保留 stub，文件標註「待之後實作」
- [ ] **Layer 5: 清理**
  - [ ] 合入 `OMG_Foundation(CPLEX only).cs` 獨家功能後刪除
  - [ ] 刪除 `OMG_Foundation/`、`OMG_MILP Development APIs/` 兩資料夾
  - [ ] 補 README、`MERGE-DECISIONS.md` 完成版

## References

- 比對來源：`OMG_Foundation/`、`OMG_MILP Development APIs/`、`OptimFoundation/`（同 repo root）
- 受影響的既有規格：無（此為第一份 spec）
