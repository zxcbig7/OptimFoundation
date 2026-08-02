---
title: OptimFoundation 拆成「專案 config」與「solver config」兩層，pipeline 各吃一次
status: draft
created: 2026-08-01
updated: 2026-08-01
modules: [framework-core, framework-cplex, template, projects-migration, docs]
---

# Dual Config — ProjectConfig / SolverConfig 分離

## Summary

在 `OptimFoundation.Core` 新增 `ProjectConfig`（專案身分、要留哪些檔），並把 `enableLog` / `exportLP` / `exportMPS` / `exportSol` / `LogToConsole` / `LogFilePath` **從 `CplexConfig` 移除**搬進去。`CplexConfig` 之後只剩**真正的 CPLEX solver 旋鈕**，`OptModel.UseConfig` 做成兩個 overload、pipeline 各吃一次。

這是 breaking change，但**本規格只負責框架 solution 內**：`OptimFoundation.sln` 底下 12 個呼叫點（Templates 9 + tests 3）當場編不過、必須同步改。`AI-Modeling` 端的 17 檔（Projects 16 + Template 1）**不在本規格範圍** —— 它們吃 `AI-Modeling/dlls/` 的 DLL 複製品，本規格**刻意不更新該複製品**，所以 8 個練習專案完全不受影響、照舊可跑。那 17 檔連同 `dlls/` 更新一起在 [`2026-08-01-optim-docs-and-projects-migration.md`](2026-08-01-optim-docs-and-projects-migration.md) 處理。

## Motivation / Why

1. **實驗追蹤資料被污染（本規格的主因）**：`ConfigSnapshot.From()` 用 reflection 掃 config 的所有 public field/property（`ConfigSnapshot.cs:54-64`），所以每一筆 `Trial` 的 `SolverSpecific` 都夾帶 `enableLog` / `exportLP` / `exportMPS` / `exportSol` / `LogToConsole` / `LogFilePath`。這些跟「solver 怎麼解」無關，卻進了 `Experiments/*.json`——那份 JSON 是要餵給人跟 LLM 做 tuning 分析的，混進檔案輸出開關等於在分析資料裡塞雜訊。**solver config 只留 solver 能調的東西，實驗分析才追得準。**
2. **語意錯置**：`exportLP` 是「這個專案要不要留 .lp 檔」，不是解法參數。`LogFilePath` 更誇張——CPLEX 側完全沒用到（`CplexConfig.cs:166-167` 自承），純粹因為 `ISolverConfig` 要求才存在。
3. **專案設定沒有單一入口**：專案名在 ctor、log 檔名在 `Logging` static、保留天數在 ctor 第二參數——換一個專案要記得改三個地方。
4. **experiment 模式特別痛**：同一專案掃 7 組 solver 設定，專案身分是同一份、只有 solver 設定該變。現在兩者黏在一起，只能整包重建 config。

## Scope

### In Scope

- `OptimFoundation.Core` 新增 `Config/ProjectConfig.cs`
- **從 `CplexConfig` 移除** 6 個非 solver 成員：`enableLog`、`exportLP`、`exportMPS`、`exportSol`、`LogToConsole`、`LogFilePath`
- **從 `ISolverConfig` 移除** `LogToConsole`、`LogFilePath` 兩個介面成員
- `OptEngine`（Cplex）新增 ctor overload 收 `ProjectConfig`，改由它取得四個輸出開關
- `OptModel` 新增 `UseConfig(Func<ProjectConfig>)` overload，與既有 `UseConfig(Func<CplexConfig>)` 並存
- `OptModel` 把「設 log 檔名 / 清舊檔」從 ctor 延後到 `Execute()`，讓 fluent 註冊的 `ProjectConfig` 來得及生效
- **遷移框架 solution 內的 12 個呼叫點**：`OptimFoundation/Templates/*`（9 檔）+ `tests/OptimFoundation.Cplex.Tests/Integration/*`（3 檔）—— 這些與 `src/` 同一個 solution，刪欄位當下就編不過，非改不可

### Out of Scope

