# OptimFoundation Code Map

> 同步日期：2026-10-03

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
| `ModelElementBase.cs` | `ModelElementBase`（`InitClassBySets` 反射填值並驗 token、`InitFromDataRow` 資料來源載入用不驗 token、`ToString` 組 key）、`SetRowBase`、`ParameterBase`、`VariableBase`、`ConstraintBase` |
| `ModelNaming.cs` | internal：`@` key 組成（`Token`/`TryToken`/`Compose`/`ValidateComposedName`）、`yyyy_MM_dd`（帶時間則 `yyyy_MM_dd_HH_mm_ss`）日期格式與保留字元/空白驗證 |
| `VariablePrefixNaming.cs` | internal（`OptimFoundation.Internal` namespace）：B/C/I 前綴解析 `TryResolve`；以 linked source 同時編入 `OptimFoundation.Generators`，避免編譯期與執行期規則漂移 |
| `EngineBase.cs` | `ISolverConfig`（含 tuning 共通旋鈕，原 `ITunableConfig` 已併入）、`ISolverEngine`（含 `AddMIPStart`）、`OptBounds.Infinity`（= 1E20，框架唯一的「無上限」）、`ISpecialConstraints<TVar,TExpr>` 介面；`SolveStatus`/`VarType`/`ConstraintSense`/`ObjectiveSense`/`ModelType` enum；`EngineBase<TModel,TVar,TExpr,TConstr>` 抽象泛型基底——單一變數池 `Variables`（無分組結構；GetSetVarNames/GetSetVarValues/GetSolution(type) 以型別名篩選，名稱為 `TypeName` 或 `TypeName@…`）、Build*Vs 批次建變數、AddLHS/AddRHS pool、CreateGreaterEqual/LessEqual/Equal/Range（Range 與目標式遇 RHS pool → `[POOL_RHS_IGNORED]` warn 後捨棄）、CreateMinimize/Maximize（含 LHS 常數項 → `ObjectiveConstant`）、`AddMIPStart`（名稱解析 + LP/未知名稱 warn 略過 → `AddMIPStartCore` primitive）、CreateLessEqualSoft/CreateGreaterEqualSoft/CreateEqualSoft 軟性限制式、GetSolution/GetSetVarValues 取解、VariableBuildCounts/ConstraintBuildCounts 建立統計、未引用變數檢查（Solve 前 CPLEX 收的變數比框架宣告的少時寫 `[UNREFERENCED_VARIABLES]` WARN 點名；`RecordImportedModel`/`RecordDirect*` 給子類別登記建立統計）、ModelType（LP/MILP/IP/BP，經 `ReadModelComposition` primitive 向 solver 模型取值並於 Solve 前印 `[模型類型]` log）、ErrorOnce 例外邊界（★ `ISolverEngine.cs` 與 `Enums.cs` 已併入本檔，原兩檔已從檔案系統刪除，重構進行中） |
| `VariableBuilder.cs` | primitive / `SetRowBase` / ValueTuple domain 展開為 `TypeName@v1@v2…`、`GenVarCombinations` 笛卡兒積、`ValidateVariableArity` |
| `DataContext.cs` | `OptData.Load`（Initialize→Freeze）、`ParamRow`/`SetRegistration`/`ParamRegistration`、`DataContext`（`RegisterSet`/`RegisterParam`/`GuardMutation`；`DataIssues` 收集驗證問題（DuplicateKey / Numeric / InvalidKey），逐筆 `[DATA_VALIDATION_WARNING]` warn 後照常建模）、`ParameterLookupExtensions.FindParameterOrLog` |
| `Experiments/Experiment.cs` | `Experiment`（`Trials`、`Summaries`、`Project` / `Name` / `WriteSummary`；`Save` 把紀錄接在專案的四個累積檔尾端：`{Project}-trial.csv` / `-meta.csv` / `-summary.csv`（`WriteSummary` 時）/ `-trajectory.csv`（有點才寫），每列前三欄 `RecordedAt` / `Experiment` / `RunId`；沒有 trial 就 WARN 不寫）、`ITrajectorySource` 介面、`Trial`（`Trial.Capture(engine, label, solveAction, captureTrajectory)` 擷取 `ConfigSnapshot` + `SolveMetrics`；`captureTrajectory=false` 不開收斂軌跡）、`ConfigSummary`（每組設定 = 同批同模型、label 去掉 `-s<seed>` 的彙總：各狀態數、找到可行解數、逐 seed 跟基準比大小的贏 / 輸 / 平手 / 無法比較數；label 含 `warmup` 不計入也不當基準）、`BaselineComparer`（internal，逐 seed 比大小：有沒有解 → 有沒有證明最佳 → `SolveTimeMs` / `Gap`；主表 VsBaseline 與 summary 共用） |
| `Infrastructure/Logging.cs` | Console + log 檔雙寫（延遲開檔、lock 保護）、`ErrorOnce`（同一例外物件只記一次）、`SetLogFileName`（由 OptProject / OptExperiment 自動呼叫）、`WriteToFile`、`ClearLogs` |
| `Infrastructure/ProjectConfig.cs` | 專案設定（輸出開關）：`EnableSolverLog`/`ExportLP`/`ExportMPS`/`ExportSol`/`ExportIIS`（未接線）、`Clone()`、`Quiet()`（實驗預設）；不進 `ConfigSnapshot`。專案名與保留期屬於 `OptProject` |
| `Experiments/ConfigSnapshot.cs` | `ConfigSnapshot.From(ISolverConfig)`：只記「真的有設」的旋鈕，`ISolverConfig` 共通欄位 + reflection 補抓 solver 專屬欄位 |
| `Experiments/SolveMetrics.cs` | `SolveMetrics`（Status/ObjectiveValue/BestBound/`Gap`（結果 gap）/`SolveTimeMs`（純求解）/`BuildAndSolveTimeMs`（建模 + 求解，經 OptProject.Solve / OptExperiment 才有值，否則 null）/NodeCount/IterationCount/`Seed`（實際使用的種子）/`TrajectoryEnabled`，以及取自 CPLEX 模型的 `ModelType` 與各類變數 / 限制式數量…）+ `ConvergencePoint`；`FirstSolutionMs`/`BoundChange`/`LastBoundChangeMs` 由 `Convergence` 序列推算、`ObjectiveSense`（取自 CPLEX） |
| `Experiments/ExpCsvWriter.cs` | 實驗輸出只有 CSV：`CsvExperimentWriter`（一列一 Trial，只寫與同批基準的 `ConfigChanges`；基準 = 第一個 label 含 `baseline` 的非暖機 trial，沒有就取第一個非暖機 trial，含 `ModelType`、`VsBaseline` 欄（跟同 seed 基準比：`win` / `lose` / `tie`）；每格都有值，缺值寫 `off` / `none` / `n/a` / `baseline` 標記）、`MetaCsvWriter`（Section/Key/Value 說明檔：`legend` 標記定義、批次資訊、模型規模、`model` 模型類型與各類數量、求解環境、基準完整設定；每批（RunId）一份，schema v11）、`SummaryCsvWriter`（`-summary.csv`，一列一組設定，寫出 `ConfigSummary`；基準列的 Wins / Losses / Ties / NotCompared 寫 `baseline`）、`TrajectoryCsvWriter`（收斂軌跡長格式，1 列 / 收斂點，`TrialId` 對回主表；還沒有值寫 `#N/A`）；四個 writer 的 `Write(experiment, path, recordedAt)` 都是附加，走 internal `CumulativeCsv`（新檔寫 BOM + 表頭；表頭相同接檔尾；表頭不同舊檔改名 `-old-<時間>` + WARN；寫不進去改寫 `-locked-<時間>` + WARN） |
| `Infrastructure/ClassInfo.cs` | 由變數/參數類別反推 DB 表結構：`VarInsertCmd`/`ParamInsertCmd`/`VarTableCreateCmd`/`ParamTableCreateCmd`；同檔的 `ReflectionHelper` 提供 C# → Oracle 型別對應與 `GetMemberNames`/`GetMemberTypes`/`GenerateSQLCols` |
| `Infrastructure/FolderDir.cs` | 固定資料夾配置（Input/Output/Log/Model/IIS/Solution/Experiment）、`CreateAll()`（建立 OptProject 時全部建好）、`PurgeAllOutputs` 保留期清理（不清 Input 與 Experiment；Experiment 的累積檔只會長大）、`ProjFolder`（`GetPath`/`TryCreateFile`/`PurgeOlderThan`） |

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
| `IO/db/DbCtrlBase.cs` | `IDbCtrl` 的 DB-agnostic 抽象基底：ambient connection/transaction 編排（巢狀交易參與外層、成功 commit / 例外 rollback）；具體驅動只需覆寫 `CreateRawConnection` |
| `IO/db/OracleDbCtrl.cs` | `OracleDbCtrl : DbCtrlBase`：Oracle 查詢與建表/清表（`CreateParamTable`/`CreateResultTable`/`DropTable`/`DeleteTable`/`TruncateTable`/`SaveToDB`）、`BuildConnectionString`；`OracleSolutionSink` + `OracleSolutionBatch`（陣列綁定批次寫解） |
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
| `src/OptimFoundation.Cplex/OptEngine.cs` | `OptEngine : EngineBase<Cplex, INumVar, ILinearNumExpr, IRange>`：`AddVariable(s)`/`LinearExpr`/`AddConstraint`/`AddRangeConstraint`/`SetObjective`（含常數項）/`SetVariableBounds`/`AddMIPStartCore` primitive、`MipStartEffort`、`SolveCore`（含收斂軌跡 callback，只記 CPLEX 實際呼叫到的點、不補點；求解時間取 `Cplex.GetCplexTime()` 前後相減，不自己計時；BestBound/MIP gap 只在 MIP 讀，LP 記 gap=0 並留 log）、`ExportModel`/`ReadModel`（re-index 後 `RecordImportedModel` 依檔案同步 `ObjectiveSense`）、`ReadSolution`（.sol/.mst 起始解）/`ExportSolution`、`RunModel`（internal：Build → `OptModel.ApplyTo` → beforeSolve → `Trial.Capture`，OptProject.Solve 與 OptExperiment 共用的唯一執行路徑）、`GetConflictConstraints`（IIS）、`CopyModel`/`MergeModel`/`MergeVariables`、`GetCVSolution`/`GetIVSolution`/`GetBVSolution` |
| `OptEngine.Configuration.cs` | 同一個 partial class 的組態套用區：`LoadConfig(ISolverConfig)` 把 `CplexConfig` 全部約 182 顆旋鈕與 `ProjectConfig`（log 路由、LP/MPS/Sol 匯出，log tag `[Project Setting]`）逐項套進 CPLEX；獨立成檔避免淹沒 `OptEngine.cs` 的建模主線 |
| `CplexConfig.cs` | `CplexConfig : ISolverConfig`：CPLEX 22.1.1 全部 182 顆可設參數，分四類（停止條件/執行資源/重複量測/搜尋策略，只有搜尋策略可進 tuning variant 池），一律 `null` = 不設、無 property 帶預設值 |
| `OptModel.cs` | `OptModel`：`AddVariables`/`AddObjective`/`AddConstraints` 記錄建模步驟；`ReadModel` 以既有模型檔（.lp/.mps/.sav）取代逐步建模（走 `OptEngine.ReadModel`）；`ReadSolution`/`AddMIPStart` 起始解步驟（建模後套用）；`ApplyTo` 依序套用 variables → objective → constraints → MIP start |
| `OptProject.cs` | 專案（唯一入口，`IDisposable`）：ctor(`name`, `retentionDays`) 驗證名稱、接上 `{name}` log、`FolderDir.CreateAll`、保留期清理、印 `[Project]`；`LoadConfig(ProjectConfig)`（正式求解的專案設定）、`Solve(model, config, onSolved, beforeSolve)`（Clone 組態建新 engine → `OptEngine.RunModel`，不開軌跡；Trial 接在 `Experiment/{專案}-trial.csv` 與 `-meta.csv`，Experiment 欄 = `SolveExperimentName`（`solve`），不寫 summary；成功才跑 onSolved）；最近一次 Solve 的 `Engine`/`IsSuccess`/`Trial`/`TotalElapsed`/`BuildModelElapsed`（皆用 CPLEX 時鐘）；`Experiment(name, description)` → `OptExperiment`；internal `NextRunId()`（批次識別 yyyyMMdd-HHmmss，同一秒再開一批加 `-2`） |
| `OptExperiment.cs` | 實驗（由 `OptProject.Experiment` 建立，建立時切到 `{專案}-{實驗}_exp` log）：model × config 交叉矩陣 + 明確 cell（`AddTrial`）；`LoadConfig(ProjectConfig)`（預設 `ProjectConfig.Quiet()`）、`CaptureTrajectory`；`Run()` 逐 cell 建新 engine 走 `OptEngine.RunModel`、`Experiment.Save()` 接在 `Experiment/{專案}-trial.csv` 等累積檔（Experiment 欄 = `Name`）；`Name`/`FullName`（`FullName` 只用在 log 檔名） |

