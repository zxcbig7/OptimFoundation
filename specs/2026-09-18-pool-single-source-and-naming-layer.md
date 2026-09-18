---
title: 變數池與限制池以 cplex.model 為唯一來源，命名層獨立
status: draft
created: 2026-09-18
updated: 2026-09-18
modules: [core, cplex, generators, tests, templates]
---

# 池的唯一來源與命名層分離

## Summary

把「模型裡有哪些變數、有哪些限制式」這件事的唯一真相收斂到建完模的 `cplex.model`。框架不再維護平行的 registry：`VariableSets` 直接移除，`Variables` 與限制式名稱集合改成對 `cplex.model` 的投影。型別化查詢改為「命名層產生字串集合 → 拿去篩選 `Variables`」，不再預先分組。

同時把「變數 key 的產生與解析」抽成獨立的命名層：只處理字串集合，完全不認得任何 solver 型別，可以在不建立 engine 的情況下單獨測試。兩層之間以 string mapping 銜接。

分兩階段交付：Phase 1 抽接縫、行為不變、消費端零改動；Phase 2 把內部身分從 string 換成 int index，接縫與消費端 API 皆不動。

## Motivation / Why

**三份真相**。建立變數時 `Model.NumVar(lb, ub, cplexType, name)` 已經把名字交給 CPLEX（`OptEngine.cs:313`），CPLEX 自己就存著「有哪些變數、各叫什麼」。但框架同時維護 `protected readonly Dictionary<string, TVar> Variables`（`EngineBase.cs:180`）與再分一層的 `Dictionary<string, Dictionary<string, TVar>> VariableSets`（`EngineBase.cs:183`），限制式那側則有 `HashSet<string> _verifyConstraints`（`EngineBase.cs:275`）與 `List<IRange> _constraints`（`OptEngine.cs:32`）。同一份資訊存三處，彼此可能不一致，且沒有任何機制偵測不一致。

**`VariableSets` 是可以直接刪的那一份**。它的用途只是「依型別名預先分組」，而分組鍵就是 key 的前半段——命名層既然能 `Encode` 出某型別的完整 key 集合、也能 `TryDecode` 出任一 key 的型別名，分組就可以即時由 `Variables` 篩出來，不需要一個常駐的巢狀字典。刪掉它，池就真的只剩 `Variables` 一個投影。

**已經有現成證明**。`ReindexFromModel()`（`OptEngine.cs:258-276`）已經做到「清空框架集合，從 `matrix.NumVars` 與 `matrix.Ranges` 反推重建」。也就是「池是 cplex.model 的投影」這件事技術上可行、程式碼已存在、已在匯入路徑上運作。問題只是它是匯入模式的特例路徑，自建模型走另一條。

**自建與匯入兩套語意**。因為匯入模型沒有 C# 變數類別，現行設計讓 `VariableSets` 在匯入模式保持空的（`OptEngine.cs:200-202`、`:262`），型別化取解（`GetSetVarValues<T>()`、`GetSolution("TypeName")`）在匯入模式直接不可用。連帶讓 `RegisteredVariableCount`（`EngineBase.cs:191`，量的是 `VariableSets`）在匯入模式恆為 0，`ModelInspector` 只好把它列進「失真」清單逐條向使用者解釋（`Templates/ModelInspector/Reporting/ModelInspection.cs:225-228`）。根因都是「從名稱反推型別」的能力不存在——而那正是命名層該負責的事。改成篩選之後，只要名稱符合命名規範，型別化取解對兩種來源都成立，那份失真清單也可以縮短。

**命名邏輯無法單獨驗證**。笛卡兒積展開、key 組字串、token 驗證、前綴判型目前散在 `VariableBuilder`、`DesignBases.ModelElementBase.ToString()`、`ModelNaming`、`VariablePrefixNaming`，而且長在 engine 的建模路徑上。要測一條命名規則就得建一個 engine，導致這層實際上沒有被獨立測過。

## Scope

### In Scope