- **`AI-Modeling` 端的一切**：`Projects/*` 16 檔、`Template/Program.cs` 1 檔、`dlls/` 更新、`VERSION.txt` 解 pin —— 全部歸 [`2026-08-01-optim-docs-and-projects-migration.md`](2026-08-01-optim-docs-and-projects-migration.md)。**本規格 MUST NOT 覆蓋 `AI-Modeling/dlls/`** —— Why: 覆蓋當下 8 個練習專案全部編不過，而它們要到最後一個 phase 才遷移，中間會有一長段不可用期間
- **文件更新**：`$FW/MILP Model/*`、`$OPT/AI-Modeling/CPLEX_API_REFERENCE.md`、`tuning/CLAUDE.md` 等約 40 份 —— 歸同一份遷移規格
- **檔案位置設定（輸出根目錄 / 七個子資料夾名 / 輸入 `Data/` 路徑）**：本輪完全不做。`FolderDir` 與 `Logging` 一行都不動，路徑維持今天的 `AppDomain.BaseDirectory` 固定配置。Why: 要動 `FolderDir` 的 static 欄位與 `Logging` 的 static 路徑快照，experiment 平行 trial 共用 static 有污染風險，而目前沒有實際需求
- **`OptimFoundation.Gurobi` 的功能開發**：Gurobi 不在開發 scope。本規格對它**只做讓 solution 編得過的機械修正**——`ISolverConfig` 拿掉兩個成員後，把 `LogToConsole` / `LogFilePath` 降級為 `GurobiConfig` 自有屬性，`Gurobi/OptEngine.cs` 照舊讀它們。**不**讓 Gurobi 支援 `ProjectConfig`、**不**動 Gurobi 的輸出行為
- `rowRead` / `workMemory` / `nodeFileInd` **留在 `CplexConfig`**：它們是實打實的 CPLEX `Param.*`（`Read.Constraints` / `WorkMem` / `MIP.Strategy.File`），屬 solver 旋鈕
- `ProjectConfig` 的檔案序列化（從 appsettings.json / CSV 讀設定）
- `HISTORY/` 底下的舊 code：不遷移

## User Stories / Use Cases

1. As a 跑 tuning 的人, I want to `Experiments/*.json` 的 `solverSpecific` 只有 solver 旋鈕, so that 我（或 LLM）做收斂分析時不會被 `exportLP` 這種欄位干擾。
2. As a 建模者, I want to 在 `Program.cs` 一眼看到「專案設定」與「solver 設定」是兩塊, so that 我調 gap / threads 時不會不小心動到檔案輸出。
3. As a 跑實驗的人, I want to 掃描時只換 solver config、專案 config 原封不動, so that 7 個 trial 的輸出規則保證一致。
4. As a 既有專案維護者, I want to 升級 DLL 後**編譯直接失敗**而不是安靜地改變行為, so that 我知道哪幾行要改、不會半年後才發現 .lp 沒在產。

## Acceptance Criteria

- [ ] AC1：`Program.cs` 可寫成 `new OptModel(name).UseConfig(() => projectConfig).UseConfig(() => solverConfig)`，兩個 overload 順序可互換
- [ ] AC2：`CplexConfig` 不再含 `enableLog` / `exportLP` / `exportMPS` / `exportSol` / `LogToConsole` / `LogFilePath`；其餘欄位（含 `rowRead` / `workMemory` / `nodeFileInd` 與 20 餘個 tuning 旋鈕）一個不少
- [ ] AC3：`ConfigSnapshot.From(cplexConfig)` 產出的 `SolverSpecific` **不再出現**上述 6 個 key；`Experiments/*.json` 同步變乾淨
- [ ] AC4：`ProjectConfig` 每個屬性可單獨設定，沒設到的各自套預設值；預設值等同今天的行為（`EnableSolverLog = true`、三個 `Export* = false`）
- [ ] AC5：兩個 `UseConfig` 都不呼叫時，框架範本 `Templates/Sudoku_SHC279` 與 `Templates/FJSP_BASIC_BRICK` 的 `dotnet run` 輸出與本規格實作前**逐字相同**（Status、目標值、log 檔名規則、LP/MPS/Sol 是否產出）
- [ ] AC6：`ProjectConfig.ProjectName` 與 ctor 參數同時存在時 **ctor 參數優先**（保住 `new OptModel(Dataload.PuzzleName)` 寫法）；`RetentionDays` 同規則
- [ ] AC7：框架 solution 內 12 個呼叫點遷移後 `dotnet build OptimFoundation.sln` 0 error；**遷移前**它們應如預期出現 `CS0117`（fail-loud，不得有任何呼叫點安靜地改變行為）
- [ ] AC8：OptimFoundation 既有 xUnit 全數通過（現況 69 個），並新增涵蓋「設定解析順序」與「snapshot 不含輸出開關」的測試
- [ ] AC9：`AI-Modeling/dlls/` **未被更動**（六個 DLL 的檔案時間戳與本規格開工前一致）；`AI-Modeling/Projects/*` 8 個專案 build + run 結果與 `AI-Modeling/baseline/BASELINE.md` 完全一致——證明本規格對 AI-Modeling 端零影響
- [ ] AC10：`OptimFoundation.Gurobi` 編譯通過且行為不變（只做機械修正）

