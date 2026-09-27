# OptimFoundation Code Map

> 同步日期：2026-09-26

## Solution

| 路徑 | 責任 |
| --- | --- |
| `src/OptimFoundation.Core/` | solver-neutral 核心、IO、naming、logging、專案/求解設定、experiments、DB/CSV 輔助 infrastructure |
| `src/OptimFoundation.Generators/` | Set / Parameter / Variable source generator |
| `src/OptimFoundation.Cplex/` | CPLEX adapter、model/project/experiment |
| `tests/OptimFoundation.Cplex.Tests/` | Core、generator、Cplex adapter tests |
| `Templates/` | 可 build 的現行 API 範例 + 匯入模型檔的 ModelInspector / ModelTuner 工具 |
| `specs/` | 開發規格與架構決策 |
| `docs/` | 對外說明頁 |

## Core

| 檔案 | 主要型別 / 功能 |
| --- | --- |
| `ModelStats.cs` | `ModelCounts`（變數 / 型別分布 / 限制式 / 特殊元素 / 目標式方向）、`ModelStatsMismatch`、`ModelStatsReport`（`IsMatch` / `Summary`）：模型統計對帳的值物件，掛在 `Trial.Metrics.ModelStats` |
| `DesignBases.cs` | `ModelElementBase`（`InitClassBySets` 反射填值並驗 token、`InitFromDataRow` 資料來源載入用不驗 token、`ToString` 組 key）、`SetRowBase`、`ParameterBase`、`VariableBase`、`ConstraintBase` |
| `ModelNaming.cs` | internal：`@` key 組成（`Token`/`TryToken`/`Compose`/`ValidateComposedName`）、`yyyy_MM_dd`（帶時間則 `yyyy_MM_dd_HH_mm_ss`）日期格式與保留字元/空白驗證 |
| `VariablePrefixNaming.cs` | internal（`OptimFoundation.Internal` namespace）：B/C/I 前綴解析 `TryResolve`；以 linked source 同時編入 `OptimFoundation.Generators`，避免編譯期與執行期規則漂移 |
| `EngineBase.cs` | `ISolverConfig`（含 tuning 共通旋鈕，原 `ITunableConfig` 已併入）、`ISolverEngine`（含 `AddMIPStart`）、`OptBounds.Infinity`（= 1E20，框架唯一的「無上限」）、`ISpecialConstraints<TVar,TExpr>` 介面；`SolveStatus`/`VarType`/`ConstraintSense`/`ObjectiveSense`/`ModelType` enum；`EngineBase<TModel,TVar,TExpr,TConstr>` 抽象泛型基底——Build*Vs 批次建變數、AddLHS/AddRHS pool、CreateGreatEqual/LessEqual/Equal/Range（Range 與目標式遇 RHS pool → `[POOL_RHS_IGNORED]` warn 後捨棄）、CreateMinimize/Maximize（含 LHS 常數項 → `ObjectiveConstant`）、`AddMIPStart`（名稱解析 + LP/未知名稱 warn 略過 → `AddMIPStartCore` primitive）、CreateLeSoft/GeSoft/EqSoft 軟性限制式、GetSolution/GetSetVarValues 取解、VariableBuildCounts/ConstraintBuildCounts 建立統計、`ReconcileModelStats()` 模型統計對帳（建模記帳 / 框架索引 / `ReadSolverModelCounts` primitive 三方比對，Solve 前寫 `[模型統計對帳]` 與 `[MODEL_STATS_MATCH]`/`[MODEL_STATS_MISMATCH]` log 並存進 `ModelStats` / `LastMetrics.ModelStats`；`RecordImportedModel`/`RecordDirect*` 給子類別登記記帳）、ModelType（LP/MILP/IP/BP，經 `ReadModelComposition` primitive 向 solver 模型取值並於 Solve 前印 `[模型類型]` log）、ErrorOnce 例外邊界（★ `ISolverEngine.cs` 與 `Enums.cs` 已併入本檔，原兩檔已從檔案系統刪除，重構進行中） |
| `VariableBuilder.cs` | primitive / `SetRowBase` / ValueTuple domain 展開為 `TypeName@v1@v2…`、`GenVarCombinations` 笛卡兒積、`ValidateVariableArity` |
| `DataContext.cs` | `OptData.Load`（Initialize→Freeze）、`ParamRow`/`SetRegistration`/`ParamRegistration`、`DataContext`（`RegisterSet`/`RegisterParam`/`GuardMutation`；`DataIssues` 收集驗證問題（DuplicateKey / Numeric / InvalidKey），逐筆 `[DATA_VALIDATION_WARNING]` warn 後照常建模）、`ParameterLookupExtensions.FindParameterOrLog` |
| `Experiments/Experiment.cs` | `Experiment`（`Trials`、`Save` 輸出 CSV + meta-CSV(+trajectory-CSV)，同名直接覆寫、不讀回不累積）、`ITrajectorySource` 介面、`Trial`（`Trial.Capture(engine, label, solveAction)` 套件化擷取 `ConfigSnapshot` + `SolveMetrics`） |
| `Logging/Logging.cs` | Console + log 檔雙寫（延遲開檔、lock 保護）、`ErrorOnce`（同一例外物件只記一次）、`SetLogFileName`、`WriteToFile`、`ClearLogs` |
| `Infrastructure/ProjectConfig.cs` | 專案層設定：`ProjectName`/`RetentionDays`/`EnableSolverLog`/`ExportLP`/`ExportMPS`/`ExportSol`/`ExportIIS`、`Clone()` |
| `Experiments/ConfigSnapshot.cs` | `ConfigSnapshot.From(ISolverConfig)`：只記「真的有設」的旋鈕，`ISolverConfig` 共通欄位 + reflection 補抓 solver 專屬欄位 |
| `Experiments/SolveMetrics.cs` | `SolveMetrics`（Status/ObjectiveValue/BestBound/MipGap/RunTimeMs/NodeCount/IterationCount…）+ `ConvergencePoint`；`TFeasMs`/`DeltaBound`/`TStallMs` 由 `Convergence` 序列推算；`ModelStats`（求解前的模型統計對帳） |
| `Experiments/ExpCsvWriter.cs` | 實驗輸出只有 CSV：`CsvExperimentWriter`（一列一 Trial，只寫與同批基準的 `DiffKnobs`）、`MetaCsvWriter`（Section/Key/Value 說明檔：批次資訊、模型規模、`modelStats` 對帳（schema v2）、求解環境、基準完整設定）、`TrajectoryCsvWriter`（收斂軌跡長格式，1 列 / 收斂點） |
| `Infrastructure/ClassInfo.cs` | 由變數/參數類別反推 DB 表結構：`VarInsertCmd`/`ParamInsertCmd`/`VarTableCreateCmd`/`ParamTableCreateCmd` |
| `Infrastructure/FolderDir.cs` | 固定輸出資料夾配置（Data/Solution/Logs/Models/IISs/Sols/Experiments）、`ProjFolder`（`GetPath`/`TryCreateFile`/`PurgeOlderThan`）、`FolderDir.PurgeOutputs` 保留期清理 |
| `Infrastructure/ReflectionHelper.cs` | C# → Oracle 型別對應表、`GetMemberNames`/`GetMemberTypes`/`GenerateSQLCols`（供 `ClassInfo` 拼建表語句） |

