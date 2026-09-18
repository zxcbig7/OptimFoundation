---
title: 模型來源二態（自建 / 匯入）與統一模型統計
status: draft
created: 2026-09-18
updated: 2026-09-18
modules: [core, cplex, experiments, templates]
---

# 模型來源二態與統一模型統計

## Summary

讓 OptimFoundation 的模型有兩種來源——「框架自建」與「匯入既有模型檔（.lp / .mps / .sav）」——並讓兩者是兩個共用同一契約的物件，使 `OptProject` / `OptExperiment` 可以無腦換物件、呼叫端程式碼一行不改。

同時把「模型結構統計」收斂成單一實作：一律基於組裝完成的 model 物件取值，不再有自建一套、匯入一套、求解後再反推一套的分歧。框架限定的建模期對帳資訊（Expected vs Actual）與統計切開，並在型別上讓匯入模式取不到，而不是回 0。

## Motivation / Why

使用者有「手上只有一份模型檔」的情境：模型不是本框架建的，沒有 C# 變數類別、不遵守框架命名規範，但仍然要能丟進框架求解、跑實驗、看統計。

目前的障礙有三個：

1. **來源二態靠 null 分岔而非型別**。`OptModel` 是單一 sealed class，用 `_sourceFile != null` 決定行為（`src/OptimFoundation.Cplex/OptModel.cs:96-97`）。這讓「哪些能力只有自建模型成立」無法從型別看出來，只能靠文件。

2. **統計有三套來源，其中一套會說謊**。
   - `VariableCount` / `ConstraintCount`：兩條路徑共用 `Variables` dict 與 `_constraints`，這部分本來就是一套，沒問題。
   - `ObjectiveSense`：只在 `SetObjective` 路徑寫入 `_objectiveSense`（`EngineBase.cs:1097`）。`ReindexFromModel` 取了 `Model.GetObjective()` 卻沒讀 `.Sense` 也沒回寫（`OptEngine.cs:265`），因此匯入模式恆為框架預設的 `Minimize`。**這是全部成員裡唯一給錯誤值而非 0 值的**，最容易誤導。
   - 變數型別分布：目前由消費端從三支取解 API 的 key 數反推（`Templates/ModelInspector/Reporting/ReportWriter.cs:210-217`），等於第三套；且 infeasible / timeout 時量不到，而那正是最需要看模型結構的時候。

3. **框架限定資訊在匯入模式回 0 而不是「不適用」**。`RegisteredVariableCount`、`VariableBuildCounts`、`ConstraintBuildCounts`、`ObjectiveTermCount`、`SoftConstraintCount`、`SoftPenaltyTermCount` 這六者的唯一寫入點都是 `BatchBuild`（`EngineBase.cs:307-329`），已確認 100% 只在自建路徑寫入。匯入時它們恆為 0 / 空，目前僅靠 README 表格與 runtime caveat 字串擋，型別上仍讀得到那個假值。

第三點還有一個跨規範的後果：`Trial` 只記 `Model.Name`（`OptExperiment.cs:159`），`SourceFile` 不進任何 Trial / ConfigSnapshot 欄位，匯入來源在實驗輸出裡不可追溯——與 MILP domain 天條「baseline provenance MUST 永久可追溯」直接衝突。

## Scope

### In Scope

- **Cplex 層**：`OptModel` 拆成 abstract base + `BuiltModel` / `ImportedModel` 兩個具體型別，`OptProject` / `OptExperiment` 改吃 base 契約。
- **Core 層**：新增 solver-agnostic 的 `ModelProfile`（模型結構統計）與 `AuthoringReport`（建模期對帳），擷取點掛在 `ISolverEngine` / `EngineBase`。
- **Cplex 層**：實作 `CaptureProfile()`，統計一律向組裝完成的 model 物件取值。
- **修正 `ObjectiveSense`**：改由 solver 物件讀取，兩條路徑同一套。
- **Experiments**：`SolveMetrics` / `Trial` 補模型來源、來源檔、型別分布、模型載入耗時；CSV / JSON writer 跟進。
- **匯入覆蓋率警告**：`ReindexFromModel` 數出被跳過的非 LP-matrix 元素數，寫進 `ModelProfile`，不中斷流程。
- **Templates/ModelInspector 同步瘦身**：改用框架提供的 `ModelProfile`，caveat 清單由型別表達取代手寫字串。
- 對應的 unit / integration tests。
- 本規格檔本身進 `specs/`。