## Module Interactions

- **OptimFoundation.Core**
  - 新增 `Config/ProjectConfig.cs`
  - `ISolverEngine.cs`：`ISolverConfig` 移除 `LogToConsole`、`LogFilePath`
  - `Experiments/ConfigSnapshot.cs`：**不改 code**，欄位移走後自然變乾淨
  - `Infrastructure/FolderDir.cs`、`Logging/Logging.cs`：**不動**
- **OptimFoundation.Cplex**
  - `CplexConfig.cs`：刪 6 個成員
  - `OptEngine.cs`：新增 `OptEngine(CplexConfig, ProjectConfig)` ctor；**只有 4 行**改讀 `ProjectConfig`——143（`enableLog`）、230（`exportLP`）、238（`exportMPS`）、246（`exportSol`），順手清掉冗餘的 `== true`。私有欄位 `_enableLog` / `_exportLp` / `_exportMps` / `_exportSol`（23–26 行宣告）與其下游消費端（223、253、767 行）**維持原樣**，只是來源換人
  - `OptModel.cs`：`UseConfig` 兩 overload、ctor 副作用延後、`Execute()` 內解析設定並把 `ProjectConfig` 交給 `OptEngine`
- **OptimFoundation.Gurobi**（僅機械修正）
  - `GurobiConfig.cs`：`LogToConsole` / `LogFilePath` 由介面實作降級為自有屬性
- **遷移對象（框架 solution 內 12 個 `.cs`，全部相對於 `$OPT/OptimFoundation/OptimFoundation/`）**
  - `Templates/FJSP_BASIC_BRICK/Program.cs`
  - `Templates/Sudoku_SHC279/Program.cs`
  - `Templates/Template_CPLEX/Program.cs`
  - `Templates/Template_CPLEX/CrossExperiment.cs`
  - `Templates/Template_CPLEX/ExperimentDemo.cs`
  - `Templates/Template_CPLEX/RosteringProblem.cs`
  - `Templates/Template_CPLEX/RosteringProblemOptModel.cs`
  - `Templates/Tutorial/Program.cs`
  - `Templates/Tutorial/ExperimentRunner.cs`
  - `tests/OptimFoundation.Cplex.Tests/Integration/ExperimentIntegrationTests.cs`
  - `tests/OptimFoundation.Cplex.Tests/Integration/OptEngineIntegrationTests.cs`
  - `tests/OptimFoundation.Cplex.Tests/Integration/SolverParamCoverageTests.cs`
- **NOT 本規格**：`AI-Modeling/**`（含 `dlls/`、`Projects/*`、`Template/`、所有 `.md`）→ 見遷移規格

## API Design

### `ProjectConfig`（OptimFoundation.Core）