- 新增命名層：變數 key 與限制式名稱的**產生**（笛卡兒積展開、組 key）與**解析**（拆回型別名與各維度值）、token 驗證、前綴判型。此層不引用任何 solver 型別。
- **移除 `VariableSets`**（`EngineBase.cs:183`）。型別化查詢改為「命名層產生字串集合 → 篩選 `Variables`」，不再維護預先分組的巢狀字典。
- `Variables` / `_verifyConstraints` / `_constraints` 改為 `cplex.model` 的投影，投影的建構點收斂成單一入口。
- 自建與匯入兩條路徑共用同一個池建構點，`ReindexFromModel` 不再是匯入專屬特例。
- 以篩選重新實作所有依賴 `VariableSets` 的 public / protected 成員，對外簽名與語意不變。
- Phase 2：內部身分由 string 換成 int index，字串降級為顯示 / 匯出 / 查找用的名稱。
- 命名層的獨立單元測試（不建立 engine）。

### Out of Scope

- **消費端 API 變更**。`AddLHS(coef, new VariableB_X { ... })`、`BuildVars<T>(sets...)`、`CreateLessEqual(name)` 的簽名與語意不動，Templates 與既有模型專案零改動。
- **CSV / DB 欄位對應的反射**。`IO/ModelRowMapper.cs` 與 `Infrastructure/ReflectionHelper.cs` 的「表頭字串 ↔ property」對應不在本次範圍。
- **solver 無關化**。本次仍以 CPLEX 為唯一 adapter，不為換 solver 做設計。
- **pool 例外殘留問題**。`AddConstraint` 丟例外時 `ClearPool()` 被跳過（`EngineBase.cs:1171-1185`）是既有缺陷，另案處理，不混進本規格。
- **`CreateXxx(rhs, name)` overload 的存廢**。該 overload 覆蓋 `_rhsConst`（`EngineBase.cs:1081`、`1109`、`1137`）的問題另案。

## User Stories / Use Cases

1. As a 框架維護者, I want to 在不建立 engine 的情況下測試命名規則, so that 改一條命名規則不必跑整個建模流程就能驗證。
2. As a 框架維護者, I want to 只有一個地方能決定「模型裡有哪些變數」, so that 不會出現框架說有、CPLEX 說沒有的不一致。
3. As a 使用匯入模型的人, I want to 對 `.lp` / `.sav` 匯入的模型也能用型別化取解, so that 匯入模型不必退回字串 API。
4. As a 既有模型專案的開發者, I want to 框架內部怎麼改都不影響我的程式碼, so that 升級框架不需要改我的 Constraint 與 Objective。

## Acceptance Criteria

### Phase 1（抽接縫，行為不變）

- [ ] **AC1 命名層零 solver 依賴**：命名層的所有型別不 `using` 任何 `ILOG.*`，也不引用 `EngineBase` / `OptEngine`；以 `grep` 可驗證。
- [ ] **AC2 命名層可獨立測試**：新增的命名層單元測試在不建立任何 engine、不需要 CPLEX DLL 的情況下通過。
- [ ] **AC3 池的單一建構點**：寫入 `Variables` 的位置收斂成單一方法，`grep` 得出來只有一處；其餘位置一律唯讀。
- [ ] **AC4 限制池同樣收斂**：`_verifyConstraints` 與 `_constraints` 的寫入點同樣收斂成單一入口。
- [ ] **AC5 兩種來源共用路徑**：自建模型與 `OptModel.FromFile()` 匯入模型走同一個池建構方法；`ReindexFromModel` 不再是獨立的特例實作。
- [ ] **AC6 `VariableSets` 已移除**：`grep -rn "VariableSets" src/` 零結果（含註解），且不留任何等價的常駐分組結構。
- [ ] **AC7 型別化查詢改為篩選**：`GetSetVarValues<T>()`、`GetSetVarValues(setName)`、`GetSetVarNames<T>()`、`GetSolution(varTypeName)`、`GetVariableSet(setName)` 全部改由命名層產生字串集合後篩選 `Variables` 實作，對外簽名與回傳語意不變。
- [ ] **AC8 消費端零改動**：`Templates/` 五個專案與 `ModelInspector` 一行不改即可 build；特別是 Tutorial、RosteringProblem、FJSP_BASIC_BRICK 的 Solution 層共 16 處 `GetSetVarValues<T>()` 呼叫結果與改動前逐值相同。
- [ ] **AC9 測試全綠**：`dotnet build OptimFoundation.sln` 與 `dotnet test` 通過，既有測試不需修改語意。
- [ ] **AC10 型別化取解對匯入模型成立**：匯入一個名稱符合命名規範的模型後，`GetSetVarValues<T>()` 能正確回傳（現行為恆空）；名稱不符規範時降級為空集合並發一次 `Logging.Warn`，不丟例外。