### Out of Scope

- **nnz、density、係數 / RHS 數量級範圍**：需要 `ILPMatrix` row-wise API 或 `IloCplex` 彙總屬性，兩者簽名在本 repo 內零使用、未驗證，本次不碰。
- **從變數名反推型別與維度**（名稱推斷層）：框架匯出的 `.sav` 保留 `VariableB_Xxx@dim` 格式，理論上可部分恢復自建限定的聚合統計，本次不做。
- **`OptExperiment` 每個 trial 重讀模型檔的效能問題**：行為不改（每 trial 獨立讀檔正好保證 trial 間隱態隔離，對實驗是優點），本次只讓耗時可見。
- **`CodeMap.md` 全面重寫**：僅在 References 點出其 stale 狀態。

## User Stories / Use Cases

1. As a 建模者, I want to 把別人給的 `.lp` 丟進框架直接求解, so that 不必先把模型翻譯成 C# 類別。
2. As a 建模者, I want to 在求解之前就知道這個模型有多少變數、多少限制式、其中幾個是 0/1, so that 我能判斷它的規模與難度，即使它最後 infeasible 或 timeout。
3. As a tuning 執行者, I want to 把匯入的模型丟進 `OptExperiment` 跑參數矩陣, so that 我能對非本框架的模型做 solver tuning，而且程式碼跟自建模型完全一樣。
4. As a tuning 執行者, I want to 從實驗輸出看出這個 Trial 的模型是哪來的（自建還是哪個檔）, so that champion 的 provenance 可追溯。
5. As a 框架使用者, I want to 在匯入模式下「根本拿不到」那些只有自建模型才成立的數字, so that 我不會把 0 當成真值讀。
6. As a 框架維護者, I want to 統計只有一份實作, so that 兩條路徑不會在同一個指標上給出不同答案。

## Acceptance Criteria

- [ ] **AC1 兩路徑統計逐欄相等**：`RosteringProblem` 自建求解取得的 `ModelProfile`，與其匯出 `.sav` 再匯入取得的 `ModelProfile`，結構欄位（變數數、型別分布、限制式數、sense 分布、bounds 分布、objective sense）逐欄相等。
- [ ] **AC2 統計早於求解且不依賴解**：`CaptureProfile()` 在 `Solve()` 之前呼叫即可取得完整結構統計；模型 infeasible 或 timeout 時，結構統計仍完整（不再由取解 key 數反推）。
- [ ] **AC3 `ObjectiveSense` 不再說謊**：匯入一個目標式為 maximize 的模型檔，`ModelProfile.ObjectiveSense` 回 `Maximize`；自建 maximize 模型亦回 `Maximize`。
- [ ] **AC4 框架限定資訊型別上取不到**：匯入模式下 `engine.Authoring` 為 `null`；`RegisteredVariableCount` / `VariableBuildCounts` / `ConstraintBuildCounts` / `ObjectiveTermCount` / `SoftConstraintCount` / `SoftPenaltyTermCount` 不再能從 engine 直接讀到 0，必須經 `Authoring` 且先過 null 檢查。
- [ ] **AC5 實驗端無腦換物件**：把 `OptExperiment` 的 `AddModel(OptModel.Build(...))` 換成 `AddModel(OptModel.FromFile(...))`，其餘呼叫端程式碼一行不改即可跑完整個矩陣，每個 Trial 的規模欄位都正確。
- [ ] **AC6 來源可追溯**：Trial 輸出（CSV 與 JSON）含模型來源種類與來源檔路徑；自建模型的來源檔欄位為空而非遺漏欄位。
- [ ] **AC7 覆蓋率警告**：匯入一個含非 LP-matrix 元素的模型檔時，`ModelProfile.UncoveredElementCount > 0` 且 `IsComplete == false`，流程不中斷；`ModelInspector` 報告會把這個警告印出來。
- [ ] **AC8 模型載入耗時可見**：匯入來源的 Trial，其 metrics 含 `ModelLoadMs`（讀檔 + reindex 耗時）且 > 0；自建模型該欄位為 0。
- [ ] **AC9 疊加行為保留**：`OptModel.FromFile(...).AddConstraints(...)` 仍然先匯入、再套用追加步驟（既有測試 `FromFile_ThenAddConstraints_AppliesBoth` 不需修改語意即通過）。
- [ ] **AC10 全綠**：`dotnet build OptimFoundation.sln` 與 `dotnet test` 通過，全部 Templates 可 build。