```csharp
namespace OptimFoundation.Core
{
    /// <summary>
    /// 專案層設定：這個專案叫什麼、要留哪些檔。
    /// 與「solver 怎麼解」無關——跑 tuning 掃描時本物件不該跟著變，
    /// 也因此它不會進 ConfigSnapshot，不污染實驗分析資料。
    /// 每個屬性的預設值 = 今天不設定時的行為。
    /// 檔案「放哪」不在本型別職責內：路徑仍由 FolderDir 固定配置（見規格 Out of Scope）。
    /// </summary>
    public sealed class ProjectConfig
    {
        /// <summary>專案名（log 檔與 LP/MPS/Sol/IIS 檔名前綴）。null = 沿用 OptModel ctor 的 projectName。</summary>
        public string ProjectName { get; set; }

        /// <summary>輸出檔保留天數，超過就在 Execute() 開頭清掉。null = 30；&lt;= 0 關閉清理。</summary>
        public int? RetentionDays { get; set; }

        /// <summary>solver 求解過程的 log 是否即時印到 Console。false 仍完整寫進框架 log 檔，只是不洗畫面。</summary>
        public bool EnableSolverLog { get; set; } = true;

        /// <summary>求解前把模型匯出成 .lp（人可讀，對照 Model.md 驗證用）→ Models/。</summary>
        public bool ExportLP { get; set; } = false;

        /// <summary>求解前把模型匯出成 .mps（標準交換格式）→ Models/。</summary>
        public bool ExportMPS { get; set; } = false;

        /// <summary>求解成功後把解匯出成 .sol → Sols/。</summary>
        public bool ExportSol { get; set; } = false;

        /// <summary>寫解時的 DATA_ID 欄位預設值。null = 呼叫端自己傳。</summary>
        public string DataId { get; set; }

        /// <summary>寫解時的 USER 欄位預設值。null = 呼叫端自己傳。</summary>
        public string UserId { get; set; }
    }
}
```

> - `EnableSolverLog` 預設 **true**，對齊 `CplexConfig.enableLog = true` 的**原始碼**現況（`CPLEX_API_REFERENCE.md` §4 寫 `false` 是過時的，本規格一併修正該文件）
> - 屬性用 PascalCase property，非 `CplexConfig` 的 camelCase public field。Why: camelCase field 是既有型別的歷史包袱，新型別不繼承

### `CplexConfig` 移除清單

| 移除成員 | 去處 | 理由 |
| --- | --- | --- |
| `bool enableLog` | `ProjectConfig.EnableSolverLog` | 輸出行為，非 solver 旋鈕 |
| `bool exportLP` | `ProjectConfig.ExportLP` | 同上 |
| `bool exportMPS` | `ProjectConfig.ExportMPS` | 同上 |
| `bool exportSol` | `ProjectConfig.ExportSol` | 同上 |
| `bool LogToConsole` | `ProjectConfig.EnableSolverLog` | 原本只是 `enableLog` 的 adapter 別名 |
| `string LogFilePath` | 直接刪 | CPLEX 側從未使用（`CplexConfig.cs:166` 自承），純 snapshot 污染 |

**保留**：其餘全部，含 `workThreads` / `rowRead` / `workMemory` / `epGap` / `nodeSelect` / `randomSeed` / `epOpt` / `epRHS` / `timeLimit` / `polishAfterTime` / `mipEmphasis` / `varSel` / `algorithm` / `nodeFileInd` / `parallelMode` / `detTimeLimit` / `clockType` / `numericalEmphasis` / `epInt` / `epAGap` / `nodeLimit` / `treeMemoryLimit` / `intSolLimit` / `probe` / `rinsHeur` / `mipSearch` / `diveType` / `branchDir` / `cutsFactor` / `cutPasses` / 五族 cuts / `simplexIterLimit` / `barrierAlgorithm`，以及 `ISolverConfig` / `ITunableConfig` 的其餘 adapter。

### `ISolverConfig` 變更

```csharp
public interface ISolverConfig
{
    double? TimeLimit { get; set; }
    double? MipGap { get; set; }
    int? Threads { get; set; }
    // 移除：bool LogToConsole { get; set; }
    // 移除：string LogFilePath { get; set; }
    int ScaleWarnThreshold => 10_000_000;
}
```

### `OptEngine` / `OptModel`

```csharp
// OptEngine（Cplex）
public OptEngine(CplexConfig config);                          // 既有；ProjectConfig 用預設值
public OptEngine(CplexConfig config, ProjectConfig project);   // 新增

// OptModel
public OptModel UseConfig(Func<ProjectConfig> configFactory);  // 新增
public OptModel UseConfig(Func<CplexConfig> configFactory);    // 既有，簽名不變
```

呼叫端：

