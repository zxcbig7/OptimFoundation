---
title: 框架層資料防護 —— load 後的機械邏輯集中到 Core（驗證 / CSV / scale guard / 數值 sanity / 輸出 transaction）
status: implementing
created: 2026-07-18
updated: 2026-07-18
modules: [core, generators, cplex, gurobi, templates]
---

# 框架層資料防護（Framework Data Guard）

## Summary

把「load 之後」的機械邏輯從各專案手寫、複製，集中進 OptimFoundation 框架寫一次：
① `DataContext` 驗證器（參照完整性 / key 唯一性 / 完整性 / 數值 sanity，聚合 fail-fast）；
② `CsvCtrl` 解析升 RFC4180；③ 引擎 `Solve()` 前的 scale guard；④ 輸出端 transaction 邊界。
硬約束：**LOAD 維持顯式手寫**（每行 `.Load()` 不變、不回退反射自動載入）；驗證邏輯**零專案複製**。

## Motivation / Why

- 資料層現在把「載入正確性」做得好（Set 四道防呆、缺欄明確例外），但「資料**語意**正確性」幾乎全裸：parameter 引用不存在的 set 成員、重複 key、缺格靜默補 0（消費端 `FirstOrDefault(...)?.QTY ?? 0.0`）→ solver **靜默解出錯的最優解**，實務最難抓。
- 過去這類驗證是每個專案 ctor 尾端手寫（如 `ValidateSetsCoverParameters()`），**複製到每個專案、難維護**。Vic 定案：機械邏輯集中框架、專案只留顯式宣告。
- CSV `SplitLine` = `Replace("\"","").Split(',')`，不支援 RFC4180，zh-TW Excel 匯出含逗號欄位即壞。
- BigM 由數據推導（`maxCap / min(正 MachineHours)`），無值域 sanity → 除零 / `Infinity` / 過大 BigM 造成 solver 數值不穩。
- 變數量 `|Set₁|×|Set₂|×…` 爆炸無估算/警告；DB 輸出多變數型別分批寫，無 transaction、失敗留髒資料。

## Scope

### In Scope

- **`DataContext`**（Core 新增）：`Dataload` 的 base，持有 set/param 註冊表，建構完成時聚合驗證。
- **`DataValidator`**（Core 新增）：四類檢查 + 聚合報告 + 載入摘要 log。
- **`AutoSetsGenerator` 擴充**（Generators）：為每個 `Parameter_*` emit「index-set metadata」+ 註冊碼，供 `DataContext` 消費（**非 runtime reflection**）。
- **`[FullGrid]` attribute**（Core 新增）：opt-in 標記某 parameter 需全格覆蓋。
- **`CsvCtrl` 解析升級**：RFC4180 引號 / `""` 跳脫 / 含逗號欄位。
- **`EngineBase` template method**：`Solve()` / `Build()` 收進 Core，前置 scale guard；各 engine 改實作 `SolveCore()` / `BuildCore()`（動 Cplex + Gurobi）。
- **`Numeric.SafeRatio` helper**（Core 新增）：帶除零 / 非有限 / 量級門檻防呆的比值，供 BigM 這類推導選用。
- **輸出 transaction**：`IDbCtrl` 加 transaction primitive；新增 `OracleSolutionSink`（多變數型別原子寫入）；`ISolutionSink` 加 batch/transaction API（CSV 為 no-op）。
- **`Templates/Tutorial`**：改用 `DataContext` + 新建構路徑，作權威示範。
- **`Templates/dlls/`**：Core(+Cplex+Gurobi) rebuild 後重佈。

### Out of Scope

- **多情境（scenario）載入**：本波不做（輸出端 `dataId` hook 已在，未來補不 breaking）。
- 反射自動載入 / DB 表名慣例 / 讓 `DbDataSource` 實作 `IDataSource`：永久禁止（見 `2026-07-16-io-interface.md`）。
- CSV 欄位內換行（quoted newline）：不支援，維持逐行讀，文件標明限制。
- scale guard 自動 throw / 自動拆解模型：只 warn，不阻擋、不改模型。
- Gurobi runtime 驗證：license 過期，只保證 compile。

## User Stories / Use Cases