### Phase 2（換實作，接縫不動）

- [ ] **AC11 介面不動**：Phase 1 定下的命名層介面簽名在 Phase 2 不變更。
- [ ] **AC12 消費端仍零改動**：Templates 與 tests 在 Phase 2 後仍一行不改。
- [ ] **AC13 查表改索引**：`AddLHS` / `AddRHS` 解析變數的路徑不再做字串 hash 查表，改為陣列索引；以 benchmark 或程式碼路徑檢視佐證。
- [ ] **AC14 效能不退步**：以現有最大的 Template（RosteringProblem）為基準，建模階段耗時不高於 Phase 1 前的水準；型別化查詢改成篩選後，`GetSetVarValues<T>()` 不得成為新的熱點。

## Module Interactions

- **Core（`OptimFoundation.Core`）**
  - 新增命名層（namespace 待定，見 Open Questions）：承接目前分散在 `VariableBuilder.cs`、`ModelNaming.cs`、`VariablePrefixNaming.cs`、`DesignBases.cs` 的 key 產生與解析邏輯。
  - `EngineBase.cs`：`Variables`（:180）與 `_verifyConstraints`（:275）改為投影；`VariableSets`（:183）刪除。
  - `EngineBase.cs` 要改寫成篩選的成員（皆為 `VariableSets` 的現行依賴者）：`RegisteredVariableCount`（:191）、`AddVariables` 的登記段（:466-475）、變數查找（:681）、`GetVariableSet`（:716-724）、`GetAllVarNames`（:731）、`GetSetVarNames`（:748）、`GetSetVarValues`（:762）、`GetSolution`（:779）、清空（:837）。
  - `DesignBases.cs`：`ModelElementBase.ToString()` 改為委派命名層，不自行組字串。

- **Cplex（`OptimFoundation.Cplex`）**
  - `OptEngine.cs`：`AddVariable`（:305-313）、`AddVariables`（:345）、`AddConstraint`（:361-367）維持把名字交給 CPLEX 的行為；`ReindexFromModel`（:258）升格為兩種來源共用的池建構方法。
  - `_constraints`（:32）與 `_threadConstraints`（:42）的關係要釐清：thread constraints 尚未屬於任何 model，不能納入投影（見 Edge Cases）。

- **Generators（`OptimFoundation.Generators`）**
  - 目前以 `<Compile Include="..\OptimFoundation.Core\VariablePrefixNaming.cs" Link=...>` 共用原始碼。命名層若改變 `VariablePrefixNaming` 的位置或型別可見性，此共享機制要同步調整，否則編譯期（generator）與執行期（Core）規則會漂移。
  - `AutoSetsGenerator.cs:88-93` 的 `VarTypeFromPrefix` 委派 `VariablePrefixNaming.TryResolve`，此依賴要保持。

- **Templates / tests**
  - 目標是零改動。tests 需新增命名層的獨立測試檔。

## API Design

### 命名層介面（Phase 1 定版，Phase 2 不動）

```csharp
namespace OptimFoundation.Core.Naming
{
    /// <summary>變數 / 限制式名稱的產生與解析。不認得任何 solver 型別。</summary>
    public interface IModelNameCodec
    {
        /// <summary>由型別名與各維度值組出 key，格式 TypeName@v1@v2…。</summary>
        string Encode(string typeName, IReadOnlyList<string> dimensionValues);

        /// <summary>把 key 拆回型別名與維度值；格式不符回 false，不丟例外。</summary>
        bool TryDecode(string key, out NameParts parts);

        /// <summary>驗證單一 token 是否合法（保留字元、空白）。</summary>
        bool IsValidToken(string token);
    }

    public readonly struct NameParts
    {
        public string TypeName { get; }
        public IReadOnlyList<string> DimensionValues { get; }
    }

    /// <summary>由多個維度集合展開笛卡兒積，產生一批 key。</summary>
    public interface IVariableKeySetBuilder
    {
        IReadOnlyList<string> Expand(string typeName, params IReadOnlyList<string>[] dimensions);
    }
}
```

