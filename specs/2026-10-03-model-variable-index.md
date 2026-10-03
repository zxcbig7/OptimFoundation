---
title: 變數索引分層：Model 層管命名與批量規劃，Engine 只認名稱
status: draft
created: 2026-10-03
updated: 2026-10-03
modules: [core, cplex, tests]
---

# 變數索引分層

## Summary

變數「叫什麼名字、屬於哪個型別、一次要撈哪些」由 Model 層決定；Engine 只提供以名稱為單位的介面：批次建立、查單一變數、列出全部名稱、批次讀解值。

這裡的 Model 層指建模這一側的命名與索引工具（放在 Core、與 solver 無關、不建 engine 就能測），不是 `OptModel` 這個組裝流程。

使用端 API（`BuildVars<T>`、`AddLHS`、`GetSetVarValues<T>`、`GetSolution(type)`）留在 engine 上，內部改成轉給 Model 層：Model 層算出名稱清單，再拿清單呼叫 Engine 的名稱介面。範本與 AI-Modeling 專案零改動。

本 spec 取代已刪除的 `2026-09-18-pool-single-source-and-naming-layer.md`：那份的「移除 `VariableSets`、型別查詢改成篩選」已在 `04fa4cb` 完成；「變數池改成 cplex.model 的投影」不採用（見 Motivation）；int index 不列入。

## Motivation / Why

**命名知識散在 Engine。** `EngineBase` 現在同時負責：

- 組名：`BatchBuild` 呼叫 `VariableManager.ComposeNames`（`EngineBase.cs:511-516`）
- 去重：`SkipDuplicateVariableNames`（`EngineBase.cs:558`）
- 依型別篩選：`FilterVarNames`（`EngineBase.cs:775`）
- 前綴判型：`TryResolveVariableType` / `ValidateExplicitVariableType`（`EngineBase.cs:576` / `:598`）
- 從名稱拆出型別名：`VariableGroup`（`EngineBase.cs:996`）

要測一條命名規則就得建 engine；接新 solver 時，engine 裡也混著與 solver 無關的命名邏輯。

**取解逐個呼叫 solver。** `GetSetVarValues` / `GetSolution` 對每個變數各呼叫一次 `GetVariableValue`（`OptEngine.cs:823`，每次一個 `Model.GetValue`）。OptEngine 已經有一次讀整批的 `Model.GetValues`（`GetSolutionByType`，`OptEngine.cs:1119`），但只給 `GetCVSolution` / `GetIVSolution` / `GetBVSolution` 用。

**變數池的定位。** `Variables` 是框架宣告的索引，可以多於 CPLEX 實際收進模型的變數（沒被限制式或目標式引用的變數 CPLEX 不收）。因此不採「池 = cplex.model 投影」；另外自建模型沒有 LPMatrix，`ReindexFromModel` 的走訪方式只適用匯入的模型。

## Scope

### In Scope

- Model 層承接變數命名的全部邏輯：組名、實例轉名稱、去重、依型別取名稱、前綴判型、從名稱取型別名。
- Engine 名稱介面新增批次讀值 `GetVariableValues(names)`；其餘沿用現有的 `AddVariables`、`ReadVar(string)`、`GetAllVarNames`、`GetVariableValue`。
- 使用端 API 改成轉接：命名與篩選交給 Model 層，拿名稱清單呼叫 Engine。
- `GetSetVarValues`、`GetSolution()`、`GetSolution(type)` 改走批次讀值。

### Out of Scope

- 使用端 API 的簽名與語意。
- 限制式命名（`ComposeConstraintName`、`ConstraintGroup`），另案處理。
- int index；變數池改成 cplex.model 投影。
- 同一個 engine 重複 `Build()`（2026-10-03 決定不動）。

## User Stories / Use Cases

1. As a 框架維護者, I want to 不建 engine 就能測命名規則, so that 改一條規則不必跑整個建模流程。
2. As a 接新 solver 的人, I want to Engine 只需要處理名稱與 solver 物件, so that 不用碰 `TypeName@…` 格式。
3. As a 模型專案開發者, I want to 取解時一次讀完整批變數, so that 大模型輸出解不會因為逐個呼叫 solver 而變慢。
4. As a 既有模型專案開發者, I want to 框架內部怎麼分層都不影響我的程式碼, so that 升級框架不必改 Constraint、Objective 與 Solution。

## Acceptance Criteria

- [ ] **AC1 Model 層與 solver 無關**：不引用 `ILOG.*`、`EngineBase`、`OptEngine`；以 grep 驗證。
- [ ] **AC2 Model 層可獨立測試**：組名、去重、依型別取名稱、前綴判型、從名稱取型別名都有不建 engine 的單元測試。
- [ ] **AC3 Engine 不含變數命名知識**：`EngineBase.cs` 內 grep `VariablePrefixNaming`、`FilterVarNames`、`SkipDuplicateVariableNames`、`VariableGroup` 為零，改為呼叫 Model 層。
- [ ] **AC4 批次讀值**：`ISolverEngine` 新增 `GetVariableValues(IReadOnlyList<string> names)`；`EngineBase` 預設逐個呼叫 `GetVariableValue`；`OptEngine` 覆寫為單次 `Model.GetValues`。`GetSetVarValues`、`GetSolution()`、`GetSolution(type)` 一律經由它，並以 MockEngine 計數驗證每次取解只呼叫一次。
- [ ] **AC5 消費端零改動**：`Templates/` 與 AI-Modeling 專案不改一行即可 build；`GetSetVarValues<T>()` 結果與改動前逐值相同。
- [ ] **AC6 行為不變**：`[VARIABLE_DUPLICATE]`、`[UNREFERENCED_VARIABLES]` 的 log 格式與建立統計不變；`dotnet build OptimFoundation.sln` 與 `dotnet test` 全綠。