## IO

| 檔案 | 功能 |
| --- | --- |
| `IO/IDataSource.cs` | `IDataSource.LoadData`、共用 `Load<T>`、`ISolutionSink`、`ISolutionBatch` |
| `IO/ModelRowMapper.cs` | internal：`ModelRowMapper`：DataTable → Set/Parameter model row |
| `IO/TabularData.cs` | internal：header-based records ↔ DataTable |
| `IO/csv/CsvDataSource.cs` | CSV source 與 `CsvSolutionSink`（含 `CsvSolutionBatch`） |
| `IO/csv/CsvCtrl.cs` | RFC4180 `ParseCsv`（public；ModelTuner 讀 archive CSV 也用它）、`WriteRows`、`WriteSolution` |
| `IO/InMemoryDataSource.cs` | `AddRows` + `LoadData` |
| `IO/db/IDbCtrl.cs` | DB-agnostic 操作合約：`Query`/`Execute`/`QueryScalar`/`ExecuteBatch`/`ExecuteInTransaction`，參數一律 `(name, value)` tuple |
| `IO/db/DBCtrlBase.cs` | `IDbCtrl` 的 DB-agnostic 抽象基底：ambient connection/transaction 編排（巢狀交易參與外層、成功 commit / 例外 rollback）；具體驅動只需覆寫 `CreateRawConnection` |
| `IO/db/OracleDBCtrl.cs` | `OracleDBCtrl : DBCtrlBase`：Oracle 查詢與建表/清表（`CreateParamTable`/`CreateResultTable`/`DropTable`/`DeleteTable`/`TruncateTable`/`SaveToDB`）、`BuildConnectionString`；`OracleSolutionSink` + `OracleSolutionBatch`（陣列綁定批次寫解） |
| `IO/db/DbDataSource.cs` | query-only `IDataSource`，支援 bind parameters overload |

## Generator

`AutoSetsGenerator.cs` 注入：

- `[OptSet]`：至少一個 primitive `OptDim`，生成 `SetRowBase` row。
- `[OptParam]`：零到多個 primitive `OptDim`，生成 `ParameterBase` row 與 `QTY`。
- `[OptVar]`：零到多個 primitive `OptDim`，由 B/C/I 前綴決定型別。
- `DataContext` partial subclass 的 `RegisterAll` 註冊碼（靠繼承關係掃描，非 attribute 標記）。

Diagnostics：`OPTF001`、`OPTF002`、`OPTF003`、`OPTF006`、`OPTF007`、`OPTF008`。

`IsExternalInit.cs`：netstandard2.0 沒有 `IsExternalInit`，record 的 `init` 存取子需要它，標準 polyfill，無執行期行為。

## CPLEX