## Templates

| 範例 | 重點 |
| --- | --- |
| `Templates/Tutorial/` | 一維 Set、Parameter、B/C/I Variable、標準組裝 |
| `Templates/TSP_MultiDimSet/` | 多維 Set 稀疏 domain |
| `Templates/Sudoku_SHC279/` | 多維 Set、scalar Parameter、raw import-data → `WriteRows` |
| `Templates/RosteringProblem/` | 排班、soft/複雜限制式 |
| `Templates/FJSP_BASIC_BRICK/` | 既有專案識別名稱；內部程式已使用現行 row API |
| `Templates/ModelInspector/` | 吃既有模型檔（.lp/.mps/.sav）求解，印出匯入模式下框架各項功能可用/失真的對照報告；不綁任何題目專案 |
| `Templates/ModelTuner/` | 吃既有模型檔的 Phase 3 tuning 殼：`instances.lock` 指紋凍結、`TuningRound`（seeds × instances、warm-up、順序輪替、hold-out）、自動 archive（把 bin 的四個累積檔複製到 `Experiments/`，archive 只能變長；bin 被清過先從 archive 還原）、`RoundFacts`（篩 Experiment = `tuning-r<N>` 最新 RunId）（TUNING-FACTS / 勝負表 / 結果不變式 / dynamic search）、`CplexTuner`（繼承 `OptEngine` 呼叫 `TuneParam`） |
| `Templates/MODEL-SOP.md` | Set/Parameter/Variable 宣告 SOP，跨 Template 共用的宣告寫法範例（非 buildable 專案） |

## 規格

| 文件 | 用途 |
| --- | --- |
| `specs/2026-09-28-project-scope-and-unified-run.md` | status: implementing — OptProject 升格為專案（資料夾 / log / 保留期）並直接提供 `Solve`，Solve 與 Experiment 共用 internal `OptEngine.RunModel`；`ProjectConfig` 只留輸出開關、吃設定一律 `LoadConfig`；Solve 也留 Trial；AI-Modeling 同步待辦 |
| `specs/2026-09-26-model-stats-reconciliation.md` | status: superseded（2026-10-03 撤除，只留 `[UNREFERENCED_VARIABLES]` WARN）— 框架建模統計 vs solver 模型統計對帳；建模階段（Solve 前 log）與 tuning 階段（Trial / meta CSV / ModelTuner facts）皆可見；順帶修正匯入模式 `ObjectiveSense` |
| `specs/2026-09-18-model-source-duality-and-profile.md` | status: draft — 模型來源二態（自建 / 匯入既有模型檔）改走共同契約、統一模型結構統計（`ModelProfile`/`AuthoringReport`）設計；其中匯入模式 `ObjectiveSense` 失真（AC3）已由 2026-09-26 spec 修正，其餘未實作 |

## Build

```powershell
dotnet build OptimFoundation.sln
dotnet test tests/OptimFoundation.Cplex.Tests/OptimFoundation.Cplex.Tests.csproj
```

## Comment cleanup scope (2026-10-03)
範圍含手寫 source、project config、HTML 與 gitignore。計數為註解標記候選行，可能含字串中的範例及路徑。排除 .git、bin、obj、packages、生成碼、二進位與 CSV 資料；JSON 無註解，.sln 版本標記保留。上方既有地圖保留；下表 +/- 為整理前相對 HEAD 的既有變更，驗收以任務開始時備份比對。


### File Index