`VariablePrefixNaming.TryResolve` 維持現行語意（`VariableB_` / `VariableC_` / `VariableI_`），僅調整可見性以同時服務 Core 與 Generator。

### 池投影介面

```csharp
/// <summary>對建完模的 solver model 做投影，產生框架用的索引。唯一的池建構入口。</summary>
protected abstract PoolSnapshot ProjectFromModel();

protected readonly struct PoolSnapshot
{
    public IReadOnlyDictionary<string, TVar> Variables { get; }
    public IReadOnlyCollection<string> ConstraintNames { get; }
}
```

投影只產生**一層**平坦的 `Variables`，不做任何預先分組。自建路徑與匯入路徑都呼叫此方法，`OptEngine` 的實作即現行 `ReindexFromModel` 的內容（走 `ILPMatrix` 的 `NumVars` / `Ranges`）。

### 型別化查詢改為篩選

`VariableSets` 移除後，所有「取某型別的全部變數」的需求改走同一條路：命名層產生字串集合 → 篩選 `Variables`。

```csharp
/// <summary>依名稱集合篩出變數；集合由命名層產生。</summary>
protected IReadOnlyDictionary<string, TVar> Filter(IEnumerable<string> names);

/// <summary>依型別名篩出變數；內部以 IModelNameCodec.TryDecode 比對 TypeName。</summary>
protected IReadOnlyDictionary<string, TVar> FilterByTypeName(string typeName);
```

`GetSetVarValues<T>()` 等既有 public 成員改以 `FilterByTypeName(typeof(T).Name)` 實作，簽名與回傳語意不變（AC7、AC8）。呼叫端若已握有 key 集合（例如剛從 `Expand()` 拿到的那一批），走 `Filter(names)` 可省去逐筆解析。

## Data Model

無資料庫 schema 變更。

變數 key 格式維持 `TypeName@v1@v2…`，零維變數無 `@`（`ModelNaming.cs`）。保留字元與空白的禁用規則維持 `ModelNaming.ValidateToken` 現行定義（`ModelNaming.cs:109-120`）。

Phase 2 新增內部身分型別：

```csharp
internal readonly struct VarRef
{
    public readonly int Index;   // 對應投影陣列的位置
}
```

`VarRef` 不出現在任何 public 簽名上。

## Edge Cases & Error Handling

- **Thread constraints**：`_threadConstraints`（`OptEngine.cs:42`）尚未加進 model，投影時不得納入，否則會出現「池說有、model 說沒有」。投影方法要明確排除並在文件註明。
- **匯入模型名稱不符命名規範**：`TryDecode` 回 false，該變數仍在 `Variables` 裡（以原始名稱為 key），只是不會被任何 `FilterByTypeName` 選中；投影完成時發一次 `Logging.Warn` 記錄無法歸類的數量，不丟例外。
- **篩選結果為空 vs 型別不存在**：`GetSetVarValues<T>()` 對「該型別確實沒有變數」與「名稱全部無法解析」都會回空集合。兩者要在 log 上可區分，不得讓呼叫端把匯入失敗誤讀成模型裡沒有這種變數。
- **CPLEX 自動改名或無名變數**：匯入的模型可能有空名或重複名。空名變數以 solver index 產生穩定替代名；重複名發 `Logging.Warn` 並保留第一個，行為與現行 `ReindexFromModel` 一致。
- **投影時機**：投影必須在變數建立之後、限制式引用之前可用。若在變數尚未建立時查表，回傳明確例外（含 event code、context、value、reason、`result=aborted`），不得回 null 讓呼叫端靜默失敗。
- **模型變動後的投影失效**：`BuildVars` 之後又新增變數時，投影需重建或增量更新。策略見 Open Questions。
- **soft constraint 與 objective**：`_objectiveTerms`（`EngineBase.cs:279`）與 `_softPenaltyTerms`（:280）是建模中的暫存 buffer，不是池，不在投影範圍內。