## Module Interactions

- **Core（`OptimFoundation.Core`）**
  - 新增 `ModelProfile.cs`：結構統計值物件 + `ModelSourceKind` enum。
  - 新增 `AuthoringReport.cs`：建模期對帳值物件。
  - `ISolverEngine.cs`：新增 `ModelProfile CaptureProfile()` 契約成員。
  - `EngineBase.cs`：`CaptureProfile()` 給 virtual 預設（throw `NotSupportedException`）；六個自建限定成員收進 `Authoring` 屬性；`_objectiveSense` 不再作為對外真相來源。
  - `Experiments/SolveMetrics.cs`、`Experiments/Trial.cs`、`Experiments/CsvExperimentWriter.cs`、`Experiments/JsonExperimentWriter.cs`：補欄位。

- **Cplex（`OptimFoundation.Cplex`）**
  - `OptModel.cs`：拆成 abstract `OptModel` + `BuiltModel` + `ImportedModel`。
  - `OptEngine.cs`：override `CaptureProfile()`；`ReindexFromModel` 補回寫 objective sense 與計數被跳過的元素；`ImportModel` 量測載入耗時。
  - `OptProject.cs`、`OptExperiment.cs`：改吃 `OptModel` base 契約；把 `ModelProfile` 與載入耗時帶進 Trial。

- **Templates**
  - `ModelInspector`：`ModelInspection.cs` 改持有 `ModelProfile` + `AuthoringReport?`，移除手寫 caveat 清單中已由型別表達的項目；`ReportWriter.cs` 的「模型結構」節改讀 profile。
  - `Tutorial` / `TSP_MultiDimSet` / `Sudoku_SHC279` / `RosteringProblem` / `FJSP_BASIC_BRICK`：`new OptModel(...)` → `OptModel.Build(...)`。

- **Tests**
  - `ModelImportIntegrationTests.cs`、`ExperimentIntegrationTests.cs`、`OptEngineIntegrationTests.cs`、`RunnerSymmetryTests.cs`、`Mocks/MockEngine.cs`：跟進新契約，並新增 AC1–AC8 對應測試。

## API Design

### 模型來源二態

```csharp
namespace OptimFoundation.Cplex
{
    /// <summary>模型定義的共用契約。OptProject / OptExperiment 只認這個型別。</summary>
    public abstract class OptModel
    {
        public string Name { get; }

        /// <summary>模型來源種類。</summary>
        public abstract ModelSourceKind Source { get; }

        /// <summary>匯入來源檔絕對路徑；自建模型恆為 null。</summary>
        public virtual string SourceFile => null;

        /// <summary>建立一個以程式逐步組裝的模型。</summary>
        public static BuiltModel Build(string name = "Model");

        /// <summary>以既有模型檔（.lp / .mps / .sav，含 .gz / .bz2）定義模型。</summary>
        public static ImportedModel FromFile(string fileName, string name = null);

        public OptModel AddVariables(Action<OptEngine> build);
        public OptModel AddObjective(Action<OptEngine> build);
        public OptModel AddConstraints(Action<OptEngine> build);

        /// <summary>模板方法：先套用來源，再依序執行已錄下的步驟。</summary>
        internal void ApplyTo(OptEngine engine);

        /// <summary>來源專屬的套用動作。自建為 no-op，匯入為讀檔 + re-index。</summary>
        protected abstract void ApplySource(OptEngine engine);
    }

    public sealed class BuiltModel : OptModel
    {
        public override ModelSourceKind Source => ModelSourceKind.Authored;
    }

    public sealed class ImportedModel : OptModel
    {
        public override ModelSourceKind Source => ModelSourceKind.Imported;
        public override string SourceFile { get; }
    }
}
```