```csharp
using var model = new OptModel(Dataload.PuzzleName)
    .UseConfig(() => projectConfig)
    .UseConfig(() => solverConfig)
    .AddVariables(engine => CreateVariables(dataload, engine))
    .AddModel(engine => BuildModel(dataload, engine))
    .OnSolved(engine => dataload.WriteToCSV(engine));
```

兩個 overload 靠參數型別區分，**順序可互換**；同型別呼叫兩次以最後一次為準（與今天語意一致）。

### 設定解析順序（`Execute()` 內）

| 設定 | 解析順序（先者勝） | 都沒設的預設 |
| --- | --- | --- |
| 專案名 | ctor `projectName` → `ProjectConfig.ProjectName` | `"Model"` |
| 保留天數 | ctor `retentionDays` → `ProjectConfig.RetentionDays` | `30` |
| solver log / 三個匯出開關 | `ProjectConfig`（唯一來源） | `true` / `false` / `false` / `false` |

> ctor 參數優先於 `ProjectConfig` 是為了 AC6：既有 `new OptModel("X")` 不受影響。ctor 用預設值（`"Model"` / `30`）時才輪到 `ProjectConfig`。

### `Execute()` 流程變更

```text
今天：ctor → SetLogFileName(name) → PurgeOutputs(days)
      Execute() → new OptEngine(config) → SetModelName → Build → …

改後：ctor → 只記下 projectName / retentionDays（不做副作用）
      Execute() → 解析 ProjectConfig
                → Logging.SetLogFileName(解析後的專案名)
                → PurgeOutputs(解析後的天數)
                → log 一行「有效設定」
                → new OptEngine(solverConfig, projectConfig) → SetModelName → Build → …
```

## Data Model

無 DB，路徑配置不變（仍固定掛在執行檔目錄下）：

```text
bin/Debug/net8.0/
├── Data/                        輸入（不受保留期清理）
├── Solution/  Logs/  Models/  Sols/  IISs/  Experiments/
```

唯一的持久化影響：`Experiments/<name>.json` 的 `solverSpecific` 少掉 6 個 key（AC3）。**舊實驗檔不回溯轉換**——新舊格式並存，比較時以有無這些 key 判斷世代。

## Migration

移除欄位會讓每個呼叫點噴 `CS0117: 'CplexConfig' 未包含 'enableLog' 的定義`。**這是刻意的 fail-loud**（AC7 / Story 4）：安靜地保留舊欄位會讓人以為還有效，實際上 .lp 早就不產了。

機械轉換規則：

```csharp
// Before
var config = new CplexConfig { epGap = 0.03, timeLimit = 300, workThreads = 8,
                               enableLog = true, exportSol = true, exportLP = true, exportMPS = true };
using var m = new OptModel("X").UseConfig(() => config) …

// After
var solverConfig = new CplexConfig { epGap = 0.03, timeLimit = 300, workThreads = 8 };
var projectConfig = new ProjectConfig { EnableSolverLog = true, ExportSol = true, ExportLP = true, ExportMPS = true };
using var m = new OptModel("X").UseConfig(() => projectConfig).UseConfig(() => solverConfig) …
```

本規格範圍內是 **12 個檔案**同型修改（框架 solution 內）。`AI-Modeling` 的 17 檔用同一個 pattern，但在遷移規格處理，且會與 runner 對稱化合併成同一刀，避免同批檔案改兩次。

## Edge Cases & Error Handling

- **`enableLog` 預設是 `true` 不是 `false`** —— 遷移時若照 `CPLEX_API_REFERENCE.md` §4 的錯誤敘述設成 `false`，所有專案會安靜地失去 Console solver log。遷移 MUST 以原始碼為準
- **`UseConfig` 同型別呼叫兩次** → 後者覆蓋前者，不丟例外
- **兩個 overload 都不呼叫** → 兩邊都用預設，等同今天
- **ctor 到 `Execute()` 之間寫的 log** → 落在預設檔名的 log 檔（`SetLogFileName` 延後了）。框架自身此期間不寫 log；呼叫端若在此期間 `Logging.Info` 會進舊檔，於 `<summary>` 註明
- **experiment 模式不經 `OptModel`**（直接 `new OptEngine`）→ 必須改用 `new OptEngine(solverConfig, projectConfig)`，否則四個輸出開關會全部吃預設值（solver log 會突然變成開著、洗掉掃描輸出）
- **Gurobi** → `ISolverConfig` 少兩個成員後 `GurobiConfig` 必須自己宣告，否則 `Gurobi/OptEngine.cs:50,53` 編不過