## Module Interactions

- **Core**
  - Model 層 `VariableManager.cs`（見 Open Questions 1）：現有內容，加上 `EngineBase` 的 `FilterVarNames`、`SkipDuplicateVariableNames`、`TryResolveVariableType`、`ValidateExplicitVariableType`、`VariableGroup`。
  - `EngineBase.cs`：使用端 API 改為轉接；`ISolverEngine` 與 `EngineBase` 新增 `GetVariableValues`。
  - `IO/csv/CsvCtrl.cs:132`、`IO/db/OracleDbCtrl.cs:278 / :456`：仍呼叫 `GetSolution(typeName)`，不用改，自動受惠於批次讀值。
- **Cplex**
  - `OptEngine.cs`：覆寫 `GetVariableValues`，以 `Model.GetValues(INumVar[])` 一次讀取；`GetSolutionByType` 可共用同一段。
- **Generators**
  - 不動。`VariablePrefixNaming.cs` 仍以 linked source 編入 Generator。
- **Tests**
  - `VariableManagerTests` 隨 Model 層擴充；新增 Model 層獨立測試與 MockEngine 批次讀值計數測試。

## API Design

```csharp
// Model 層（Core，static、無狀態）
public static class VariableManager
{
    // 組名：sets 笛卡兒積 → TypeName@v1@v2…；泛型版檢查維度數量
    public static IReadOnlyList<string> ComposeNames<TVariable>(object[] sets);
    public static IReadOnlyList<string> ComposeNames(string typeName, object[] sets);

    // 變數實例 → 名稱（實例的 ToString()）
    public static string NameOf(object varSpec);

    // 去重：批內重複或 exists 為 true 的名稱略過，寫 [VARIABLE_DUPLICATE] WARN
    public static IReadOnlyList<string> SkipDuplicates(string typeName, IReadOnlyList<string> names, Func<string, bool> exists);

    // 依型別取名稱：名稱等於型別名（0 維），或以「型別名@」開頭
    public static IEnumerable<string> NamesOfType(string typeName, IEnumerable<string> allNames);

    // 名稱 → 型別名（第一個 @ 之前）；建立統計與未引用變數分組用
    public static string TypeNameOf(string name);

    // 類別名前綴 → VarType（VariableB_ / VariableC_ / VariableI_）
    public static bool TryResolveVarType(string className, out VarType type);
}

// Engine 名稱介面
public interface ISolverEngine
{
    double GetVariableValue(string name); // 既有
    IReadOnlyDictionary<string, double> GetVariableValues(IReadOnlyList<string> names); // 新增
}
```

使用端 API 轉接示意（簽名不變）：

```csharp
public Dictionary<string, double> GetSetVarValues(string setName)
{
    var names = VariableManager.NamesOfType(setName, Variables.Keys).ToList();
    return new Dictionary<string, double>(GetVariableValues(names));
}
```

## Edge Cases & Error Handling

- 名稱清單為空：回空字典，不呼叫 solver。
- 名稱不在池裡：`VARIABLE_NOT_FOUND`（同 `ReadVar`）。由池篩出的清單正常不會發生。
- 匯入模型的名稱不符 `TypeName@…`：`NamesOfType` 回空（同現行行為）。
- 尚未求解或沒有可用解：solver 丟例外，維持 `SOLUTION_READ_FAILED` Error Log 後原樣 rethrow。
- 回傳順序：照變數池的建立順序（同現行行為）。

## Open Questions

1. ~~**Model 層類別名**~~ —— 已決定（2026-10-03）：`VariableBuilder` 改名為 `VariableManager`，並已完成。同批改名：`GetVarNames` → `ComposeNames`（string 版參數 `setName` → `typeName`）、`ConvertSetsToStringLists` → `ConvertSetsToTokens`、`ConvertSetsToVarPartLists` → `ConvertSetsToRows`、`GenVarParts` → `CombineRows`。
2. ~~`VariableBuilder.BuildVars<TVariable>(Action<object>, object[])` 是否移除~~ —— 已決定（2026-10-03）：移除，連同只服務它的建構子快取 `GetCtor`，以及同樣沒有呼叫者的 `GenVarCombinations`。
3. **`GetVariableValues` 的可見度**：建議放上 `ISolverEngine`（public），`CsvCtrl` / `OracleDbCtrl` 這類只拿 `ISolverEngine` 的程式也能批次讀；或只放在 `EngineBase` 當 protected。

## Implementation Plan

1. 擴充 Model 層 `VariableManager`：搬入 `EngineBase` 的 `FilterVarNames`、`SkipDuplicateVariableNames`、`TryResolveVariableType`、`ValidateExplicitVariableType`、`VariableGroup`；補不建 engine 的單元測試（AC1、AC2）。
2. `EngineBase` 改呼叫 Model 層，確認 AC3 的 grep 為零。
3. `ISolverEngine` / `EngineBase` 新增 `GetVariableValues`，`OptEngine` 覆寫為 `Model.GetValues`；`GetSetVarValues`、`GetSolution` 改走它；補 MockEngine 計數測試（AC4）。
4. 全量 build、test、template build（AC5、AC6）；同步 `developer-guide.md`、`CodeMap.md`、AI-Modeling `optimfoundation-api-guide.md`。
