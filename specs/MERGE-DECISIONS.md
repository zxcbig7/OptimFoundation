# Merge Decisions — OptimFoundation Solver Abstraction

Spec: [2026-05-14-solver-abstraction-merge.md](2026-05-14-solver-abstraction-merge.md)
狀態：stub 階段完成，逐層搬入中。

## 檔案來源對照表

| 新位置                                                                  | 採用來源                                                           | 原因                                                      | 狀態      |
| ----------------------------------------------------------------------- | ------------------------------------------------------------------ | --------------------------------------------------------- | --------- |
| `src/OptimFoundation.Core/ISolverEngine.cs`                             | 新設計（綜合三版抽象）                                             | 三版 Interfaces.cs 不相容，重新設計精簡介面               | ✅ stub    |
| `src/OptimFoundation.Core/EngineBase.cs`                                | OMG_MILP `EngineBase<TModel, TVar, TExpr, TConstr>`                | 最新的泛型抽象基類                                        | ✅ stub    |
| `src/OptimFoundation.Core/ISolverConfig.cs`                             | 新設計                                                             | OMG_Foundation 的 `ISovlerConfig`（拼字錯）是空介面，重寫 | ✅ stub    |
| `src/OptimFoundation.Core/ISpecialConstraints.cs`                       | OMG_Foundation `ISpecialConstrints`（修拼字）                      | SOS/Indicator/Lazy 特殊約束抽象                           | ✅ stub    |
| `src/OptimFoundation.Core/Enums.cs`                                     | 新設計                                                             | 統一 VarType / ConstraintSense / ObjectiveSense           | ✅ stub    |
| `src/OptimFoundation.Core/Infrastructure/` (FolderDir/ReflectionHelper/ClassInfo) | OptimFoundation 版                                                 | 最新、含 logging（目錄改名 `Core/` → `Infrastructure/`） | ✅ done    |
| `src/OptimFoundation.Core/VariableBuilder.cs`                           | OMG_MILP 版（改名 `VariableManager` → `VariableBuilder`）          | 最新                                                      | ✅ done    |
| `src/OptimFoundation.Core/DesignBases.cs`                               | OptimFoundation 版                                                 | 最精簡                                                    | ✅ done    |
| `src/OptimFoundation.Core/Csv/CsvCtrl.cs`                               | OMG_MILP 版 + `OMG_Foundation(CPLEX only).cs` 的 `read_matrix_csv` | 合併獨家功能（檔名大小寫 `CSVCtrl` → `CsvCtrl`）         | ✅ done    |
| `src/OptimFoundation.Core/Logging/Logging.cs`                           | OMG_MILP 版（檔名 `Log.cs` → `Logging.cs`）                        | 最新                                                      | ✅ done    |
| `src/OptimFoundation.Core/Db/IDbCtrl.cs`                                | 新設計                                                             | DB 介面抽象                                               | ✅ done    |
| `src/OptimFoundation.Core/Db/DBCtrlBase.cs`                             | OMG_MILP `DBCtrlBase`（行 12-930）                                 | 新增的 base class                                         | ✅ done    |
| `src/OptimFoundation.Db.Oracle/OracleDBCtrl.cs`                         | OMG_MILP `DBCtrl` + OMG_Foundation Oracle 連線細節                 | Oracle 特化                                               | ✅ done    |
| `src/OptimFoundation.Cplex/CplexConfig.cs`                              | OMG_MILP `CplexSovlerConfig`（修拼字 → `CplexConfig`）             | 最新                                                      | ✅ stub    |
| `src/OptimFoundation.Cplex/OptEngine.cs`                                | OMG_MILP `CplexEngine.cs`（class 改名 `CplexEngine` → `OptEngine`） | 呼叫端只認 `OptEngine`，solver 差異由 namespace 隔離     | ✅ done    |
| `src/OptimFoundation.Gurobi/GurobiConfig.cs`                            | 新設計（仿 CplexConfig 結構）                                      | Gurobi 參數風格                                           | ✅ stub    |
| `src/OptimFoundation.Gurobi/OptEngine.cs`                               | OMG_Foundation `GurobiEngine.cs`（class 改名 `GurobiEngine` → `OptEngine`） | 同上，stub-only                                  | ✅ stub    |

## 拼字修正

| 原名稱               | 新名稱                |
| -------------------- | --------------------- |
| `ISovlerConfig`      | `ISolverConfig`       |
| `ISpecialConstrints` | `ISpecialConstraints` |
| `CplexSovlerConfig`  | `CplexConfig`         |

## Namespace 統一

| 舊 namespace             | 新 namespace                |
| ------------------------ | --------------------------- |
| `OMG.CplexEngine`        | `OptimFoundation.Cplex`     |
| `OMG.GurobiEngine`       | `OptimFoundation.Gurobi`    |
| `OMG.DBManager.MySQL`    | `OptimFoundation.Db.Oracle` |
| `OMG.Core`               | `OptimFoundation.Core`      |
| `Foundation.CplexEngine` | `OptimFoundation.Cplex`     |

## 待清理（Layer 5）

- [ ] 刪除 `OptimFoundation/CPLEX_Foundation.sln` 與 `OptimFoundation/CPLEX_Foundation.csproj`
- [ ] 刪除 `OptimFoundation/` 根目錄的舊 `.cs` 檔（搬入 `src/` 後）
  - `CPLEX_Engine.cs`、`Core.cs`、`CSVCtrl.cs`、`DesignBases.cs`、`Log.cs`、`VariableManager.cs`
- [ ] 刪除 `OptimFoundation/OMG_Foundation(CPLEX only).cs`（獨家功能 `read_matrix_csv` 合進 `CSVCtrl.cs` 後）
- [ ] 刪除整個 `OMG_Foundation/` 資料夾
- [ ] 刪除整個 `OMG_MILP Development APIs/` 資料夾
- [ ] 刪除 `OptimFoundation/Engines/` 子資料夾（內容已搬到 `src/OptimFoundation.{Cplex,Gurobi}/`）

## Open Questions（沿用 spec）

- [ ] CPLEX `HintPath` 假設 `..\..\..\..\..\IBM\ILOG\CPLEX_Studio2211\...`（從 `src/<proj>/` 往上 5 層）；若你的安裝路徑不同需調整
- [ ] `OMG_Foundation(CPLEX only).cs` 的 `Solver<T>` 是否仍有外部呼叫？（待確認後決定是否保留 wrapper）