| 檔案 | +行 | -行 | 語言 | 註解標記行 | 關鍵 Symbol |
| --- | --- | --- | --- | --- | --- |
| `OptimFoundation.sln` | 0 | 0 | .sln | 1 |  |
| `.gitignore` | 0 | 0 | .gitignore | 45 |  |
| `tests/OptimFoundation.Cplex.Tests/Unit/ConfigSummaryTests.cs` | 166 | 0 | .cs | 8 | `ConfigSummaryTests` |
| `tests/OptimFoundation.Cplex.Tests/OptimFoundation.Cplex.Tests.csproj` | 0 | 0 | .csproj | 4 |  |
| `tests/OptimFoundation.Cplex.Tests/Unit/ZzScratchProbe.cs` | 38 | 0 | .cs | 0 | `ZzScratchProbe` |
| `tests/OptimFoundation.Cplex.Tests/Unit/VariableBuilderTests.cs` | 2 | 2 | .cs | 4 | `VariableBuilderTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/StringOverloadParityTests.cs` | 0 | 0 | .cs | 11 | `StringOverloadParityTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/ScaleGuardTests.cs` | 6 | 6 | .cs | 14 | `ScaleGuardTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/RunnerSymmetryTests.cs` | 62 | 24 | .cs | 3 | `RunnerSymmetryTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/PoolSemanticsAndMipStartTests.cs` | 3 | 3 | .cs | 5 | `PoolSemanticsAndMipStartTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/OracleSolutionSinkTests.cs` | 4 | 4 | .cs | 16 | `OracleSolutionSinkTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/ObservabilityTests.cs` | 2 | 2 | .cs | 1 | `ObservabilityTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/ModelElementBaseTests.cs` | 0 | 0 | .cs | 6 | `ModelElementBaseTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/GeneratorNumericCoverageTests.cs` | 11 | 11 | .cs | 14 | `Set_GncItem`, `Parameter_GncProfit`, `Parameter_GncScalar`, `VariableC_GncAmount`, `VariableI_GncCount`, `GncDataload`, `GeneratorNumericCoverageTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/FakeDbCtrl.cs` | 0 | 0 | .cs | 0 | `FakeDbCtrl` |
| `tests/OptimFoundation.Cplex.Tests/Unit/ExperimentSaveTests.cs` | 157 | 23 | .cs | 8 | `ExperimentSaveTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/EngineBaseTests.cs` | 31 | 15 | .cs | 21 | `EngineBaseTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/DbCtrlBaseTransactionTests.cs` | 13 | 13 | .cs | 26 | `FakeDbTransaction`, `FakeDbConnection`, `TestableDbCtrl`, `DbCtrlBaseTransactionTests` |
| `docs/tutorial-optmodel-callbacks.html` | 8 | 8 | .html | 2 |  |
| `docs/index.html` | 3 | 4 | .html | 1 | `Set_Arc`, `Parameter_ArcCost`, `VariableB_UseArc` |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | 11 | 0 | .cs | 4 | `VarS`, `VarDG`, `VarInt`, `VariableB_Pick`, `VariableC_Amt`, `VariableI_Cnt`, `VariableX_LegacyAmt`, `VariableY_LegacyCnt`, `VariableC_ArcFlow`, `VariableC_ArcFlowByDate`, `VariableC_ArcFlowWrongArity`, `Set_Arc`, `Constraint_Test`, `ParamX` |
| `tests/OptimFoundation.Cplex.Tests/Mocks/MockEngine.cs` | 8 | 8 | .cs | 11 | `MockEngine`, `MockConfig` |
| `src/OptimFoundation.Generators/OptimFoundation.Generators.csproj` | 0 | 0 | .csproj | 2 |  |
| `src/OptimFoundation.Generators/IsExternalInit.cs` | 1 | 1 | .cs | 2 | `IsExternalInit` |
| `src/OptimFoundation.Generators/AutoSetsGenerator.cs` | 35 | 35 | .cs | 62 | `AutoSetsGenerator`, `OptSetAttribute`, `OptParamAttribute`, `OptVarAttribute`, `OptDimAttribute` |
| `src/OptimFoundation.Core/ModelElementBase.cs` | 7 | 7 | .cs | 8 | `ModelElementBase`, `SetRowBase`, `ParameterBase`, `VariableBase`, `ConstraintBase` |
| `src/OptimFoundation.Core/DataContext.cs` | 6 | 6 | .cs | 24 | `DataIssueKind`, `DataIssue`, `DataValidator`, `OptData`, `ParamRow`, `SetRegistration`, `ParamRegistration`, `DataContext`, `ParameterLookupExtensions` |
| `src/OptimFoundation.Core/EngineBase.cs` | 144 | 144 | .cs | 446 | `ISolverConfig`, `ISolverEngine`, `ISpecialConstraints`, `SolveStatus`, `VarType`, `ConstraintSense`, `ObjectiveSense`, `ModelType`, `OptBounds`, `EngineBase` |
| `src/OptimFoundation.Core/VariablePrefixNaming.cs` | 5 | 5 | .cs | 6 | `VariablePrefixNaming` |
| `src/OptimFoundation.Core/ModelNaming.cs` | 7 | 7 | .cs | 19 | `ModelNaming` |
| `src/OptimFoundation.Core/VariableBuilder.cs` | 14 | 14 | .cs | 35 | `VariableBuilder` |
| `src/OptimFoundation.Core/OptimFoundation.Core.csproj` | 0 | 0 | .csproj | 1 |  |
| `src/OptimFoundation.Cplex/CplexConfig.cs` | 508 | 508 | .cs | 982 | `CplexConfig` |
| `src/OptimFoundation.Cplex/OptEngine.Configuration.cs` | 28 | 28 | .cs | 111 | `OptEngine` |
| `src/OptimFoundation.Cplex/OptEngine.cs` | 192 | 110 | .cs | 276 | `OptEngine` |
| `src/OptimFoundation.Cplex/OptExperiment.cs` | 66 | 64 | .cs | 22 | `OptExperiment` |
| `src/OptimFoundation.Cplex/OptimFoundation.Cplex.csproj` | 0 | 0 | .csproj | 1 |  |
| `src/OptimFoundation.Cplex/OptProject.cs` | 144 | 98 | .cs | 45 | `OptProject` |
| `src/OptimFoundation.Cplex/OptModel.cs` | 20 | 20 | .cs | 41 | `OptModel` |
| `src/OptimFoundation.Core/Experiments/SolveMetrics.cs` | 79 | 29 | .cs | 48 | `SolveMetrics`, `ConvergencePoint` |
| `src/OptimFoundation.Core/Experiments/Experiment.cs` | 289 | 39 | .cs | 107 | `Experiment`, `ITrajectorySource`, `Trial`, `ConfigSummary`, `BaselineComparison`, `BaselineComparer` |
| `src/OptimFoundation.Core/Experiments/ExpCsvWriter.cs` | 402 | 114 | .cs | 139 | `CsvExperimentWriter`, `MetaCsvWriter`, `SummaryCsvWriter`, `TrajectoryCsvWriter`, `CumulativeCsv` |
| `src/OptimFoundation.Core/Experiments/ConfigSnapshot.cs` | 10 | 10 | .cs | 17 | `ConfigSnapshot` |
| `tests/OptimFoundation.Cplex.Tests/Integration/ZzProbeExportExtTests.cs` | 51 | 0 | .cs | 0 | `ZzProbeExportExtTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/SolverParamValueMatrixTests.cs` | 12 | 12 | .cs | 36 | `SolverParamValueMatrixTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/SolverParamCoverageTests.cs` | 55 | 47 | .cs | 49 | `SolverParamCoverageTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/SolutionPipelineIntegrationTests.cs` | 7 | 7 | .cs | 5 | `SolutionPipelineIntegrationTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/ProjectScopeIntegrationTests.cs` | 249 | 0 | .cs | 11 | `ProjectScopeIntegrationTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/OptEngineIntegrationTests.cs` | 35 | 43 | .cs | 22 | `OptEngineIntegrationTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/ModelImportIntegrationTests.cs` | 51 | 50 | .cs | 13 | `ModelImportIntegrationTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/ExperimentIntegrationTests.cs` | 241 | 61 | .cs | 28 | `ExperimentIntegrationTests` |
| `src/OptimFoundation.Core/IO/TabularData.cs` | 1 | 1 | .cs | 1 | `TabularData` |
| `src/OptimFoundation.Core/IO/ModelRowMapper.cs` | 3 | 3 | .cs | 5 | `ModelRowMapper` |
| `src/OptimFoundation.Core/IO/InMemoryDataSource.cs` | 4 | 4 | .cs | 8 | `InMemoryDataSource` |
| `src/OptimFoundation.Core/IO/IDataSource.cs` | 10 | 10 | .cs | 27 | `IDataSource`, `ISolutionSink`, `ISolutionBatch` |
| `src/OptimFoundation.Core/Infrastructure/ProjectConfig.cs` | 13 | 15 | .cs | 14 | `ProjectConfig` |
| `src/OptimFoundation.Core/Infrastructure/Logging.cs` | 10 | 10 | .cs | 33 | `Logging` |
| `src/OptimFoundation.Core/Infrastructure/FolderDir.cs` | 22 | 9 | .cs | 31 | `FolderDir`, `ProjFolder` |
| `src/OptimFoundation.Core/Infrastructure/ClassInfo.cs` | 13 | 13 | .cs | 44 | `ReflectionHelper`, `ClassInfo` |
| `src/OptimFoundation.Core/IO/db/OracleDbCtrl.cs` | 0 | 0 | .cs | 102 | `OracleDbCtrl`, `OracleSolutionSink` |
| `src/OptimFoundation.Core/IO/db/IDbCtrl.cs` | 6 | 6 | .cs | 24 | `IDbCtrl` |
| `src/OptimFoundation.Core/IO/db/DbDataSource.cs` | 4 | 4 | .cs | 13 | `DbDataSource` |
| `src/OptimFoundation.Core/IO/db/DbCtrlBase.cs` | 0 | 0 | .cs | 26 | `DbCtrlBase` |
| `src/OptimFoundation.Core/IO/csv/CsvDataSource.cs` | 12 | 12 | .cs | 24 | `CsvDataSource`, `CsvSolutionSink` |
| `src/OptimFoundation.Core/IO/csv/CsvCtrl.cs` | 9 | 9 | .cs | 27 | `CsvCtrl` |
| `Templates/Tutorial/Variable/VariableI_Batch.cs` | 1 | 1 | .cs | 1 | `VariableI_Batch` |
| `Templates/Tutorial/Variable/VariableC_Produce.cs` | 1 | 1 | .cs | 1 | `VariableC_Produce` |
| `Templates/Tutorial/Variable/VariableB_Setup.cs` | 1 | 1 | .cs | 1 | `VariableB_Setup` |
| `Templates/Tutorial/Tutorial.csproj` | 0 | 6 | .csproj | 3 |  |
| `Templates/Tutorial/Solution/TutorialSolution.cs` | 2 | 2 | .cs | 7 | `TutorialSolution` |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableC_Start.cs` | 0 | 0 | .cs | 1 | `VariableC_Start` |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableC_Makespan.cs` | 1 | 1 | .cs | 1 | `VariableC_Makespan` |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableC_Complete.cs` | 0 | 0 | .cs | 1 | `VariableC_Complete` |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableB_Precede.cs` | 1 | 1 | .cs | 4 | `VariableB_Precede` |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableB_Assign.cs` | 0 | 0 | .cs | 1 | `VariableB_Assign` |
| `Templates/Tutorial/Set/Set_Shift.cs` | 0 | 0 | .cs | 1 | `Set_Shift` |
| `Templates/Tutorial/Set/Set_Product.cs` | 0 | 0 | .cs | 1 | `Set_Product` |
| `Templates/Tutorial/Set/Set_Machine.cs` | 0 | 0 | .cs | 1 | `Set_Machine` |
| `Templates/Tutorial/Set/Set_Date.cs` | 0 | 0 | .cs | 1 | `Set_Date` |
| `Templates/Tutorial/Program.cs` | 13 | 17 | .cs | 10 | `Program` |
| `Templates/RosteringProblem/Variable/VariableC_WeekendLT4.cs` | 0 | 0 | .cs | 1 | `VariableC_WeekendLT4` |
| `Templates/RosteringProblem/Variable/VariableC_BelowAVG.cs` | 0 | 0 | .cs | 1 | `VariableC_BelowAVG` |
| `Templates/RosteringProblem/Variable/VariableB_SixDayWork.cs` | 1 | 1 | .cs | 1 | `VariableB_SixDayWork` |
| `Templates/RosteringProblem/Variable/VariableB_ShiftAssign.cs` | 0 | 0 | .cs | 1 | `VariableB_ShiftAssign` |
| `Templates/RosteringProblem/Variable/VariableB_Off1Day.cs` | 1 | 1 | .cs | 1 | `VariableB_Off1Day` |
| `Templates/RosteringProblem/Variable/VariableB_NightToDay.cs` | 0 | 0 | .cs | 1 | `VariableB_NightToDay` |
| `Templates/RosteringProblem/Variable/VariableB_GroupMismatch.cs` | 0 | 0 | .cs | 1 | `VariableB_GroupMismatch` |
| `Templates/RosteringProblem/Variable/VariableB_DoubleOffLT2.cs` | 1 | 1 | .cs | 1 | `VariableB_DoubleOffLT2` |
| `Templates/RosteringProblem/Variable/VariableB_DoubleOffFlag.cs` | 1 | 1 | .cs | 1 | `VariableB_DoubleOffFlag` |
| `Templates/RosteringProblem/status.json` | 0 | 0 | .json | 0 |  |
| `Templates/FJSP_BASIC_BRICK/Solution/FJSP_BASIC_BRICKSolution.cs` | 1 | 1 | .cs | 7 | `FJSP_BASIC_BRICKSolution` |
| `Templates/Tutorial/Parameter/Parameter_UnitProfit.cs` | 0 | 0 | .cs | 1 | `Parameter_UnitProfit` |
| `Templates/Tutorial/Parameter/Parameter_SetupCost.cs` | 0 | 0 | .cs | 1 | `Parameter_SetupCost` |
| `Templates/Tutorial/Parameter/Parameter_MachineHours.cs` | 0 | 0 | .cs | 1 | `Parameter_MachineHours` |
| `Templates/Tutorial/Parameter/Parameter_Demand.cs` | 0 | 0 | .cs | 1 | `Parameter_Demand` |
| `Templates/Tutorial/Parameter/Parameter_Capacity.cs` | 0 | 0 | .cs | 1 | `Parameter_Capacity` |
| `Templates/Tutorial/Parameter/Parameter_BatchSize.cs` | 0 | 0 | .cs | 1 | `Parameter_BatchSize` |
| `Templates/Tutorial/Objective/ObjectiveFunction.cs` | 0 | 0 | .cs | 1 | `ObjectiveFunction` |
| `Templates/RosteringProblem/Objective/ObjectiveFunction.cs` | 0 | 0 | .cs | 3 | `ObjectiveFunction` |
| `Templates/RosteringProblem/Solution/RosteringProblemSolution.cs` | 1 | 1 | .cs | 13 | `RosteringProblemSolution` |
| `Templates/FJSP_BASIC_BRICK/Set/Set_Operation.cs` | 0 | 0 | .cs | 1 | `Set_Operation` |
| `Templates/FJSP_BASIC_BRICK/Set/Set_Lot.cs` | 0 | 0 | .cs | 1 | `Set_Lot` |
| `Templates/FJSP_BASIC_BRICK/Set/Set_Eqp.cs` | 0 | 0 | .cs | 1 | `Set_Eqp` |
| `Templates/FJSP_BASIC_BRICK/Program.cs` | 18 | 21 | .cs | 13 | `Program` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_SoftMakespanTarget.cs` | 1 | 1 | .cs | 1 | `Parameter_SoftMakespanTarget` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_ProcessTime.cs` | 0 | 0 | .cs | 1 | `Parameter_ProcessTime` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_NoOverlapForwardOffset.cs` | 1 | 1 | .cs | 1 | `Parameter_NoOverlapForwardOffset` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_NoOverlapBackwardOffset.cs` | 1 | 1 | .cs | 1 | `Parameter_NoOverlapBackwardOffset` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_MakespanPenalty.cs` | 1 | 1 | .cs | 1 | `Parameter_MakespanPenalty` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_MakespanFloor.cs` | 1 | 1 | .cs | 1 | `Parameter_MakespanFloor` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_ExactlyOne.cs` | 1 | 1 | .cs | 1 | `Parameter_ExactlyOne` |
| `Templates/RosteringProblem/Set/Set_Group.cs` | 0 | 0 | .cs | 1 | `Set_Group` |
| `Templates/RosteringProblem/Set/Set_Employee.cs` | 0 | 0 | .cs | 1 | `Set_Employee` |
| `Templates/RosteringProblem/Set/Set_Date.cs` | 0 | 0 | .cs | 1 | `Set_Date` |
| `Templates/RosteringProblem/RosteringProblem.sln` | 0 | 0 | .sln | 1 |  |
| `Templates/RosteringProblem/RosteringProblem.csproj` | 0 | 4 | .csproj | 1 |  |
| `Templates/Tutorial/Data/Dataload.cs` | 4 | 4 | .cs | 6 | `Dataload` |
| `Templates/RosteringProblem/Program.cs` | 18 | 23 | .cs | 18 | `Program` |
| `Templates/FJSP_BASIC_BRICK/Objective/ObjectiveFunction.cs` | 1 | 1 | .cs | 1 | `ObjectiveFunction` |
| `Templates/Tutorial/Constraint/Constraint_SetupLink.cs` | 1 | 1 | .cs | 4 | `Constraint_SetupLink` |
| `Templates/Tutorial/Constraint/Constraint_Demand.cs` | 2 | 2 | .cs | 4 | `Constraint_Demand` |
| `Templates/Tutorial/Constraint/Constraint_Capacity.cs` | 0 | 0 | .cs | 4 | `Constraint_Capacity` |
| `Templates/Tutorial/Constraint/Constraint_BatchDef.cs` | 0 | 0 | .cs | 4 | `Constraint_BatchDef` |
| `Templates/RosteringProblem/Data/Dataload.cs` | 13 | 13 | .cs | 25 | `Dataload` |
| `Templates/FJSP_BASIC_BRICK/FJSP_BASIC_BRICK.csproj` | 0 | 4 | .csproj | 1 |  |
| `Templates/FJSP_BASIC_BRICK/Data/Dataload.cs` | 9 | 9 | .cs | 16 | `Dataload` |
| `Templates/RosteringProblem/Constraint/Constraint_FullfillDemand.cs` | 0 | 0 | .cs | 1 | `Constraint_FullfillDemand` |
| `Templates/RosteringProblem/Parameter/Parameter_DoubleOffWindow.cs` | 1 | 1 | .cs | 1 | `Parameter_DoubleOffWindow` |
| `Templates/RosteringProblem/Parameter/Parameter_DoubleOffThreshold.cs` | 1 | 1 | .cs | 1 | `Parameter_DoubleOffThreshold` |
| `Templates/RosteringProblem/Parameter/Parameter_DoubleOffLT2Penalty.cs` | 0 | 0 | .cs | 1 | `Parameter_DoubleOffLT2Penalty` |
| `Templates/RosteringProblem/Constraint/Constraint_DoubleOffLT2.cs` | 2 | 2 | .cs | 5 | `Constraint_DoubleOffLT2` |
| `Templates/RosteringProblem/Parameter/Parameter_CrossGroup.cs` | 1 | 1 | .cs | 2 | `Parameter_CrossGroup` |
| `Templates/RosteringProblem/Parameter/Parameter_BelowAVGPenalty.cs` | 0 | 0 | .cs | 1 | `Parameter_BelowAVGPenalty` |
| `Templates/RosteringProblem/Constraint/Constraint_CrossGroup.cs` | 0 | 0 | .cs | 2 | `Constraint_CrossGroup` |
| `Templates/RosteringProblem/Parameter/Parameter_BackupGroup.cs` | 0 | 0 | .cs | 2 | `Parameter_BackupGroup` |
| `Templates/RosteringProblem/Constraint/Constraint_BelowAVG.cs` | 1 | 1 | .cs | 2 | `Constraint_BelowAVG` |
| `Templates/RosteringProblem/Constraint/Constraint_OneGroup.cs` | 0 | 0 | .cs | 1 | `Constraint_OneGroup` |
| `Templates/RosteringProblem/Constraint/Constraint_OffOneDay.cs` | 1 | 1 | .cs | 3 | `Constraint_OffOneDay` |
| `Templates/RosteringProblem/Constraint/Constraint_NightToDay.cs` | 1 | 1 | .cs | 2 | `Constraint_NightToDay` |
| `Templates/RosteringProblem/Constraint/Constraint_PreAssign.cs` | 0 | 0 | .cs | 1 | `Constraint_PreAssign` |
| `Templates/RosteringProblem/Constraint/Constraint_SixDayWork.cs` | 1 | 1 | .cs | 4 | `Constraint_SixDayWork` |
| `Templates/RosteringProblem/Parameter/Parameter_NightToDayPenalty.cs` | 0 | 0 | .cs | 1 | `Parameter_NightToDayPenalty` |
| `Templates/RosteringProblem/Parameter/Parameter_NightToDay.cs` | 1 | 1 | .cs | 2 | `Parameter_NightToDay` |
| `Templates/RosteringProblem/Parameter/Parameter_GroupMismatchPenalty.cs` | 0 | 0 | .cs | 1 | `Parameter_GroupMismatchPenalty` |
| `Templates/RosteringProblem/Parameter/Parameter_NightToDayWindow.cs` | 1 | 1 | .cs | 1 | `Parameter_NightToDayWindow` |
| `Templates/RosteringProblem/Parameter/Parameter_OffOneDayPenalty.cs` | 0 | 0 | .cs | 1 | `Parameter_OffOneDayPenalty` |
| `Templates/RosteringProblem/Constraint/Constraint_WeekendLT4.cs` | 1 | 1 | .cs | 2 | `Constraint_WeekendLT4` |
| `Templates/TSP_MultiDimSet/TSP_MultiDimSet.csproj` | 0 | 4 | .csproj | 1 |  |
| `Templates/TSP_MultiDimSet/status.json` | 0 | 0 | .json | 0 |  |
| `Templates/.gitignore` | 0 | 0 | .gitignore | 6 |  |
| `Templates/TSP_MultiDimSet/Program.cs` | 16 | 19 | .cs | 12 | `Program` |
| `Templates/TSP_MultiDimSet/Variable/VariableC_VisitOrder.cs` | 0 | 0 | .cs | 1 | `VariableC_VisitOrder` |
| `Templates/TSP_MultiDimSet/Solution/TSP_MultiDimSetSolution.cs` | 1 | 1 | .cs | 9 | `TSP_MultiDimSetSolution` |
| `Templates/TSP_MultiDimSet/Variable/VariableB_UseArc.cs` | 0 | 0 | .cs | 1 | `VariableB_UseArc` |
| `Templates/RosteringProblem/Parameter/Parameter_OffOneDayWindow.cs` | 1 | 1 | .cs | 1 | `Parameter_OffOneDayWindow` |
| `Templates/TSP_MultiDimSet/Parameter/Parameter_ArcCost.cs` | 0 | 0 | .cs | 1 | `Parameter_ArcCost` |
| `Templates/RosteringProblem/Parameter/Parameter_PreAssign.cs` | 0 | 0 | .cs | 2 | `Parameter_PreAssign` |
| `Templates/RosteringProblem/Parameter/Parameter_One.cs` | 2 | 2 | .cs | 2 | `Parameter_One` |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_CustomerInDegree.cs` | 0 | 0 | .cs | 4 | `Constraint_CustomerInDegree` |
| `Templates/RosteringProblem/Parameter/Parameter_ShiftDemand.cs` | 0 | 0 | .cs | 1 | `Parameter_ShiftDemand` |
| `Templates/RosteringProblem/Parameter/Parameter_SixDayPenalty.cs` | 0 | 0 | .cs | 1 | `Parameter_SixDayPenalty` |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_CustomerOutDegree.cs` | 0 | 0 | .cs | 4 | `Constraint_CustomerOutDegree` |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_DepotInDegree.cs` | 0 | 0 | .cs | 4 | `Constraint_DepotInDegree` |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_DepotOutDegree.cs` | 0 | 0 | .cs | 4 | `Constraint_DepotOutDegree` |
| `Templates/RosteringProblem/Parameter/Parameter_SixDayWindow.cs` | 1 | 1 | .cs | 1 | `Parameter_SixDayWindow` |
| `Templates/RosteringProblem/Parameter/Parameter_Weekend4DayPenalty.cs` | 0 | 0 | .cs | 1 | `Parameter_Weekend4DayPenalty` |
| `Templates/RosteringProblem/Parameter/Parameter_WeekendOffThreshold.cs` | 1 | 1 | .cs | 1 | `Parameter_WeekendOffThreshold` |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_SubtourMTZ.cs` | 0 | 0 | .cs | 4 | `Constraint_SubtourMTZ` |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_VisitOrderRange.cs` | 1 | 1 | .cs | 5 | `Constraint_VisitOrderRange` |
| `Templates/TSP_MultiDimSet/Objective/ObjectiveFunction.cs` | 0 | 0 | .cs | 1 | `ObjectiveFunction` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_CompleteDef.cs` | 1 | 1 | .cs | 1 | `Constraint_CompleteDef` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_AssignOneEqp.cs` | 1 | 1 | .cs | 1 | `Constraint_AssignOneEqp` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_MakespanDef.cs` | 2 | 2 | .cs | 2 | `Constraint_MakespanDef` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_MakespanInfeasibleCap.cs` | 2 | 2 | .cs | 5 | `Constraint_MakespanInfeasibleCap` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_MakespanWindow.cs` | 1 | 1 | .cs | 1 | `Constraint_MakespanWindow` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_MakespanTargetSoft.cs` | 5 | 5 | .cs | 6 | `Constraint_MakespanTargetSoft` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_RoutePrecedence.cs` | 2 | 2 | .cs | 2 | `Constraint_RoutePrecedence` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_NoOverlap.cs` | 1 | 1 | .cs | 8 | `Constraint_NoOverlap` |
| `Templates/TSP_MultiDimSet/Set/Set_Node.cs` | 0 | 0 | .cs | 1 | `Set_Node` |
| `Templates/TSP_MultiDimSet/Set/Set_Depot.cs` | 0 | 0 | .cs | 1 | `Set_Depot` |
| `Templates/TSP_MultiDimSet/Set/Set_Customer.cs` | 0 | 0 | .cs | 1 | `Set_Customer` |
| `Templates/TSP_MultiDimSet/Set/Set_Arc.cs` | 1 | 1 | .cs | 4 | `Set_Arc` |
| `Templates/ModelTuner/ModelTuner.csproj` | 0 | 0 | .csproj | 0 |  |
| `Templates/TSP_MultiDimSet/Data/Dataload.cs` | 2 | 2 | .cs | 2 | `Dataload` |
| `Templates/ModelTuner/Tuning/TuningRound.cs` | 21 | 22 | .cs | 14 | `TuningRound` |
| `Templates/ModelTuner/Tuning/TunerWorkspace.cs` | 95 | 37 | .cs | 36 | `TuningInstance`, `TunerWorkspace` |
| `Templates/ModelTuner/Tuning/TunerCli.cs` | 0 | 0 | .cs | 1 | `TunerCli` |
| `Templates/ModelTuner/Tuning/RoundFacts.cs` | 138 | 212 | .cs | 42 | `RoundFacts` |
| `Templates/ModelTuner/Tuning/CplexTuner.cs` | 12 | 12 | .cs | 12 | `CplexTuner` |
| `Templates/ModelTuner/Program.cs` | 27 | 27 | .cs | 33 |  |
| `Templates/ModelInspector/ModelInspector.sln` | 0 | 0 | .sln | 1 |  |
| `Templates/ModelInspector/ModelInspector.csproj` | 0 | 0 | .csproj | 0 |  |
| `Templates/ModelInspector/InspectionOptions.cs` | 3 | 3 | .cs | 17 | `InspectionOptions` |
| `Templates/ModelInspector/Program.cs` | 12 | 17 | .cs | 9 | `Program` |
| `Templates/Sudoku_SHC279/Constraint/Constraint_RowDigit.cs` | 0 | 0 | .cs | 1 | `Constraint_RowDigit` |
| `Templates/Sudoku_SHC279/Constraint/Constraint_Given.cs` | 0 | 0 | .cs | 1 | `Constraint_Given` |
| `Templates/Sudoku_SHC279/Constraint/Constraint_ColumnDigit.cs` | 0 | 0 | .cs | 1 | `Constraint_ColumnDigit` |
| `Templates/Sudoku_SHC279/Constraint/Constraint_CellValue.cs` | 0 | 0 | .cs | 1 | `Constraint_CellValue` |
| `Templates/Sudoku_SHC279/Constraint/Constraint_BlockDigit.cs` | 0 | 0 | .cs | 1 | `Constraint_BlockDigit` |
| `Templates/Sudoku_SHC279/Program.cs` | 15 | 18 | .cs | 11 | `Program` |
| `Templates/Sudoku_SHC279/Data/Dataload.cs` | 4 | 4 | .cs | 4 | `Dataload` |
| `Templates/Sudoku_SHC279/Parameter/Parameter_ObjCoef.cs` | 0 | 0 | .cs | 1 | `Parameter_ObjCoef` |
| `Templates/Sudoku_SHC279/Parameter/Parameter_ExactlyOne.cs` | 1 | 1 | .cs | 1 | `Parameter_ExactlyOne` |
| `Templates/ModelInspector/Reporting/ReportWriter.cs` | 23 | 23 | .cs | 7 | `ReportWriter` |
| `Templates/ModelInspector/Reporting/ModelInspection.cs` | 32 | 24 | .cs | 52 | `ModelInspection`, `ArtifactFile` |
| `Templates/Sudoku_SHC279/Sudoku_SHC279.csproj` | 0 | 4 | .csproj | 1 |  |
| `Templates/Sudoku_SHC279/Objective/ObjectiveFunction.cs` | 1 | 1 | .cs | 1 | `ObjectiveFunction` |
| `Templates/Sudoku_SHC279/Solution/Sudoku_SHC279Solution.cs` | 0 | 0 | .cs | 1 | `Sudoku_SHC279Solution` |
| `Templates/Sudoku_SHC279/Variable/VariableB_CellDigit.cs` | 0 | 0 | .cs | 1 | `VariableB_CellDigit` |
| `Templates/Sudoku_SHC279/Set/Set_Column.cs` | 0 | 0 | .cs | 1 | `Set_Column` |
| `Templates/Sudoku_SHC279/Set/Set_BlockCell.cs` | 1 | 1 | .cs | 1 | `Set_BlockCell` |
| `Templates/Sudoku_SHC279/Set/Set_Block.cs` | 0 | 0 | .cs | 1 | `Set_Block` |
| `Templates/Sudoku_SHC279/Set/Set_Digit.cs` | 0 | 0 | .cs | 1 | `Set_Digit` |
| `Templates/Sudoku_SHC279/Set/Set_Given.cs` | 0 | 0 | .cs | 1 | `Set_Given` |
| `Templates/Sudoku_SHC279/Set/Set_Row.cs` | 0 | 0 | .cs | 1 | `Set_Row` |