## Non-Functional Requirements

- **Fail-loud 優先於相容**：本規格刻意選擇編譯失敗而非靜默降級（見 Migration）
- **Observability**：`Execute()` 開頭 log **一行**解析後的有效設定，全部值都印、**非預設值標 `*`**，並在專案名後標來源（`(ctor)` / `(cfg)` / `(default)`）：

  ```text
  [EffectiveConfig] ProjectName=Sudoku(ctor) RetentionDays=30 SolverLog=ON ExportLP=ON* ExportMPS=OFF ExportSol=ON*
  ```

  Why 全印而非只印非預設：全預設時整行會消失，分不出「沒設定」與「log 壞了」；且事後重現實驗需要完整值。`*` 負責讓異動一眼可見。
  Why solver 旋鈕不比照辦理：`OptEngine` 既有慣例已是「有設才印」（`randomSeed` 218 行、LP/MPS/Sol 233/241/249 行），維持原樣不重複
- **框架唯讀天條的例外**：本規格**刻意**改框架本體，需 rebuild Core + Cplex 並更新 `$OPT/AI-Modeling/dlls/` 與 `VERSION.txt`
- **Performance**：無影響（設定解析 O(1)，只在 `Execute()` 跑一次）

## 後續 spec（runner 對稱化）已定案的設計決定

本規格只做 config 分層。以下是討論中**已拍板**、由後續 spec 實作的架構決定，先記在此避免遺失：