## Non-Functional Requirements

- **Performance**：`VariableBuilder` 現行註解明載「直接組字串、不建 element 實例（比反射快 10x+）」。命名層抽出後 NEVER 引入 per-variable 的反射或實例建立；投影 NEVER 引入 per-term 的 solver interop 呼叫。
- **相容性**：Templates 與 tests 零改動是硬條件（AC6、AC10）。命名層的 public 介面在 Phase 1 定版後即為契約。
- **可測試性**：命名層測試不得依賴 CPLEX DLL 或 license。
- **Observability**：投影建構完成時記錄變數數與限制式數；無法歸類的變數數量發一次 `Logging.Warn`。所有主動錯誤依 workspace CLAUDE.md 規定，throw 前留下含 event code、context、value、reason、`result=aborted` 的 Error Log。

## Breaking Changes

Phase 1 與 Phase 2 對消費端皆無 breaking change（AC6、AC10）。

框架內部的 breaking change：

- `protected readonly Dictionary<string, TVar> Variables` 改為唯讀投影，任何在 `EngineBase` 子類中直接寫入該字典的程式碼會失效。目前僅 `OptEngine.ReindexFromModel`（`OptEngine.cs:260-262`）有此行為，會一併改寫。
- **`protected readonly Dictionary<string, Dictionary<string, TVar>> VariableSets` 移除**。子類若直接讀寫此欄位會編譯失敗。框架內目前只有 `OptEngine.ReindexFromModel`（`OptEngine.cs:262`）與 `MockEngine`（`tests/.../Mocks/MockEngine.cs:53` 的註解）涉及。
- `protected Dictionary<string, TVar> GetVariableSet(string setName)`（`EngineBase.cs:716`）回傳型別由可變 `Dictionary` 改為 `IReadOnlyDictionary`（篩選結果不應可寫）。
- `RegisteredVariableCount`（`EngineBase.cs:191`）語意變更：現行量的是 `VariableSets`（僅 `Build*Vs` 登記者，排除軟性限制式的彈性變數與匯入變數，見 `EngineBase.cs:411` 註解）。`VariableSets` 移除後需重新定義，見 Open Questions。此變更會連動 `PreSolveGuard` 的 `ScaleWarnThreshold` 判斷基準，以及 `ModelInspector` 的失真清單（`Templates/ModelInspector/Reporting/ModelInspection.cs:43`、`:228`、`Reporting/ReportWriter.cs:199`）。
- `ModelElementBase.ToString()` 的實作改為委派命名層。輸出格式不變，但覆寫此方法的自訂型別行為需複驗。

## Open Questions

- [ ] **命名層的落地位置**：獨立 namespace（`OptimFoundation.Core.Naming`）還是獨立組件（`OptimFoundation.Naming.dll`）？獨立組件能真正保證零 solver 依賴，但會影響 Generator 的 linked source 機制與 `dlls/` 的散佈方式。
- [ ] **`AddLHS` 的解析時機**：(a) 每次即時查 `cplex.model`、(b) 建完變數後快取一份投影索引、(c) 延後到 `CreateXxx` 才一次解析。初步傾向 (b)——保住效能又維持 cplex.model 為真相。待確認。
- [ ] **投影失效策略**：`BuildVars` 之後再建變數時，投影是整份重建還是增量更新？整份重建簡單但在多次 `AddVariables` 的情境下是 O(n²)。
- [ ] **`RegisteredVariableCount` 的新定義**：`VariableSets` 移除後，這個數字要改成量什麼？(a) 等同 `Variables.Count`（語意最單純，但 `PreSolveGuard` 的門檻基準改變，且不再能區分「框架建的」與「匯入的 / 軟性限制式的彈性變數」）、(b) 以命名層可解析的變數數為準（保留原本「框架建的」語意，但每次計算要掃描）、(c) 移除這個屬性，由 `ModelInspector` 自行計算。
- [ ] **篩選的實作策略**：`FilterByTypeName` 以 (a) `TryDecode` 逐筆比對 TypeName、還是 (b) 前綴字串比對 `TypeName@`（零維變數需特例）？(a) 正確性高、(b) 快。若 `GetSetVarValues<T>()` 成為熱點，Phase 2 可在投影時順便建型別 → 索引範圍的對照表——但那是投影的衍生索引，不是回頭恢復 `VariableSets`。
- [ ] **規格基準**：以目前 worktree（`ISolverEngine` + `Enums` 已併入 `EngineBase.cs`）為基準，還是等該輪重構落地後再定？本規格的行號皆取自目前 worktree。
- [ ] **Acceptance Criteria 確認**：AC1–AC14 是我初擬，待使用者確認或調整。