1. As a 建模者, 我 load 完資料, 只要有 parameter 引用了不存在的 set 成員 / 重複 key / QTY 是 NaN, 建構當下就一次收到**全部**錯誤清單, 不用等 solver 解出鬼答案才發現。
2. As a 建模者, 我的 CSV 有 `"Product, Large"` 這種含逗號欄位, 讀進來正確不裂欄。
3. As a 建模者, 我某 parameter 語意上必須全格覆蓋, 我在類別上加 `[FullGrid]`, 缺格立即報。
4. As a 建模者, 我的模型變數量爆到千萬級, solve 前就收到警告, 知道是規模問題不是卡死。
5. As a 建模者, 我把解寫進 Oracle, 中途失敗時不會留下寫一半的髒資料。
6. As a 框架維護者, 上述邏輯只在框架有一份, 新專案繼承即得, 不用複製任何驗證 code。

## Acceptance Criteria

**驗證器（DataContext / DataValidator）**
- [x] parameter 某 index-set 欄位值不在對應 Set 內 → 建構時報「dangling」錯，含 parameter 名 / 欄位 / 違規值 / 所屬 Set。端到端實證：`[Dangling] Parameter_Demand: 第 7 列 index 欄位 'Product' 值 'Ghost' 不在 Set 'Product' 內。`
- [x] 同一 parameter 出現重複 index-key（如兩列同 `(Product,Date)`）→ 報「duplicate key」錯，含重複的 key。實證：`index key (Desk,2026-08-01) 重複：第 1 列與第 7 列。`
- [x] 標了 `[FullGrid]` 的 parameter 缺格 → 報缺哪些 `(set₁,set₂,…)` 組合；未標則不檢查完整性（opt-in 已測：稀疏且未標者不誤報）。實證：`[MissingCell] Parameter_Demand: [FullGrid] 缺 1 格（共 6 格）：(Chair,2026-08-02)`
- [x] 任一 parameter 的 `double` 欄位（含 `QTY`）為 `NaN` / `±Infinity` / 超過量級門檻（**1e15**，與 `SafeRatio` 的 1e9 刻意不同）→ 報「numeric」錯。
- [x] 多個錯誤**一次全報**（聚合），不是遇到第一個就中止；訊息含載入摘要（各 set 成員數、各 param 列數）。
- [x] 驗證由 generator 註冊的 metadata 驅動，**程式中無 `GetFields()`/`GetProperties()` 掃 Dataload 欄位的 runtime reflection**（index/數值取值改由 generator emit 的編譯期 lambda 萃取）。
- [x] `Dataload` 的 `.Load(...)` 顯式行**一行未改**；驗證不寫在專案 ctor 內（邏輯零複製）。
- [x] **追加**：index-set 名不存在 → 報 `MissingSet`，且**每個 parameter 只驗一次**（NOT 掛在列迴圈上——否則「零列 parameter + set 名打錯」會完全靜默）。
- [x] **追加**：值型別與 Set 元素型別不符 → 報 `TypeMismatch` 而非 `Dangling`（錯誤訊息指向宣告打錯，不誤導成「值不存在」）。
- [x] **追加**：`Set_*`/`Parameter_*` 型別漏掛 `[OptSet]`/`[OptParam]` → **compile error `OPTF006`**（否則該欄位靜默不註冊、永不受驗）。

**CSV RFC4180**
- [x] `"a, b",c` 解析成 `["a, b","c"]`（引號內逗號不裂欄）。
- [x] `"he said ""hi"""` 解析成 `he said "hi"`（`""` → 跳脫引號）。
- [x] 前後空白、無引號欄位行為與現況一致（現有 CSV / 測試不 regression；`SplitLine`/`ReadLines`/`ReadTable`/`ReadParameter`/`BuildParameter` 五處統一改用同一套解析）。
- [x] 欄位內含換行 → 未閉合引號丟 `InvalidDataException`（含行號），文件標明不支援。

**Scale guard**
- [x] `TotalVarCount` 超過 `ISolverConfig.ScaleWarnThreshold`（預設 **10,000,000**）→ solve 前 `Logging.Warn` 一則（含實際變數數與門檻），**不 throw、不中止**（已測：超門檻仍 `Solve()` 回 true）。
- [x] 未超門檻 → 無額外 log。
- [x] 門檻可經 config 覆寫（default interface member，不破壞既有實作者）。