| # | 決定 | 內容 |
| --- | --- | --- |
| D1 | 三個物件重新分工 | `OptModel` = **模型定義**（變數 / 目標式 / 限制式，跟在哪跑無關）；`OptProject` = 專案 runner（長時間運作，重紀錄與 housekeeping）；`OptExperiment` = 實驗 runner（n 設定 × m 模型，只著重 local 比較） |
| D2 | 今天的 `OptModel` 改名 | 今天的 `OptModel`（runner）→ `OptProject`；`OptModel` 這個名字讓給模型定義物件 |
| D3 | 模型組裝拆三個方法 | `.AddVariables()` + `.AddObjective()` + `.AddConstraints()`，**NEVER 合併成一個 `AddModel`** —— Why: 打包是有能力的開發者自己想得到的作法，框架不該預設那條路；且拆開後「目標式先於限制式」由框架階段順序保證，不再是靠註解與人的紀律（`Constraint_Soft` 的 penalty 掛空問題結構性消失） |
| D4 | `OnSolved` 掛 runner 不掛模型 | `OptProject.OnSolved(...)`；`OptExperiment` 沒有這個方法 —— Why: 「解出來之後做什麼」屬執行環境，不屬模型定義；也讓 n×m 次寫解檔的爆量問題自動不存在 |
| D5 | `ProjectConfig` 維持原名 | 不改叫 `RunConfig`。`OptExperiment` 內所有 model 共用同一份 `ProjectConfig` |
| D6 | 兩個 runner 的預設方向相反 | `OptProject`：solver log ON、匯出照設定、做 housekeeping。`OptExperiment`：log OFF、不匯出、不做 housekeeping —— Why: 靠 runner 自帶合理預設，不靠人記得傳 |
| D7 | 模型名 vs 專案名分家 | `new OptModel("Model1")` 是**模型名**（實驗中的 m 識別，Trial label 寫成 `Model1 \| baseline`）；專案名在 `ProjectConfig.ProjectName`（log / LP / MPS / Sol / IIS 檔名前綴）。遷移既有 `new OptModel("GlassFactory")` 時，那個字串是**專案名**，要搬去 runner 側 |
| D8 | `ExperimentRunner.cs` / `RunExperiment()` 消失 | 專案端不再手寫實驗迴圈；`Program.cs` 只剩「定義 model → 選一個 runner」。既有手寫雛形見 `$OPT/OptimFoundation/OptimFoundation/Templates/Template_CPLEX/CrossExperiment.cs`（n×m 的 label 慣例 `"Model1 \| baseline"` 沿用） |
| D9 | dataload 生命週期：**共用一份** | n×m 的所有 cell 共用同一個 `OptData.Load` 結果；框架在 `DataContext` 建構完成後凍結，建模階段若有人寫入就丟例外 —— Why: 每 cell 重讀重驗在 n×m 大時太慢；「安全」改由框架驗證而非註解承諾。**註**：此決定使 `Template/RunExperiment`（每 trial fresh）與 `CrossExperiment`（共用）的現行矛盾收斂到後者 |
| D10 | 材料一律宣告在外，runner 只引用 | `Program.cs` 分三段平坦排列：**① 材料**（`dataload` + `ProjectConfig` + n 個 `CplexConfig`，都在外面宣告一次）→ **② 模型**（`OptModel` 引用 `dataload`）→ **③ 環境**（`OptProject` / `OptExperiment` 引用前兩者）。同一個 config 物件可先在 `OptProject` 跑、再進 `OptExperiment` 當基準組 |
| D10a | solver 設定是**具體物件**，非調參 delegate | `AddConfig("baseline", baseline)` 收 `CplexConfig` 實例，NEVER 用今天 `Action<CplexConfig> tune` 的突變寫法 —— Why: 每組設定所見即所得，不必在腦中套用 delegate；`ConfigSnapshot` 抓到的正是眼睛看到的那個物件。為免重複打字，框架提供 `CplexConfig.Clone()` |
| D10b | 組別名稱由 `AddConfig` 傳入，NEVER 加進 config 型別 | 不在 `CplexConfig` 加 `Label` 欄位 —— Why: `ConfigSnapshot` 用 reflection 掃全部 public 成員，加了 `Label` 等於又往 `SolverSpecific` 塞非 solver 資料，直接違反本規格主旨 |
| D10c | n×m 預設全笛卡兒 + 單格逃生口 | `AddModel` × `AddConfig` 自動展開所有組合，Trial label 沿用 `CrossExperiment` 慣例 `"Model1 \| baseline"`；另提供 `.AddTrial(model, config)` 明寫單一 cell，供只想跑部分組合的情況 |
| D11 | 支援**迭代式 tuning**：贏家升格成新 baseline | 標準玩法是 `var v = baseline.Clone(); v.Emphasis = 2;` 跑一輪 → 哪組贏就把它當新 baseline → 再往上疊新組。框架要件見 D11a–D11c |
| D11a | `Clone()` MUST 用 `MemberwiseClone()` | `CplexConfig.Clone()` / `ProjectConfig.Clone()` 一律 `(T)MemberwiseClone()`，**NEVER 手寫逐欄複製** —— Why: `CplexConfig` 有 40+ 欄位，手寫版在未來任何人新增旋鈕時會靜默漏掉，迭代數輪後才發現設定沒帶下去且無任何錯誤訊息。兩型別現況全是 value type + string，shallow copy 即完整 copy。MUST 配一個反射測試：比對 clone 與原件的每個 public 成員相等（連「未來有人加了 reference type 欄位」也擋得下來） |
| D11b | `ProjectConfig` 也提供 `Clone()` | 常見用法 `var expConfig = projectConfig.Clone(); expConfig.EnableSolverLog = false;`（掃描時閉嘴），與 D6 的 runner 預設方向搭配 |
| D11c | 迭代輪次 MUST 反映在 label 或實驗名 | `Experiment.Save()` 是 append 跨 run 累積、去重鍵為 `RunAt + Label`。第 3 輪的 `"baseline"` 已是第 2 輪贏家、設定與第 1 輪的 `"baseline"` 完全不同，CSV 裡卻同名——只能靠時間戳分辨。資料本身沒丟（每個 trial 的 `ConfigSnapshot` 完整），糊掉的只有 label。規範層要求 `AddConfig("r3-baseline", …)` 或 `new OptExperiment("rostering-tuning-r3", …)`；框架不強制 |

## Open Questions