| 檔案 | 功能 |
| --- | --- |
| `src/OptimFoundation.Cplex/OptEngine.cs` | `OptEngine : EngineBase<Cplex, INumVar, ILinearNumExpr, IRange>`：`AddVariable(s)`/`LinearExpr`/`AddConstraint`/`AddRangeConstraint`/`SetObjective`（含常數項）/`SetVariableBounds`/`AddMIPStartCore` primitive、`MipStartEffort`、`SolveCore`（含收斂軌跡 callback；BestBound/MIP gap 只在 MIP 讀，LP 記 gap=0 並留 log）、`ExportModelFile`/`ImportModel`（re-index 後 `RecordImportedModel` 登記記帳並依檔案同步 `ObjectiveSense`）、`ReadSolverModelCounts`（Ncols/Nrows/NbinVars/NintVars/NSOSs/NQCs/Nindicators/semi/lazy/userCut/GetObjective）、`ReadSolution`（.sol/.mst 起始解）/`ExportSolution`、`GetConflictConstraints`（IIS）、`CopyModel`/`MergeModel`/`VariableMerge`、`GetCVSolution`/`GetIVSolution`/`GetBVSolution` |
| `OptEngine.Configuration.cs` | 同一個 partial class 的組態套用區：`Configuration(ISolverConfig)` 把 `CplexConfig` 全部約 182 顆旋鈕與 `ProjectConfig`（log 路由、LP/MPS/Sol 匯出）逐項套進 CPLEX；獨立成檔避免淹沒 `OptEngine.cs` 的建模主線 |
| `CplexConfig.cs` | `CplexConfig : ISolverConfig`：CPLEX 22.1.1 全部 182 顆可設參數，分四類（停止條件/執行資源/重複量測/搜尋策略，只有搜尋策略可進 tuning variant 池），一律 `null` = 不設、無 property 帶預設值 |
| `OptModel.cs` | `OptModel`：`AddVariables`/`AddObjective`/`AddConstraints` 記錄建模步驟；`FromFile` 以既有模型檔（.lp/.mps/.sav）取代逐步建模（走 `OptEngine.ImportModel`）；`ReadSolution`/`AddMIPStart` 起始解步驟（建模後套用）；`ApplyTo` 依序套用 variables → objective → constraints → MIP start |
| `OptProject.cs` | 單一 model × config 的執行器：`UseConfig`、`OnSolved`、`Execute`（housekeeping、log 檔名、保留期清理、EffectiveConfig log）、`Engine`/`IsSuccess`/`TotalElapsed`/`BuildModelElapsed` |
| `OptExperiment.cs` | model × config 交叉實驗矩陣，可另加明確 cell（`AddTrial`）；`Run()` 逐 cell 建 engine、`Trial.Capture`、印整批模型統計對帳總結、最後 `Experiment.Save()` |

## Templates

| 範例 | 重點 |
| --- | --- |
| `Templates/Tutorial/` | 一維 Set、Parameter、B/C/I Variable、標準組裝 |
| `Templates/TSP_MultiDimSet/` | 多維 Set 稀疏 domain |
| `Templates/Sudoku_SHC279/` | 多維 Set、scalar Parameter、raw import → `WriteRows` |
| `Templates/RosteringProblem/` | 排班、soft/複雜限制式 |
| `Templates/FJSP_BASIC_BRICK/` | 既有專案識別名稱；內部程式已使用現行 row API |
| `Templates/ModelInspector/` | 吃既有模型檔（.lp/.mps/.sav）求解，印出匯入模式下框架各項功能可用/失真的對照報告；不綁任何題目專案 |
| `Templates/ModelTuner/` | 吃既有模型檔的 Phase 3 tuning 殼：`instances.lock` 指紋凍結、`TuningRound`（seeds × instances、warm-up、順序輪替、hold-out）、自動 archive、`RoundFacts`（TUNING-FACTS / θ / 結果不變式 / dynamic search）、`CplexTuner`（繼承 `OptEngine` 呼叫 `TuneParam`） |
| `Templates/MODEL-SOP.md` | Set/Parameter/Variable 宣告 SOP，跨 Template 共用的宣告寫法範例（非 buildable 專案） |

## 規格

| 文件 | 用途 |
| --- | --- |
| `specs/2026-09-26-model-stats-reconciliation.md` | status: implemented — 框架建模統計 vs solver 模型統計對帳；建模階段（Solve 前 log）與 tuning 階段（Trial / meta CSV / ModelTuner facts）皆可見；順帶修正匯入模式 `ObjectiveSense` |
| `specs/2026-09-18-model-source-duality-and-profile.md` | status: draft — 模型來源二態（自建 / 匯入既有模型檔）改走共同契約、統一模型結構統計（`ModelProfile`/`AuthoringReport`）設計；其中匯入模式 `ObjectiveSense` 失真（AC3）已由 2026-09-26 spec 修正，其餘未實作 |

## Build

```powershell
dotnet build OptimFoundation.sln
dotnet test tests/OptimFoundation.Cplex.Tests/OptimFoundation.Cplex.Tests.csproj
```