`AddVariables` / `AddObjective` / `AddConstraints` 與步驟錄製留在 base，兩種來源共用——匯入模型仍可疊加步驟（AC9）。兩者唯一的差別是 `ApplySource`。

### 模型結構統計（通用層）

```csharp
namespace OptimFoundation.Core
{
    public enum ModelSourceKind { Authored, Imported }

    /// <summary>
    /// 組裝完成後的模型結構統計。兩種來源共用同一份實作，
    /// 一律向已組裝的 model 物件取值，不依賴建模期累積、也不依賴求解結果。
    /// </summary>
    public sealed class ModelProfile
    {
        public ModelSourceKind Source { get; }
        public string SourceFile { get; }

        public int VariableCount { get; }
        public int BinaryCount { get; }
        public int IntegerCount { get; }
        public int ContinuousCount { get; }

        public int FreeVariableCount { get; }
        public int FixedVariableCount { get; }
        public int BoundedVariableCount { get; }

        public int ConstraintCount { get; }
        public int LessEqualCount { get; }
        public int GreatEqualCount { get; }
        public int EqualCount { get; }
        public int RangeCount { get; }

        public ObjectiveSense ObjectiveSense { get; }

        /// <summary>re-index 時未能納入統計的模型元素數（非 LP-matrix 形式）。</summary>
        public int UncoveredElementCount { get; }

        /// <summary>統計是否完整涵蓋模型。false 代表本份統計偏低，不可當真值使用。</summary>
        public bool IsComplete => UncoveredElementCount == 0;
    }
}
```

### 建模期對帳（自建限定層）

```csharp
namespace OptimFoundation.Core
{
    /// <summary>
    /// 建模期的 Expected vs Actual 對帳與 pool 累積量。
    /// 只有 BuiltModel 路徑成立——匯入模型沒有經過 Build*Vs / Create* 這些入口。
    /// </summary>
    public sealed class AuthoringReport
    {
        public int RegisteredVariableCount { get; }
        public IReadOnlyDictionary<string, (int Expected, int Actual)> VariableBuildCounts { get; }
        public IReadOnlyDictionary<string, (int Expected, int Actual)> ConstraintBuildCounts { get; }
        public int ObjectiveTermCount { get; }
        public int SoftConstraintCount { get; }
        public int SoftPenaltyTermCount { get; }

        /// <summary>任一群組 Expected != Actual。</summary>
        public bool HasMismatch { get; }
    }
}
```

### Engine 契約

```csharp
// ISolverEngine.cs
ModelProfile CaptureProfile();

// EngineBase.cs
public virtual ModelProfile CaptureProfile()
    => throw new NotSupportedException("<engine> does not implement model profiling.");

/// <summary>建模期對帳報告；匯入模式為 null（那些數字在匯入路徑不成立）。</summary>
public AuthoringReport Authoring { get; }
```

`Authoring` 的 null 判定：`ReindexFromModel` 執行後設為 null，自建路徑維持非 null。這使 AC4「型別上取不到」成立——呼叫端必須先過 null 檢查。

### Experiments 欄位

```csharp
// SolveMetrics 新增
public ModelSourceKind ModelSource { get; set; }
public string ModelSourceFile { get; set; }
public int BinaryCount { get; set; }
public int IntegerCount { get; set; }
public int ContinuousCount { get; set; }
public long ModelLoadMs { get; set; }
```