## Implementation Plan

### Stub 階段（先做）

- [ ] 建 `IModelNameCodec`、`NameParts`、`IVariableKeySetBuilder` 介面檔，成員簽名齊全，body 一律 `throw new NotImplementedException()`
- [ ] 建 `PoolSnapshot` struct（只含 `Variables` 與 `ConstraintNames`）與 `EngineBase.ProjectFromModel()` abstract 簽名
- [ ] 建 `Filter(IEnumerable<string>)` 與 `FilterByTypeName(string)` 的空殼簽名
- [ ] `OptEngine` 加上 `ProjectFromModel()` override 的空殼簽名
- [ ] 建命名層測試專案骨架（或在既有 tests 下新增資料夾），測試方法簽名齊全、body `Assert.Fail("TODO")`
- [ ] 跑 `dotnet build OptimFoundation.sln` 確認型別與契約連得起來

### 逐層實作（Phase 1）

- [ ] 命名層實作：`Encode` / `TryDecode` / `IsValidToken`，行為與現行 `ModelNaming` 逐條對齊
- [ ] 命名層實作：`Expand` 笛卡兒積，行為與現行 `VariableBuilder` 對齊，保留不建實例的字串路徑
- [ ] 命名層單元測試補滿（含既有 `ModelNaming` 測試的遷移）
- [ ] `DesignBases.ModelElementBase.ToString()` 改為委派命名層
- [ ] `OptEngine.ProjectFromModel()` 實作：由現行 `ReindexFromModel` 內容改寫
- [ ] `EngineBase` 的 `Variables` / `_verifyConstraints` 改為投影，收斂寫入點
- [ ] 自建路徑改為呼叫同一個投影入口
- [ ] 實作 `Filter` / `FilterByTypeName`，把 `GetSetVarValues` / `GetSetVarNames` / `GetSolution(varTypeName)` / `GetVariableSet` / `GetAllVarNames` 逐一改寫成篩選
- [ ] 決定並實作 `RegisteredVariableCount` 的新定義（依 Open Questions 的結論），同步更新 `PreSolveGuard` 與 `ModelInspector` 的說明文字
- [ ] 刪除 `VariableSets` 欄位與所有殘留引用（含註解），`grep` 驗證歸零
- [ ] 全套測試 + 五個 Templates + ModelInspector build 驗證；Solution 層 16 處 `GetSetVarValues<T>()` 逐值比對改動前後一致

### 逐層實作（Phase 2）

- [ ] 引入 `VarRef`，投影改為陣列 + 名稱索引
- [ ] `AddLHS` / `AddRHS` / objective / soft constraint 的解析路徑改走索引
- [ ] benchmark 對照 Phase 1 前後耗時
- [ ] 全套測試 + Templates build 驗證

## References

- 受影響的既有規格：`specs/2026-09-18-model-source-duality-and-profile.md`（模型來源二態；本規格把它的 `ReindexFromModel` 從匯入特例升格為共用路徑）
- 現況地圖：`CodeMap.md`（2026-09-18 同步）
- 相關既有缺陷（另案）：`AddConstraint` 例外路徑跳過 `ClearPool`（`EngineBase.cs:1171-1185`）；`CreateXxx(rhs, name)` 覆蓋 `_rhsConst`（`EngineBase.cs:1081`、`1109`、`1137`）