**數值 sanity helper**
- [x] `Numeric.SafeRatio(num, den)`：`den==0` / 結果非有限 / 超量級門檻（預設 1e9）→ throw 明確例外（含 context 字串）；正常回比值。
- [x] Tutorial 的 `BigM` 改用 `Numeric.SafeRatio` 示範（實際值 60 = 60/1，遠低於門檻，不誤擋）。

**輸出 transaction**
- [x] `OracleSolutionSink` 在單一 transaction 內寫多個變數型別；任一步失敗 → 全 rollback，DB 無殘留。
- [x] `CsvSolutionSink` 的 batch/transaction API 為 no-op（行為與現況一致；已測 batch 產出的檔案內容與直接 `WriteSolution` 逐字相同）。
- [x] `IDbCtrl` 具備 begin/commit/rollback（或等效 `ExecuteInTransaction`）primitive；交易編排下沉到 `DBCtrlBase`（與 Oracle 無關），用 BCL `IDbConnection`/`IDbTransaction` 承載，具體驅動只覆寫 `CreateRawConnection`。
- [x] `IDbCtrl.ExecuteBatch` 批次寫入：`OracleSolutionSink.WriteRows` 改走 array-bind，不再逐列 `Execute`（解輸出從逐列 INSERT 退化前的效能等級救回）。

**整體**
- [x] **全 solution `dotnet build` 綠、0 錯誤**（8 專案：4 src + tests + Tutorial + FJSP_BASIC_BRICK + Template_CPLEX）。註：本規格開工時 `Template_CPLEX` 有 pre-existing 編譯錯誤（真病灶是兩個 Parameter 檔缺 `using SandBox.SetClass;`，非外傳的缺 `[OptSet]`），`Template_Gurobi` 等四個範本已依使用者指示淘汰；兩者皆已於收尾階段處理完畢，故全 solution build 現已納入驗收並通過。
- [x] 既有 xUnit 全過，無 regression（開工基準 94 支 → 結案 **136 支全過**，本規格新增 42 支）。
- [x] Tutorial `dotnet run` 解出、目標值與改前一致（全程 `Optimal` / `ObjVal=500.0000000000001`，逐位不變——驗證只擋壞資料，不改好資料的解）。
- [x] `Templates/dlls/` 已更新為新 build（2026-07-18 21:02 重佈；已用型別存在性驗證含 `ExecuteBatch`/`DBCtrlBase`/`DataValidator`/`ExecuteInTransaction`。註：此目錄被 gitignore，git 檢查看不到，MUST 用檔案時間或型別存在性驗證）。

## 結案狀態（2026-07-18）

**實作與驗收皆完成**：Acceptance Criteria 全數勾選；136 支 xUnit 全過（開工基準 94）；Tutorial 全程 `Optimal / ObjVal=500.0000000000001` 逐位不變；`Templates/dlls/` 已重佈。每個區塊皆由**獨立 fresh-context verifier** 驗收（產出者不自驗），關鍵行為另以**破壞性測試**與**反向證明**佐證。

驗收過程攔下的 4 個 BLOCKER（皆已修）：
1. `Set_*`/`Parameter_*` 漏掛 attribute 會**靜默不註冊**、永不受驗 → 補 `OPTF006` compile error。
2. `MissingSet` 偵測掛在**列迴圈**內 → 「零列 parameter + set 名打錯」完全靜默 → 提升為每 parameter 驗一次。
3. 交易測試只驗**假物件自己模擬**的 commit/rollback，真實作**零覆蓋**（註解掉 `Rollback()` 測試仍全過）→ 交易編排下沉 `DBCtrlBase`，改用假 `IDbConnection` 驅動真邏輯，雙向反向證明皆能抓到破壞。
4. 解輸出為了好測而從 array-bind 退化成**逐列 INSERT**（大模型慢 1–2 個數量級）→ 批次寫入提升為 `IDbCtrl.ExecuteBatch`，可假造且真實作用 array-bind。