`VarCount` / `ConstraintCount` 改由 `ModelProfile` 填入，不再直接讀框架 pool。`Trial` 與 CSV / JSON writer 新增對應欄位。

## Data Model

無資料庫異動。`ModelProfile` 與 `AuthoringReport` 皆為記憶體內值物件。

實驗輸出的 CSV 欄位新增（附加在既有欄位之後，不改既有欄位順序以免破壞既有分析腳本）：

```
ModelSource, ModelSourceFile, BinaryCount, IntegerCount, ContinuousCount, ModelLoadMs
```

## Edge Cases & Error Handling

- **模型含非 LP-matrix 元素**：`ReindexFromModel` 的 `if (enumerator.Current is not ILPMatrix matrix) continue`（`OptEngine.cs:270`）改為計數後 continue。計數寫進 `ModelProfile.UncoveredElementCount`，`IsComplete` 轉 false，並發一次 `Logging.Warn`。不中斷流程（使用者裁決：不報錯、貼警告）。**注意這是計數 enumerator 跳過的元素，不比對 solver 彙總屬性**——後者的成員名未驗證，本設計刻意避開。
- **`CaptureProfile()` 在 `Build()` 之前呼叫**：模型物件尚未存在 → 留 Error Log 後 throw `InvalidOperationException`，與既有 `ImportModel` 的前置檢查一致（`OptEngine.cs:213-251`）。
- **匯入模型無目標式**：`Model.GetObjective()` 回 null → `ObjectiveSense` 取框架預設並在 profile 旁發 Warn，不 throw（求解一個沒有目標式的可行性問題是合法情境）。
- **匯入的變數名為空或重複**：既有 `ResolveImportedName`（`OptEngine.cs:289-298`）已處理，本規格不改其行為；統計以去重後的 `Variables` 為準。
- **bounds 判定的無限大門檻**：以 solver 的無限大常數為準（CPLEX 慣例 1e20），不自訂門檻，避免與 `BuildVars` 預設上界 1E100 混淆。
- **`Authoring` 在自建但尚未建任何變數時**：回非 null 的空報告，而非 null——null 的語意保留給「這條路徑不適用」，不代表「還沒建」。
- **模型檔不存在 / 副檔名不支援**：既有行為保留（`FileNotFoundException` / `ArgumentException`），`ImportedModel` 的 ctor 不提前檢查檔案存在，維持「定義時不碰磁碟、套用時才讀」的既有語意。

## Non-Functional Requirements

- **Performance**：`CaptureProfile()` 對 `Variables` 與 `_constraints` 各做一次線性掃描，5,000 變數 / 10,000 限制式量級應在毫秒等級，不得引入 per-variable 的 solver interop 呼叫。
- **相容性**：實驗 CSV 既有欄位順序不變。
- **Observability**：匯入覆蓋不完整、無目標式、變數名重複三種情況各發一次 `Logging.Warn`；所有主動錯誤依 workspace CLAUDE.md 規定，在 throw 前留下含 event code、context、value、reason、`result=aborted` 的 Error Log。
- **文件同步**：public API 變更後須同步 `CodeMap.md`、Templates、`docs/`，並補上 workspace CLAUDE.md 所引用但目前不存在的 `specs/developer-guide.md`（見 Open Questions）。

## Breaking Changes

| 變更 | 影響面 | 遷移方式 |
| --- | --- | --- |
| `OptModel` 由 sealed class 改為 abstract base | `new OptModel(...)` 共 10 處（Templates 5 檔、tests 4 檔） | 改為 `OptModel.Build(...)` |
| 六個自建限定成員從 `EngineBase` public 面移入 `Authoring` | `ModelInspector`；tests | 改走 `engine.Authoring?.Xxx` |
| `SolveMetrics.VarCount` / `ConstraintCount` 改由 `ModelProfile` 填 | 無呼叫端語意變更 | 無 |