- [ ] Q1：`DataId` / `UserId` 進 `ProjectConfig` 後，`CsvCtrl.WriteSolution<T>(engine, dataId, userId)` 要不要出 overload 讓它自己去拿？（本輪只放欄位，不改 `CsvCtrl`）
- [ ] Q2：既有 `Experiments/*.json` 要不要寫個一次性 script 清掉舊 key？（暫定不做，新舊並存）
- [ ] Q3：spec 檔放 `$FW/specs/`（本檔位置，與既有 optim 治理規格同處）還是 `$OPT/OptimFoundation/OptimFoundation/specs/`（code 實際落點）？
- [ ] Q4：**打包方式**——本規格與 runner 對稱化拆兩份（消費端只在第二份改一次），還是合成一份大 spec？影響本規格的遷移範圍：拆兩份的話 `AI-Modeling/Projects/*` 16 檔留到第二份再動
- [x] ~~Q5：`Experiments/` 是否該從 `FolderDir._outputs` 移除？~~ → **決議：不修，維持現狀**（2026-08-01）。`Experiments/` 與其他輸出共用同一個 30 天保留期，概念一致。且 purge 判準是 `LastWriteTime`、`Experiment.Save()` 每次整份重寫 JSON——只要還在跑該實驗時間戳就持續刷新，30 天的鐘要等**完全停手**才開始走。停手超過 30 天的實驗歷史消失屬可接受風險，備份是使用者自己的責任

## Implementation Plan

### Stub 階段（先做）

- [ ] S1：`OptimFoundation.Core/Config/ProjectConfig.cs` — 完整屬性 + XML doc，無邏輯
- [ ] S2：`OptModel` 加 `UseConfig(Func<ProjectConfig>)` overload + 私有欄位，`Execute()` 留 `// TODO: 解析（規格 設定解析順序表）`
- [ ] S3：`OptEngine` 加 `(CplexConfig, ProjectConfig)` ctor，body 先沿用既有邏輯
- [ ] S4：`dotnet build` 通過（此時行為未變、欄位還沒刪）

### 逐層實作

- [ ] I1：`OptModel.Execute()` 實作解析順序表 + 有效設定 log 一行
- [ ] I2：ctor 的 `SetLogFileName` / `PurgeOutputs` 副作用搬到 `Execute()`
- [ ] I3：`OptEngine` 143/230/238/246 四行改讀 `ProjectConfig`（下游 223/253/767 不動）
- [ ] I4：`CplexConfig` 刪 6 個成員；`ISolverConfig` 刪 2 個成員；`GurobiConfig` 補回自有屬性（機械修正）
- [ ] I5：遷移框架 solution 內 12 個呼叫點（清單見 Module Interactions）→ `dotnet build OptimFoundation.sln` 0 error
- [ ] I6：跑 xUnit（AC8），新增「解析順序」與「snapshot 不含輸出開關」測試
- [ ] I7：`Templates/Sudoku_SHC279` 與 `Templates/FJSP_BASIC_BRICK` 實跑，驗 AC5
- [ ] I8：確認 `AI-Modeling/dlls/` 六個 DLL 時間戳未變、8 個練習專案跑出的結果與 `baseline/BASELINE.md` 一致（驗 AC9）

## References

- `OptModel`：`$OPT/.../src/OptimFoundation.Cplex/OptModel.cs`（ctor 副作用 69–78、`UseConfig` 83、`Execute` 117）
- `CplexConfig`：`$OPT/.../src/OptimFoundation.Cplex/CplexConfig.cs`（`enableLog = true` 在 18 行、`LogFilePath` 未使用自承在 166–167）
- `ISolverConfig`：`$OPT/.../src/OptimFoundation.Core/ISolverEngine.cs:8-27`
- `ConfigSnapshot`：`$OPT/.../src/OptimFoundation.Core/Experiments/ConfigSnapshot.cs:52-64`（reflection 掃全部 public 成員）
- `OptEngine` 四個輸出開關消費點：`$OPT/.../src/OptimFoundation.Cplex/OptEngine.cs:143,230,238,246`
- 受影響的既有規格：`$FW/specs/2026-07-14-ai-modeling-governance-wave2.md`（D13「組裝與求解」列需更新成雙 config）
- 本輪之前的相關改動：Program.cs 唯一組裝點、Constraint/Objective 與 Dataload 解耦（見 `$FW/MILP Model/optimfoundation-api-guide.md`）