**尚未完成 / 已知限制**：
- 變更仍在 working tree，**未 commit**（待使用者 review）。
- **真實 Oracle 未驗證**：交易編排與 sink wiring 已測，但真連線、array-bind 實際行為、整欄皆 null 時的 `OracleDbType` 推斷 fallback 都沒有測試 DB 可驗。
- `Templates/Template_CPLEX`、`Templates/Template_Gurobi` 的 pre-existing 編譯錯誤未處理（使用者明確指示不碰，另案）。
- 多情境（scenario）載入本波不做（見 Out of Scope）。

## Module Interactions

- **Core**：`DataContext`（base）、`DataValidator`、`FullGridAttribute`、`Numeric`；`CsvCtrl`（解析）；`EngineBase`（template method + scale guard）；`IDbCtrl`（+transaction）、`ISolutionSink`（+batch）。
- **Generators**：`AutoSetsGenerator` 為 `Parameter_*` 額外 emit index-set metadata + `DataContext` 註冊碼（`Dataload` 標 `partial`）。
- **Cplex / Gurobi**：`OptEngine.Solve()/Build()` → rename `SolveCore()/BuildCore()`；`OracleSolutionSink` 放 Cplex 或 Core.IO（依 IDbCtrl 位置，實作階段定）。
- **Templates/Tutorial**：`Dataload : DataContext`、`partial`；建構走 blessed path；`BigM` 用 `SafeRatio`。
- **Infra**：Oracle（transaction）；Gurobi 只 compile 不 runtime 驗。

## API Design

> 簽名為草案，stub 階段對照現有 code 微調。

### DataContext（Core，新增）

```csharp
public abstract class DataContext
{
    // 由 generator emit 的 partial 呼叫，登記「這顆 set」「這批 param + 它的 index-set 名 + 是否 FullGrid」
    protected void RegisterSet(string name, ISetBrick set);
    protected void RegisterParam<T>(IReadOnlyList<T> rows, string[] indexSets, bool fullGrid)
        where T : ModelElementBase;

    // 聚合四類檢查，任何違規 → 一次全列在 DataValidationException；同時 Logging.Info 載入摘要
    protected void ValidateData();
}

// blessed 建構路徑：new + generator 註冊 + 驗證，一次到位（邏輯全在框架）
public static class OptData
{
    public static T Load<T>(Func<T> factory) where T : DataContext; // 唯一多載，支援任意 ctor（含多來源）
}
```

專案端（Tutorial）唯一變化：class 宣告加 `: DataContext` + `partial`，建構點 `new Dataload(src)` → `OptData.Load<Dataload>(...)`。**`.Load()` 那幾行不動**。

### FullGrid（Core，新增）

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class FullGridAttribute : Attribute { }   // 標在 Parameter_* 上 → 啟用完整性檢查
```

### DataValidationException（Core，新增）

```csharp
public sealed class DataValidationException : Exception
{
    public IReadOnlyList<DataIssue> Issues { get; } // 每筆：Kind(Dangling/DuplicateKey/MissingCell/Numeric)、Parameter、細節
}
```

### CsvCtrl（解析升級）

```csharp
// 舊：line.Replace("\"","").Split(',')  → 新：RFC4180（引號 / "" 跳脫 / 含逗號欄）
private static string[] SplitLine(string line);   // 內部改寫，對外行為升級
```

### EngineBase（template method + scale guard）

```csharp
public bool Solve() { PreSolveGuard(); return SolveCore(); }  // Solve 由 abstract → concrete
public void Build() { BuildCore(); }                          // 同理（若需前置）
protected abstract bool SolveCore();
protected abstract void BuildCore();
private void PreSolveGuard();   // TotalVarCount > Config.ScaleWarnThreshold → Logging.Warn
// ISolverConfig 增：int ScaleWarnThreshold { get; } 預設值待定（e.g. 10_000_000）
```

### Numeric（Core，新增）

```csharp
public static class Numeric
{
    // den==0 / 非有限 / |result|>ceiling → throw；context 進錯誤訊息
    public static double SafeRatio(double numerator, double denominator,
        double magnitudeCeiling = 1e9, string context = null);
}
```

### 輸出 transaction

```csharp
public interface IDbCtrl : IDisposable   // 增：
{
    void ExecuteInTransaction(Action<IDbCtrl> work);
    // 批次寫入：同一 SQL 套用多列參數一次送出，取代逐列 Execute（Oracle 端用 array-bind 實作）
    void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows);
}