`OptModel.FromFile(...)` 簽名不變，回傳型別改為子型別 `ImportedModel`，既有呼叫端（皆使用 `var` 或 `OptModel` 宣告）不需修改。

## Open Questions

- [ ] `IObjective` 讀取 sense 的確切成員名（`Sense` 屬性 vs `GetSense()`）未經本 repo 驗證，stub 階段第一件事就是寫最小探針確認；若兩者皆不可得，退回方案為在 `ImportModel` 後以 `Model.GetObjective()` 的字串化結果判定，並把此退路記進規格。
- [ ] `specs/` 目錄目前是空的，但 workspace `CLAUDE.md` 與 `CodeMap.md` 都引用 `specs/developer-guide.md`、`specs/framework-dev-spec.md` 等七份檔案——這些檔案不存在，`CodeMap.md`（同步日期 2026-08-09）已 stale。本規格是 `specs/` 下第一份檔案。是否在本次一併重建 developer-guide，待確認。
- [ ] `ModelSourceKind` 的命名（`Authored` vs `Built`）：型別叫 `BuiltModel` 但 enum 值叫 `Authored`，是否統一成 `Built`。

## Implementation Plan

### Stub 階段（先做）

- [ ] 寫最小探針確認 `IObjective` 取 sense 的成員名（唯一的未驗證 API）
- [ ] Core：建 `ModelProfile.cs`、`AuthoringReport.cs`、`ModelSourceKind`，屬性齊全、值全為預設
- [ ] Core：`ISolverEngine` 加 `CaptureProfile()`；`EngineBase` 加 virtual 預設實作與 `Authoring` 屬性（body 為 `throw new NotImplementedException()` 或回 null）
- [ ] Cplex：`OptModel` 拆 abstract + `BuiltModel` + `ImportedModel`，`ApplySource` 留 TODO
- [ ] Cplex：`OptEngine.CaptureProfile()` override 簽名就位，body `throw new NotImplementedException()`
- [ ] 把 10 處 `new OptModel(...)` 改成 `OptModel.Build(...)`
- [ ] `dotnet build OptimFoundation.sln` 通過，確認型別與契約連得起來

### 逐層實作

- [ ] `ReindexFromModel`：回寫 objective sense、計數未涵蓋元素、`Authoring` 設 null
- [ ] `OptEngine.CaptureProfile()`：型別分布（`INumVar.Type`）、sense 分布與 bounds 分布（`IRange.LB/UB`、`INumVar.LB/UB`）
- [ ] `EngineBase`：六個自建限定成員收進 `AuthoringReport`，移除舊 public 成員
- [ ] `ImportModel`：量測讀檔 + re-index 耗時
- [ ] `SolveMetrics` / `Trial` / CSV / JSON writer 補欄位
- [ ] `OptProject` / `OptExperiment` 改吃 base 契約，帶入 profile 與載入耗時
- [ ] `Templates/ModelInspector` 改用 `ModelProfile`，移除已由型別表達的 caveat
- [ ] Tests：AC1–AC10 逐條對應測試
- [ ] 同步 `CodeMap.md`、Templates、`docs/`

## References

- 既有匯入路徑：`src/OptimFoundation.Cplex/OptModel.cs:47-104`、`src/OptimFoundation.Cplex/OptEngine.cs:213-298`
- 自建限定成員唯一寫入點：`src/OptimFoundation.Core/EngineBase.cs:307-329`（`BatchBuild`）
- 現行消費端示範：`Templates/ModelInspector/`（含匯入模式能力對照表，本規格將其型別化）
- 既有測試：`tests/OptimFoundation.Cplex.Tests/Integration/ModelImportIntegrationTests.cs`
- 受影響的既有規範：workspace `CLAUDE.md`（public API 變更須同步文件）、MILP domain 天條「baseline provenance MUST 永久可追溯」
- 已知 stale：`CodeMap.md`（同步日期 2026-08-09，引用七份不存在的 `specs/` 檔案）
