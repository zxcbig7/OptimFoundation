# OptimFoundation Code Map

> 同步日期：2026-08-09

## Solution

| 路徑 | 責任 |
| --- | --- |
| `src/OptimFoundation.Core/` | solver-neutral 核心、IO、naming、logging、experiments |
| `src/OptimFoundation.Generators/` | Set / Parameter / Variable source generator |
| `src/OptimFoundation.Cplex/` | CPLEX adapter、model/project/experiment |
| `src/OptimFoundation.Gurobi/` | Gurobi adapter 與 config |
| `tests/OptimFoundation.Cplex.Tests/` | Core、generator、Cplex adapter tests |
| `Templates/` | 可 build 的現行 API 範例 |
| `specs/` | 開發規格與架構決策 |
| `docs/` | 對外說明頁 |

## Core

| 檔案 | 主要型別 / 功能 |
| --- | --- |
| `DesignBases.cs` | `ModelElementBase`、`SetRowBase`、`ParameterBase`、`VariableBase`、`ConstraintBase`、`Numeric` |
| `ModelNaming.cs` | `@` key、`yyyy_MM_dd` 日期與 token 驗證 |
| `VariablePrefixNaming.cs` | B/C/I 前綴單一解析規則；linked 到 Generator |
| `EngineBase.cs` | variable builders、pool、constraint/objective、soft constraints、solution query、logging boundary |
| `VariableBuilder.cs` | primitive domain / `SetRowBase` domain 展開、笛卡兒積、arity 驗證 |
| `ISolverEngine.cs` | solver-neutral engine surface |
| `DataContext.cs` | `OptData.Load`、Parameter registration、freeze |
| `DataValidator.cs` | Parameter duplicate-key 與 numeric sanity |
| `Logging/Logging.cs` | structured logging、`ErrorOnce` |

## IO

| 檔案 | 功能 |
| --- | --- |
| `IO/IDataSource.cs` | `IDataSource.LoadData`、共用 `Load<T>`、`ISolutionSink`、batch |
| `IO/ModelRowMapper.cs` | `ModelRowMapper`：DataTable → Set/Parameter model row |
| `IO/TabularData.cs` | header-based records ↔ DataTable |
| `IO/csv/CsvDataSource.cs` | CSV source 與 CSV solution sink |
| `IO/csv/CsvCtrl.cs` | RFC4180 parse、`WriteRows`、`WriteSolution` |
| `IO/InMemoryDataSource.cs` | `AddRows` + `LoadData` |
| `IO/db/DbDataSource.cs` | query-only `IDataSource`，支援 bind parameters overload |
| `IO/db/IDbCtrl.cs` | DB controller abstraction |
| `IO/db/OracleDBCtrl.cs` | Oracle controller |

## Generator

`AutoSetsGenerator.cs` 注入：

- `[OptSet]`：至少一個 primitive `OptDim`，生成 `SetRowBase` row。
- `[OptParam]`：零到多個 primitive `OptDim`，生成 `ParameterBase` row 與 `QTY`。
- `[OptVar]`：零到多個 primitive `OptDim`，由 B/C/I 前綴決定型別。
- `DataContext` partial subclass 的 Parameter registration。

Diagnostics：`OPTF001`、`OPTF002`、`OPTF003`、`OPTF006`、`OPTF007`、`OPTF008`。

## CPLEX

| 檔案 | 功能 |
| --- | --- |
| `src/OptimFoundation.Cplex/OptEngine.cs` | CPLEX variable/constraint/objective primitive 與 solve |
| `OptModel.cs` | Variables → Objective → Constraints 的模型組裝 |
| `OptProject.cs` | config、engine lifecycle、execute、OnSolved |
| `OptExperiment.cs` | model × config experiment matrix |
| `CplexConfig.cs` | solver-neutral + CPLEX-specific config |

## Templates

| 範例 | 重點 |
| --- | --- |
| `Templates/Tutorial/` | 一維 Set、Parameter、B/C/I Variable、標準組裝 |
| `Templates/TSP_MultiDimSet/` | 多維 Set 稀疏 domain |
| `Templates/Sudoku_SHC279/` | 多維 Set、scalar Parameter、raw import → `WriteRows` |
| `Templates/RosteringProblem/` | 排班、soft/複雜限制式 |
| `Templates/FJSP_BASIC_BRICK/` | 既有專案識別名稱；內部程式已使用現行 row API |

## 規格

| 文件 | 用途 |
| --- | --- |
| `specs/developer-guide.md` | 現行 API 與框架維護指南 |
| `specs/framework-dev-spec.md` | Core/adapter 開發契約 |
| `specs/cplex-project-dev-spec.md` | CPLEX consumer 專案規格 |
| `specs/2026-08-08-multidim-set.md` | 多維 Set 最終設計 |
| `specs/2026-08-08-io-read-surface.md` | IO 最終設計 |
| `specs/2026-08-09-api-architecture-audit.md` | 現況盤查與待決策問題 |
| `specs/2026-08-09-datetime-naming-unification.md` | 命名、constraint overload 與 logging 變更規格 |

## Build

```powershell
dotnet build OptimFoundation.sln
dotnet test tests/OptimFoundation.Cplex.Tests/OptimFoundation.Cplex.Tests.csproj
```