// DBCtrlBase：ExecuteInTransaction 的交易編排（ambient 連線/交易、巢狀參與外層、
// commit/例外 rollback、finally 清理）與 Oracle 無關，集中在這裡，用 BCL IDbConnection/
// IDbTransaction 承載。具體驅動只需覆寫連線建立方式。
public abstract class DBCtrlBase : IDbCtrl
{
    protected abstract IDbConnection CreateRawConnection();
    public void ExecuteInTransaction(Action<IDbCtrl> work) { /* 已下沉、具體驅動不再各自實作 */ }
}

public interface ISolutionSink   // 增 batch：多型別原子寫入
{
    ISolutionBatch BeginBatch(string dataId = null, string userId = null);
}
public interface ISolutionBatch : IDisposable
{
    void Write<TVariableClass>(ISolverEngine engine);
    void Commit();   // 未 Commit 即 Dispose → rollback
}

// OracleSolutionSink.WriteRows 呼叫 IDbCtrl.ExecuteBatch（array-bind），非逐列 Execute。
public sealed class OracleSolutionSink : ISolutionSink { /* 真 transaction + array-bind 批次寫入 */ }
// CsvSolutionSink.BeginBatch → no-op batch（逐檔寫，Commit 空實作）
```

## Data Model

無新增 DB schema。`OracleSolutionSink` 沿用既有解輸出表（欄位 `DATA_ID` 等），差異僅在寫入包進 transaction。

## Edge Cases & Error Handling

- **空 parameter 表**：不算錯（合法「這個 param 這批沒資料」）；但 `[FullGrid]` + 對應 set 非空 → 報缺格。
- **parameter 有多個 double 欄**：全部納入 numeric sanity，非只 `QTY`。
- **index-set 欄位型別非 string（DateTime/int）**：比對用 Set 的實際元素型別（`SetBase<T>` 已保序 + `Contains`）。
- **`OptData.Load` 以外的建構**（直接 `new Dataload()`）：仍可編譯但不觸發驗證 → 文件標明 `OptData.Load` 為 blessed path；Tutorial 只示範 blessed path。
- **scale guard 與 soft constraint 彈性變數**：`TotalVarCount` 已含框架自動加的 surplus/deficit 變數，門檻判斷用最終值。
- **transaction 中 engine 尚未 solve**：`Write<T>` 前若 `Status != Optimal/Feasible` → 明確例外，不寫半套。
- **RFC4180 未閉合引號**：視為格式錯，`InvalidDataException` 指出行號。

## Non-Functional Requirements

- **Performance**：驗證 O(Σ param 列數)，相對 solve 微不足道；全在 load-time fail-fast。CSV 解析單次掃描，不得比現況明顯變慢。
- **相容性**：既有 69 xUnit 全過；`.Load()` API 不變；未用新功能的專案行為不變（新增 additive，非 breaking）。
- **無 runtime reflection**：驗證 metadata 走 generator emit（compile-time），符合 `2026-07-16-io-interface.md` 顯式哲學。
- **Observability**：驗證失敗訊息含 parameter / 欄位 / 值 / 所屬 set；成功時 `Logging.Info` 載入摘要（set 成員數、param 列數、date range）。
- **框架唯讀天條**：改的是框架本體，MUST Core(+Cplex+Gurobi+Generators) 一起 rebuild、跑 test、更新 `Templates/dlls/`。
- **Oracle transaction 驗證範圍（誠實區分已測 / 未測）**：
  - **已測**：交易編排邏輯本身（ambient 狀態、巢狀參與外層只 begin/commit 一次、成功 commit、例外 rollback 後原樣 rethrow、內層操作共用同一連線且不被 dispose、交易結束後 ambient 清空）——此邏輯已下沉到 `DBCtrlBase.ExecuteInTransaction`，與 Oracle 無關，故可用**假 `IDbConnection`/`IDbTransaction`**（`DbCtrlBaseTransactionTests`）直接驅動真實作驗證，而非讓假物件自己模擬 commit/rollback。反向證明：把 `DBCtrlBase` 的 `tx.Rollback()` 註解掉會導致該測試失敗。
  - **也已測**：`OracleSolutionSink` 對 `IDbCtrl` 的 wiring（batch 內多變數型別包進單一 `ExecuteInTransaction`、失敗回滾、`ExecuteBatch` 而非逐列 `Execute`）——用假 `IDbCtrl`（`FakeDbCtrl`）驗證呼叫序，這一層本來就是 Oracle-agnostic 的介面依賴，適合假物件驗證。
  - **未測**：`OracleDBCtrl.CreateRawConnection` 對真實 `OracleConnection` 的行為、`ExecuteArrayBind` 對真實 Oracle 的 array-bind 型別綁定是否正確——**無可用測試 DB**，僅保證編譯與程式碼 review 層級的正確性，與 Gurobi 只保證 compile 同性質。

## Open Questions

- [ ] `ScaleWarnThreshold` 預設值取多少（變數數）？暫定 10,000,000，Tuning 時校準。
- [ ] `Numeric.SafeRatio` 的 `magnitudeCeiling` 預設（暫定 1e9）與 BigM 實務量級是否相符？
- [ ] `OracleSolutionSink` / `IDbCtrl` transaction 放 Core.IO 還是隨 Oracle 驅動在外部專案（現 `OracleDBCtrl` 的所在層決定）？
- [ ] `IDbCtrl` transaction 用 `ExecuteInTransaction(Action)` 還是顯式 `Begin/Commit/Rollback`？

## Implementation Plan

### Stub 階段（先做，approve 後）

- [ ] Core：`DataContext`（`RegisterSet/RegisterParam/ValidateData` 簽名 + `NotImplementedException`）、`DataValidator`、`DataValidationException`、`DataIssue`、`FullGridAttribute`、`Numeric.SafeRatio`（簽名 + TODO）、`OptData.Load`（簽名 + TODO）。
- [ ] Core：`ISolverConfig.ScaleWarnThreshold`、`EngineBase.Solve()/Build()` template + `PreSolveGuard()`（TODO）+ `SolveCore()/BuildCore()` abstract。
- [ ] Cplex/Gurobi：`OptEngine` rename `Solve→SolveCore`、`Build→BuildCore`（純機械，讓它 compile）。
- [ ] Core：`IDbCtrl` transaction 簽名、`ISolutionSink.BeginBatch` + `ISolutionBatch`、`OracleSolutionSink`（stub）、`CsvSolutionSink.BeginBatch`（no-op）。
- [ ] Generators：`AutoSetsGenerator` emit index-set metadata + `DataContext` 註冊碼（stub 先 emit 空/固定，跑得過 build）。
- [ ] `dotnet build` 全綠（結構連得起來，邏輯全 TODO）。

### 逐層實作（stub 過後、逐塊對照 AC）

- [ ] `CsvCtrl` RFC4180 `SplitLine` 改寫 + 對應測試。
- [ ] `Numeric.SafeRatio` 實作 + Tutorial `BigM` 改用。
- [ ] `EngineBase.PreSolveGuard` scale guard 實作。
- [ ] `AutoSetsGenerator` 真正 emit 每個 param 的 index-set metadata + 註冊。
- [ ] `DataValidator` 四類檢查 + 聚合 + 摘要；`DataContext.ValidateData` 串接；`OptData.Load` 完成。
- [ ] `[FullGrid]` 完整性檢查串接。
- [ ] `IDbCtrl` transaction + `OracleSolutionSink` 原子寫入。
- [ ] `Templates/Tutorial` 改 `DataContext` + blessed path；跑 `dotnet run` 對照解。
- [ ] 補 xUnit：dangling / duplicate / missing-cell / numeric / CSV quoting / SafeRatio / transaction rollback。
- [ ] Core(+Cplex+Gurobi+Generators) rebuild → 更新 `Templates/dlls/`。

## References

- 前置定案：`specs/2026-07-16-io-interface.md`（顯式哲學、DB query-only、set 命名）
- 框架手冊：`specs/developer-guide.md`
- 相關 memory：`optimfoundation-dataload-explicit-design`（2026-07-18 refine：機械邏輯集中框架、LOAD 顯式）
- 天條：`$FW/MILP Model/CLAUDE.md`（框架唯讀 → rebuild + 更新 dlls/）