### Dependency Graph

| 使用者 | 相依 |
| --- | --- |
| `tests/OptimFoundation.Cplex.Tests/OptimFoundation.Cplex.Tests.csproj` | `..\..\src\OptimFoundation.Core\OptimFoundation.Core.csproj` |
| `tests/OptimFoundation.Cplex.Tests/OptimFoundation.Cplex.Tests.csproj` | `..\..\src\OptimFoundation.Cplex\OptimFoundation.Cplex.csproj` |
| `tests/OptimFoundation.Cplex.Tests/OptimFoundation.Cplex.Tests.csproj` | `..\..\src\OptimFoundation.Generators\OptimFoundation.Generators.csproj` |
| `src/OptimFoundation.Cplex/OptimFoundation.Cplex.csproj` | `..\OptimFoundation.Core\OptimFoundation.Core.csproj` |
| `Templates/Tutorial/Tutorial.csproj` | `..\..\src\OptimFoundation.Core\OptimFoundation.Core.csproj` |
| `Templates/Tutorial/Tutorial.csproj` | `..\..\src\OptimFoundation.Cplex\OptimFoundation.Cplex.csproj` |
| `Templates/RosteringProblem/RosteringProblem.csproj` | `..\..\src\OptimFoundation.Core\OptimFoundation.Core.csproj` |
| `Templates/RosteringProblem/RosteringProblem.csproj` | `..\..\src\OptimFoundation.Cplex\OptimFoundation.Cplex.csproj` |
| `Templates/RosteringProblem/RosteringProblem.csproj` | `..\..\src\OptimFoundation.Generators\OptimFoundation.Generators.csproj` |
| `Templates/FJSP_BASIC_BRICK/FJSP_BASIC_BRICK.csproj` | `..\..\src\OptimFoundation.Core\OptimFoundation.Core.csproj` |
| `Templates/FJSP_BASIC_BRICK/FJSP_BASIC_BRICK.csproj` | `..\..\src\OptimFoundation.Cplex\OptimFoundation.Cplex.csproj` |
| `Templates/FJSP_BASIC_BRICK/FJSP_BASIC_BRICK.csproj` | `..\..\src\OptimFoundation.Generators\OptimFoundation.Generators.csproj` |
| `Templates/TSP_MultiDimSet/TSP_MultiDimSet.csproj` | `..\..\src\OptimFoundation.Core\OptimFoundation.Core.csproj` |
| `Templates/TSP_MultiDimSet/TSP_MultiDimSet.csproj` | `..\..\src\OptimFoundation.Cplex\OptimFoundation.Cplex.csproj` |
| `Templates/TSP_MultiDimSet/TSP_MultiDimSet.csproj` | `..\..\src\OptimFoundation.Generators\OptimFoundation.Generators.csproj` |
| `Templates/ModelTuner/ModelTuner.csproj` | `..\..\src\OptimFoundation.Core\OptimFoundation.Core.csproj` |
| `Templates/ModelTuner/ModelTuner.csproj` | `..\..\src\OptimFoundation.Cplex\OptimFoundation.Cplex.csproj` |
| `Templates/ModelInspector/ModelInspector.csproj` | `..\..\src\OptimFoundation.Core\OptimFoundation.Core.csproj` |
| `Templates/ModelInspector/ModelInspector.csproj` | `..\..\src\OptimFoundation.Cplex\OptimFoundation.Cplex.csproj` |
| `Templates/Sudoku_SHC279/Sudoku_SHC279.csproj` | `..\..\src\OptimFoundation.Core\OptimFoundation.Core.csproj` |
| `Templates/Sudoku_SHC279/Sudoku_SHC279.csproj` | `..\..\src\OptimFoundation.Cplex\OptimFoundation.Cplex.csproj` |
| `Templates/Sudoku_SHC279/Sudoku_SHC279.csproj` | `..\..\src\OptimFoundation.Generators\OptimFoundation.Generators.csproj` |

### Symbol Index

| 檔案 | Symbol | 類型 | 行號 |
| --- | --- | --- | --- |
| `tests/OptimFoundation.Cplex.Tests/Unit/ConfigSummaryTests.cs` | `ConfigSummaryTests` | class | 14 |
| `tests/OptimFoundation.Cplex.Tests/Unit/ZzScratchProbe.cs` | `ZzScratchProbe` | class | 10 |
| `tests/OptimFoundation.Cplex.Tests/Unit/VariableBuilderTests.cs` | `VariableBuilderTests` | class | 7 |
| `tests/OptimFoundation.Cplex.Tests/Unit/StringOverloadParityTests.cs` | `StringOverloadParityTests` | class | 11 |
| `tests/OptimFoundation.Cplex.Tests/Unit/ScaleGuardTests.cs` | `ScaleGuardTests` | class | 16 |
| `tests/OptimFoundation.Cplex.Tests/Unit/RunnerSymmetryTests.cs` | `RunnerSymmetryTests` | class | 8 |
| `tests/OptimFoundation.Cplex.Tests/Unit/PoolSemanticsAndMipStartTests.cs` | `PoolSemanticsAndMipStartTests` | class | 9 |
| `tests/OptimFoundation.Cplex.Tests/Unit/OracleSolutionSinkTests.cs` | `OracleSolutionSinkTests` | class | 12 |
| `tests/OptimFoundation.Cplex.Tests/Unit/ObservabilityTests.cs` | `ObservabilityTests` | class | 15 |
| `tests/OptimFoundation.Cplex.Tests/Unit/ModelElementBaseTests.cs` | `ModelElementBaseTests` | class | 7 |
| `tests/OptimFoundation.Cplex.Tests/Unit/GeneratorNumericCoverageTests.cs` | `Set_GncItem` | class | 26 |
| `tests/OptimFoundation.Cplex.Tests/Unit/GeneratorNumericCoverageTests.cs` | `Parameter_GncProfit` | class | 32 |
| `tests/OptimFoundation.Cplex.Tests/Unit/GeneratorNumericCoverageTests.cs` | `Parameter_GncScalar` | class | 39 |
| `tests/OptimFoundation.Cplex.Tests/Unit/GeneratorNumericCoverageTests.cs` | `VariableC_GncAmount` | class | 45 |
| `tests/OptimFoundation.Cplex.Tests/Unit/GeneratorNumericCoverageTests.cs` | `VariableI_GncCount` | class | 51 |
| `tests/OptimFoundation.Cplex.Tests/Unit/GeneratorNumericCoverageTests.cs` | `GncDataload` | class | 55 |
| `tests/OptimFoundation.Cplex.Tests/Unit/GeneratorNumericCoverageTests.cs` | `GeneratorNumericCoverageTests` | class | 67 |
| `tests/OptimFoundation.Cplex.Tests/Unit/FakeDbCtrl.cs` | `FakeDbCtrl` | class | 9 |
| `tests/OptimFoundation.Cplex.Tests/Unit/ExperimentSaveTests.cs` | `ExperimentSaveTests` | class | 13 |
| `tests/OptimFoundation.Cplex.Tests/Unit/EngineBaseTests.cs` | `EngineBaseTests` | class | 7 |
| `tests/OptimFoundation.Cplex.Tests/Unit/DbCtrlBaseTransactionTests.cs` | `FakeDbTransaction` | class | 12 |
| `tests/OptimFoundation.Cplex.Tests/Unit/DbCtrlBaseTransactionTests.cs` | `FakeDbConnection` | class | 31 |
| `tests/OptimFoundation.Cplex.Tests/Unit/DbCtrlBaseTransactionTests.cs` | `TestableDbCtrl` | class | 60 |
| `tests/OptimFoundation.Cplex.Tests/Unit/DbCtrlBaseTransactionTests.cs` | `DbCtrlBaseTransactionTests` | class | 106 |
| `docs/index.html` | `Set_Arc` | class | 23 |
| `docs/index.html` | `Parameter_ArcCost` | class | 28 |
| `docs/index.html` | `VariableB_UseArc` | class | 33 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `VarS` | class | 6 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `VarDG` | class | 11 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `VarInt` | class | 17 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `VariableB_Pick` | class | 23 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `VariableC_Amt` | class | 28 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `VariableI_Cnt` | class | 33 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `VariableX_LegacyAmt` | class | 39 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `VariableY_LegacyCnt` | class | 44 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `VariableC_ArcFlow` | class | 49 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `VariableC_ArcFlowByDate` | class | 55 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `VariableC_ArcFlowWrongArity` | class | 62 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `Set_Arc` | class | 67 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `Constraint_Test` | class | 73 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `ParamX` | class | 76 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/MockEngine.cs` | `MockEngine` | class | 9 |
| `tests/OptimFoundation.Cplex.Tests/Mocks/MockEngine.cs` | `MockConfig` | class | 127 |
| `src/OptimFoundation.Generators/IsExternalInit.cs` | `IsExternalInit` | class | 5 |
| `src/OptimFoundation.Generators/AutoSetsGenerator.cs` | `AutoSetsGenerator` | class | 26 |
| `src/OptimFoundation.Generators/AutoSetsGenerator.cs` | `OptSetAttribute` | class | 105 |
| `src/OptimFoundation.Generators/AutoSetsGenerator.cs` | `OptParamAttribute` | class | 109 |
| `src/OptimFoundation.Generators/AutoSetsGenerator.cs` | `OptVarAttribute` | class | 113 |
| `src/OptimFoundation.Generators/AutoSetsGenerator.cs` | `OptDimAttribute` | class | 121 |
| `src/OptimFoundation.Core/ModelElementBase.cs` | `ModelElementBase` | class | 10 |
| `src/OptimFoundation.Core/ModelElementBase.cs` | `SetRowBase` | class | 82 |
| `src/OptimFoundation.Core/ModelElementBase.cs` | `ParameterBase` | class | 90 |
| `src/OptimFoundation.Core/ModelElementBase.cs` | `VariableBase` | class | 92 |
| `src/OptimFoundation.Core/ModelElementBase.cs` | `ConstraintBase` | class | 93 |
| `src/OptimFoundation.Core/DataContext.cs` | `DataIssueKind` | enum | 13 |
| `src/OptimFoundation.Core/DataContext.cs` | `DataIssue` | class | 15 |
| `src/OptimFoundation.Core/DataContext.cs` | `DataValidator` | class | 25 |
| `src/OptimFoundation.Core/DataContext.cs` | `OptData` | class | 114 |
| `src/OptimFoundation.Core/DataContext.cs` | `ParamRow` | class | 144 |
| `src/OptimFoundation.Core/DataContext.cs` | `SetRegistration` | class | 156 |
| `src/OptimFoundation.Core/DataContext.cs` | `ParamRegistration` | class | 176 |
| `src/OptimFoundation.Core/DataContext.cs` | `DataContext` | class | 191 |
| `src/OptimFoundation.Core/DataContext.cs` | `ParameterLookupExtensions` | class | 266 |
| `src/OptimFoundation.Core/EngineBase.cs` | `ISolverConfig` | interface | 13 |
| `src/OptimFoundation.Core/EngineBase.cs` | `ISolverEngine` | interface | 54 |
| `src/OptimFoundation.Core/EngineBase.cs` | `ISpecialConstraints` | interface | 92 |
| `src/OptimFoundation.Core/EngineBase.cs` | `SolveStatus` | enum | 111 |
| `src/OptimFoundation.Core/EngineBase.cs` | `VarType` | enum | 136 |
| `src/OptimFoundation.Core/EngineBase.cs` | `ConstraintSense` | enum | 149 |
| `src/OptimFoundation.Core/EngineBase.cs` | `ObjectiveSense` | enum | 162 |
| `src/OptimFoundation.Core/EngineBase.cs` | `ModelType` | enum | 172 |
| `src/OptimFoundation.Core/EngineBase.cs` | `OptBounds` | class | 190 |
| `src/OptimFoundation.Core/EngineBase.cs` | `EngineBase` | class | 202 |
| `src/OptimFoundation.Core/VariablePrefixNaming.cs` | `VariablePrefixNaming` | class | 10 |
| `src/OptimFoundation.Core/ModelNaming.cs` | `ModelNaming` | class | 13 |
| `src/OptimFoundation.Core/VariableBuilder.cs` | `VariableBuilder` | class | 16 |
| `src/OptimFoundation.Cplex/CplexConfig.cs` | `CplexConfig` | class | 43 |
| `src/OptimFoundation.Cplex/OptEngine.Configuration.cs` | `OptEngine` | class | 17 |
| `src/OptimFoundation.Cplex/OptEngine.cs` | `OptEngine` | class | 20 |
| `src/OptimFoundation.Cplex/OptExperiment.cs` | `OptExperiment` | class | 13 |
| `src/OptimFoundation.Cplex/OptProject.cs` | `OptProject` | class | 13 |
| `src/OptimFoundation.Cplex/OptModel.cs` | `OptModel` | class | 12 |
| `src/OptimFoundation.Core/Experiments/SolveMetrics.cs` | `SolveMetrics` | class | 9 |
| `src/OptimFoundation.Core/Experiments/SolveMetrics.cs` | `ConvergencePoint` | class | 139 |
| `src/OptimFoundation.Core/Experiments/Experiment.cs` | `Experiment` | class | 15 |
| `src/OptimFoundation.Core/Experiments/Experiment.cs` | `ITrajectorySource` | interface | 132 |
| `src/OptimFoundation.Core/Experiments/Experiment.cs` | `Trial` | class | 147 |
| `src/OptimFoundation.Core/Experiments/Experiment.cs` | `ConfigSummary` | class | 237 |
| `src/OptimFoundation.Core/Experiments/Experiment.cs` | `BaselineComparison` | enum | 349 |
| `src/OptimFoundation.Core/Experiments/Experiment.cs` | `BaselineComparer` | class | 378 |
| `src/OptimFoundation.Core/Experiments/ExpCsvWriter.cs` | `CsvExperimentWriter` | class | 22 |
| `src/OptimFoundation.Core/Experiments/ExpCsvWriter.cs` | `MetaCsvWriter` | class | 245 |
| `src/OptimFoundation.Core/Experiments/ExpCsvWriter.cs` | `SummaryCsvWriter` | class | 392 |
| `src/OptimFoundation.Core/Experiments/ExpCsvWriter.cs` | `TrajectoryCsvWriter` | class | 454 |
| `src/OptimFoundation.Core/Experiments/ExpCsvWriter.cs` | `CumulativeCsv` | class | 527 |
| `src/OptimFoundation.Core/Experiments/ConfigSnapshot.cs` | `ConfigSnapshot` | class | 10 |
| `tests/OptimFoundation.Cplex.Tests/Integration/ZzProbeExportExtTests.cs` | `ZzProbeExportExtTests` | class | 9 |
| `tests/OptimFoundation.Cplex.Tests/Integration/SolverParamValueMatrixTests.cs` | `SolverParamValueMatrixTests` | class | 32 |
| `tests/OptimFoundation.Cplex.Tests/Integration/SolverParamCoverageTests.cs` | `SolverParamCoverageTests` | class | 20 |
| `tests/OptimFoundation.Cplex.Tests/Integration/SolutionPipelineIntegrationTests.cs` | `SolutionPipelineIntegrationTests` | class | 12 |
| `tests/OptimFoundation.Cplex.Tests/Integration/ProjectScopeIntegrationTests.cs` | `ProjectScopeIntegrationTests` | class | 16 |
| `tests/OptimFoundation.Cplex.Tests/Integration/OptEngineIntegrationTests.cs` | `OptEngineIntegrationTests` | class | 12 |
| `tests/OptimFoundation.Cplex.Tests/Integration/ModelImportIntegrationTests.cs` | `ModelImportIntegrationTests` | class | 12 |
| `tests/OptimFoundation.Cplex.Tests/Integration/ExperimentIntegrationTests.cs` | `ExperimentIntegrationTests` | class | 17 |
| `src/OptimFoundation.Core/IO/TabularData.cs` | `TabularData` | class | 11 |
| `src/OptimFoundation.Core/IO/ModelRowMapper.cs` | `ModelRowMapper` | class | 12 |
| `src/OptimFoundation.Core/IO/InMemoryDataSource.cs` | `InMemoryDataSource` | class | 12 |
| `src/OptimFoundation.Core/IO/IDataSource.cs` | `IDataSource` | interface | 11 |
| `src/OptimFoundation.Core/IO/IDataSource.cs` | `ISolutionSink` | interface | 40 |
| `src/OptimFoundation.Core/IO/IDataSource.cs` | `ISolutionBatch` | interface | 50 |
| `src/OptimFoundation.Core/Infrastructure/ProjectConfig.cs` | `ProjectConfig` | class | 9 |
| `src/OptimFoundation.Core/Infrastructure/Logging.cs` | `Logging` | class | 13 |
| `src/OptimFoundation.Core/Infrastructure/FolderDir.cs` | `FolderDir` | class | 11 |
| `src/OptimFoundation.Core/Infrastructure/FolderDir.cs` | `ProjFolder` | class | 58 |
| `src/OptimFoundation.Core/Infrastructure/ClassInfo.cs` | `ReflectionHelper` | class | 11 |
| `src/OptimFoundation.Core/Infrastructure/ClassInfo.cs` | `ClassInfo` | class | 95 |
| `src/OptimFoundation.Core/IO/db/OracleDbCtrl.cs` | `OracleDbCtrl` | class | 16 |
| `src/OptimFoundation.Core/IO/db/OracleDbCtrl.cs` | `OracleSolutionSink` | class | 412 |
| `src/OptimFoundation.Core/IO/db/IDbCtrl.cs` | `IDbCtrl` | interface | 13 |
| `src/OptimFoundation.Core/IO/db/DbDataSource.cs` | `DbDataSource` | class | 14 |
| `src/OptimFoundation.Core/IO/db/DbCtrlBase.cs` | `DbCtrlBase` | class | 12 |
| `src/OptimFoundation.Core/IO/csv/CsvDataSource.cs` | `CsvDataSource` | class | 13 |
| `src/OptimFoundation.Core/IO/csv/CsvDataSource.cs` | `CsvSolutionSink` | class | 61 |
| `src/OptimFoundation.Core/IO/csv/CsvCtrl.cs` | `CsvCtrl` | class | 13 |
| `Templates/Tutorial/Variable/VariableI_Batch.cs` | `VariableI_Batch` | class | 9 |
| `Templates/Tutorial/Variable/VariableC_Produce.cs` | `VariableC_Produce` | class | 10 |
| `Templates/Tutorial/Variable/VariableB_Setup.cs` | `VariableB_Setup` | class | 10 |
| `Templates/Tutorial/Solution/TutorialSolution.cs` | `TutorialSolution` | class | 8 |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableC_Start.cs` | `VariableC_Start` | class | 9 |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableC_Makespan.cs` | `VariableC_Makespan` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableC_Complete.cs` | `VariableC_Complete` | class | 9 |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableB_Precede.cs` | `VariableB_Precede` | class | 14 |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableB_Assign.cs` | `VariableB_Assign` | class | 10 |
| `Templates/Tutorial/Set/Set_Shift.cs` | `Set_Shift` | class | 8 |
| `Templates/Tutorial/Set/Set_Product.cs` | `Set_Product` | class | 8 |
| `Templates/Tutorial/Set/Set_Machine.cs` | `Set_Machine` | class | 8 |
| `Templates/Tutorial/Set/Set_Date.cs` | `Set_Date` | class | 8 |
| `Templates/Tutorial/Program.cs` | `Program` | class | 10 |
| `Templates/RosteringProblem/Variable/VariableC_WeekendLT4.cs` | `VariableC_WeekendLT4` | class | 8 |
| `Templates/RosteringProblem/Variable/VariableC_BelowAVG.cs` | `VariableC_BelowAVG` | class | 8 |
| `Templates/RosteringProblem/Variable/VariableB_SixDayWork.cs` | `VariableB_SixDayWork` | class | 9 |
| `Templates/RosteringProblem/Variable/VariableB_ShiftAssign.cs` | `VariableB_ShiftAssign` | class | 10 |
| `Templates/RosteringProblem/Variable/VariableB_Off1Day.cs` | `VariableB_Off1Day` | class | 9 |
| `Templates/RosteringProblem/Variable/VariableB_NightToDay.cs` | `VariableB_NightToDay` | class | 9 |
| `Templates/RosteringProblem/Variable/VariableB_GroupMismatch.cs` | `VariableB_GroupMismatch` | class | 9 |
| `Templates/RosteringProblem/Variable/VariableB_DoubleOffLT2.cs` | `VariableB_DoubleOffLT2` | class | 8 |
| `Templates/RosteringProblem/Variable/VariableB_DoubleOffFlag.cs` | `VariableB_DoubleOffFlag` | class | 9 |
| `Templates/FJSP_BASIC_BRICK/Solution/FJSP_BASIC_BRICKSolution.cs` | `FJSP_BASIC_BRICKSolution` | class | 8 |
| `Templates/Tutorial/Parameter/Parameter_UnitProfit.cs` | `Parameter_UnitProfit` | class | 8 |
| `Templates/Tutorial/Parameter/Parameter_SetupCost.cs` | `Parameter_SetupCost` | class | 8 |
| `Templates/Tutorial/Parameter/Parameter_MachineHours.cs` | `Parameter_MachineHours` | class | 9 |
| `Templates/Tutorial/Parameter/Parameter_Demand.cs` | `Parameter_Demand` | class | 10 |
| `Templates/Tutorial/Parameter/Parameter_Capacity.cs` | `Parameter_Capacity` | class | 10 |
| `Templates/Tutorial/Parameter/Parameter_BatchSize.cs` | `Parameter_BatchSize` | class | 8 |
| `Templates/Tutorial/Objective/ObjectiveFunction.cs` | `ObjectiveFunction` | class | 7 |
| `Templates/RosteringProblem/Objective/ObjectiveFunction.cs` | `ObjectiveFunction` | class | 8 |
| `Templates/RosteringProblem/Solution/RosteringProblemSolution.cs` | `RosteringProblemSolution` | class | 8 |
| `Templates/FJSP_BASIC_BRICK/Set/Set_Operation.cs` | `Set_Operation` | class | 8 |
| `Templates/FJSP_BASIC_BRICK/Set/Set_Lot.cs` | `Set_Lot` | class | 8 |
| `Templates/FJSP_BASIC_BRICK/Set/Set_Eqp.cs` | `Set_Eqp` | class | 8 |
| `Templates/FJSP_BASIC_BRICK/Program.cs` | `Program` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_SoftMakespanTarget.cs` | `Parameter_SoftMakespanTarget` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_ProcessTime.cs` | `Parameter_ProcessTime` | class | 10 |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_NoOverlapForwardOffset.cs` | `Parameter_NoOverlapForwardOffset` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_NoOverlapBackwardOffset.cs` | `Parameter_NoOverlapBackwardOffset` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_MakespanPenalty.cs` | `Parameter_MakespanPenalty` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_MakespanFloor.cs` | `Parameter_MakespanFloor` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_ExactlyOne.cs` | `Parameter_ExactlyOne` | class | 7 |
| `Templates/RosteringProblem/Set/Set_Group.cs` | `Set_Group` | class | 8 |
| `Templates/RosteringProblem/Set/Set_Employee.cs` | `Set_Employee` | class | 8 |
| `Templates/RosteringProblem/Set/Set_Date.cs` | `Set_Date` | class | 8 |
| `Templates/Tutorial/Data/Dataload.cs` | `Dataload` | class | 9 |
| `Templates/RosteringProblem/Program.cs` | `Program` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Objective/ObjectiveFunction.cs` | `ObjectiveFunction` | class | 6 |
| `Templates/Tutorial/Constraint/Constraint_SetupLink.cs` | `Constraint_SetupLink` | class | 10 |
| `Templates/Tutorial/Constraint/Constraint_Demand.cs` | `Constraint_Demand` | class | 10 |
| `Templates/Tutorial/Constraint/Constraint_Capacity.cs` | `Constraint_Capacity` | class | 10 |
| `Templates/Tutorial/Constraint/Constraint_BatchDef.cs` | `Constraint_BatchDef` | class | 10 |
| `Templates/RosteringProblem/Data/Dataload.cs` | `Dataload` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Data/Dataload.cs` | `Dataload` | class | 8 |
| `Templates/RosteringProblem/Constraint/Constraint_FullfillDemand.cs` | `Constraint_FullfillDemand` | class | 7 |
| `Templates/RosteringProblem/Parameter/Parameter_DoubleOffWindow.cs` | `Parameter_DoubleOffWindow` | class | 7 |
| `Templates/RosteringProblem/Parameter/Parameter_DoubleOffThreshold.cs` | `Parameter_DoubleOffThreshold` | class | 7 |
| `Templates/RosteringProblem/Parameter/Parameter_DoubleOffLT2Penalty.cs` | `Parameter_DoubleOffLT2Penalty` | class | 7 |
| `Templates/RosteringProblem/Constraint/Constraint_DoubleOffLT2.cs` | `Constraint_DoubleOffLT2` | class | 11 |
| `Templates/RosteringProblem/Parameter/Parameter_CrossGroup.cs` | `Parameter_CrossGroup` | class | 10 |
| `Templates/RosteringProblem/Parameter/Parameter_BelowAVGPenalty.cs` | `Parameter_BelowAVGPenalty` | class | 7 |
| `Templates/RosteringProblem/Constraint/Constraint_CrossGroup.cs` | `Constraint_CrossGroup` | class | 8 |
| `Templates/RosteringProblem/Parameter/Parameter_BackupGroup.cs` | `Parameter_BackupGroup` | class | 10 |
| `Templates/RosteringProblem/Constraint/Constraint_BelowAVG.cs` | `Constraint_BelowAVG` | class | 8 |
| `Templates/RosteringProblem/Constraint/Constraint_OneGroup.cs` | `Constraint_OneGroup` | class | 7 |
| `Templates/RosteringProblem/Constraint/Constraint_OffOneDay.cs` | `Constraint_OffOneDay` | class | 9 |
| `Templates/RosteringProblem/Constraint/Constraint_NightToDay.cs` | `Constraint_NightToDay` | class | 8 |
| `Templates/RosteringProblem/Constraint/Constraint_PreAssign.cs` | `Constraint_PreAssign` | class | 7 |
| `Templates/RosteringProblem/Constraint/Constraint_SixDayWork.cs` | `Constraint_SixDayWork` | class | 10 |
| `Templates/RosteringProblem/Parameter/Parameter_NightToDayPenalty.cs` | `Parameter_NightToDayPenalty` | class | 7 |
| `Templates/RosteringProblem/Parameter/Parameter_NightToDay.cs` | `Parameter_NightToDay` | class | 10 |
| `Templates/RosteringProblem/Parameter/Parameter_GroupMismatchPenalty.cs` | `Parameter_GroupMismatchPenalty` | class | 7 |
| `Templates/RosteringProblem/Parameter/Parameter_NightToDayWindow.cs` | `Parameter_NightToDayWindow` | class | 7 |
| `Templates/RosteringProblem/Parameter/Parameter_OffOneDayPenalty.cs` | `Parameter_OffOneDayPenalty` | class | 7 |
| `Templates/RosteringProblem/Constraint/Constraint_WeekendLT4.cs` | `Constraint_WeekendLT4` | class | 8 |
| `Templates/TSP_MultiDimSet/Program.cs` | `Program` | class | 7 |
| `Templates/TSP_MultiDimSet/Variable/VariableC_VisitOrder.cs` | `VariableC_VisitOrder` | class | 8 |
| `Templates/TSP_MultiDimSet/Solution/TSP_MultiDimSetSolution.cs` | `TSP_MultiDimSetSolution` | class | 7 |
| `Templates/TSP_MultiDimSet/Variable/VariableB_UseArc.cs` | `VariableB_UseArc` | class | 9 |
| `Templates/RosteringProblem/Parameter/Parameter_OffOneDayWindow.cs` | `Parameter_OffOneDayWindow` | class | 7 |
| `Templates/TSP_MultiDimSet/Parameter/Parameter_ArcCost.cs` | `Parameter_ArcCost` | class | 9 |
| `Templates/RosteringProblem/Parameter/Parameter_PreAssign.cs` | `Parameter_PreAssign` | class | 11 |
| `Templates/RosteringProblem/Parameter/Parameter_One.cs` | `Parameter_One` | class | 8 |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_CustomerInDegree.cs` | `Constraint_CustomerInDegree` | class | 10 |
| `Templates/RosteringProblem/Parameter/Parameter_ShiftDemand.cs` | `Parameter_ShiftDemand` | class | 9 |
| `Templates/RosteringProblem/Parameter/Parameter_SixDayPenalty.cs` | `Parameter_SixDayPenalty` | class | 7 |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_CustomerOutDegree.cs` | `Constraint_CustomerOutDegree` | class | 10 |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_DepotInDegree.cs` | `Constraint_DepotInDegree` | class | 10 |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_DepotOutDegree.cs` | `Constraint_DepotOutDegree` | class | 10 |
| `Templates/RosteringProblem/Parameter/Parameter_SixDayWindow.cs` | `Parameter_SixDayWindow` | class | 7 |
| `Templates/RosteringProblem/Parameter/Parameter_Weekend4DayPenalty.cs` | `Parameter_Weekend4DayPenalty` | class | 7 |
| `Templates/RosteringProblem/Parameter/Parameter_WeekendOffThreshold.cs` | `Parameter_WeekendOffThreshold` | class | 7 |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_SubtourMTZ.cs` | `Constraint_SubtourMTZ` | class | 10 |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_VisitOrderRange.cs` | `Constraint_VisitOrderRange` | class | 11 |
| `Templates/TSP_MultiDimSet/Objective/ObjectiveFunction.cs` | `ObjectiveFunction` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_CompleteDef.cs` | `Constraint_CompleteDef` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_AssignOneEqp.cs` | `Constraint_AssignOneEqp` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_MakespanDef.cs` | `Constraint_MakespanDef` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_MakespanInfeasibleCap.cs` | `Constraint_MakespanInfeasibleCap` | class | 11 |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_MakespanWindow.cs` | `Constraint_MakespanWindow` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_MakespanTargetSoft.cs` | `Constraint_MakespanTargetSoft` | class | 12 |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_RoutePrecedence.cs` | `Constraint_RoutePrecedence` | class | 7 |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_NoOverlap.cs` | `Constraint_NoOverlap` | class | 12 |
| `Templates/TSP_MultiDimSet/Set/Set_Node.cs` | `Set_Node` | class | 8 |
| `Templates/TSP_MultiDimSet/Set/Set_Depot.cs` | `Set_Depot` | class | 8 |
| `Templates/TSP_MultiDimSet/Set/Set_Customer.cs` | `Set_Customer` | class | 8 |
| `Templates/TSP_MultiDimSet/Set/Set_Arc.cs` | `Set_Arc` | class | 12 |
| `Templates/TSP_MultiDimSet/Data/Dataload.cs` | `Dataload` | class | 7 |
| `Templates/ModelTuner/Tuning/TuningRound.cs` | `TuningRound` | class | 13 |
| `Templates/ModelTuner/Tuning/TunerWorkspace.cs` | `TuningInstance` | record | 11 |
| `Templates/ModelTuner/Tuning/TunerWorkspace.cs` | `TunerWorkspace` | class | 18 |
| `Templates/ModelTuner/Tuning/TunerCli.cs` | `TunerCli` | class | 6 |
| `Templates/ModelTuner/Tuning/RoundFacts.cs` | `RoundFacts` | class | 14 |
| `Templates/ModelTuner/Tuning/CplexTuner.cs` | `CplexTuner` | class | 14 |
| `Templates/ModelInspector/InspectionOptions.cs` | `InspectionOptions` | class | 6 |
| `Templates/ModelInspector/Program.cs` | `Program` | class | 11 |
| `Templates/Sudoku_SHC279/Constraint/Constraint_RowDigit.cs` | `Constraint_RowDigit` | class | 7 |
| `Templates/Sudoku_SHC279/Constraint/Constraint_Given.cs` | `Constraint_Given` | class | 7 |
| `Templates/Sudoku_SHC279/Constraint/Constraint_ColumnDigit.cs` | `Constraint_ColumnDigit` | class | 7 |
| `Templates/Sudoku_SHC279/Constraint/Constraint_CellValue.cs` | `Constraint_CellValue` | class | 7 |
| `Templates/Sudoku_SHC279/Constraint/Constraint_BlockDigit.cs` | `Constraint_BlockDigit` | class | 7 |
| `Templates/Sudoku_SHC279/Program.cs` | `Program` | class | 7 |
| `Templates/Sudoku_SHC279/Data/Dataload.cs` | `Dataload` | class | 8 |
| `Templates/Sudoku_SHC279/Parameter/Parameter_ObjCoef.cs` | `Parameter_ObjCoef` | class | 8 |
| `Templates/Sudoku_SHC279/Parameter/Parameter_ExactlyOne.cs` | `Parameter_ExactlyOne` | class | 7 |
| `Templates/ModelInspector/Reporting/ReportWriter.cs` | `ReportWriter` | class | 8 |
| `Templates/ModelInspector/Reporting/ModelInspection.cs` | `ModelInspection` | class | 11 |
| `Templates/ModelInspector/Reporting/ModelInspection.cs` | `ArtifactFile` | record | 134 |
| `Templates/Sudoku_SHC279/Objective/ObjectiveFunction.cs` | `ObjectiveFunction` | class | 7 |
| `Templates/Sudoku_SHC279/Solution/Sudoku_SHC279Solution.cs` | `Sudoku_SHC279Solution` | class | 7 |
| `Templates/Sudoku_SHC279/Variable/VariableB_CellDigit.cs` | `VariableB_CellDigit` | class | 10 |
| `Templates/Sudoku_SHC279/Set/Set_Column.cs` | `Set_Column` | class | 8 |
| `Templates/Sudoku_SHC279/Set/Set_BlockCell.cs` | `Set_BlockCell` | class | 10 |
| `Templates/Sudoku_SHC279/Set/Set_Block.cs` | `Set_Block` | class | 8 |
| `Templates/Sudoku_SHC279/Set/Set_Digit.cs` | `Set_Digit` | class | 8 |
| `Templates/Sudoku_SHC279/Set/Set_Given.cs` | `Set_Given` | class | 10 |
| `Templates/Sudoku_SHC279/Set/Set_Row.cs` | `Set_Row` | class | 8 |

### Review Scope Notes

- 註解整理涵蓋 Core、Cplex、Generators、Templates、tests、HTML 範例及 project/config 註解。
- 保留數學式、模型符號、參數範圍、單位、XML tags 與行為限制。Generator 字串只改三處輸出註解，其餘字串與程式邏輯不變。
- packages 為外部套件，Generated、bin、obj 為產生檔；均不編輯。Markdown 規格與 README 正文不在本次措辭修改範圍。
- Symbol 行號已依本次註解整理結果同步。Build/test 指令見既有 Build 區塊。
