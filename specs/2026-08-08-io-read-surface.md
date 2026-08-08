---
title: IO 讀取面收斂 — 只保留「讀 set」與「讀 param」兩件事
status: draft
created: 2026-08-08
updated: 2026-08-08
modules: [core-data, io-csv, io-db]
---

# IO 讀取面收斂

## Summary

框架目前有 **21 個讀取入口**散在六個型別上，其中大多數沒有任何生產呼叫者。本規格把它收斂成**兩件事**：

```csharp
IEnumerable<string[]> LoadRows(string name);            // 讀 set（也是所有讀取的底層原語）
List<TParam> LoadParam<TParam>(string file = null);     // 讀 param
```

其餘全部刪除——不標 `[Obsolete]`、不留過渡期。各來源（CSV / InMemory / DB）只需實作 `LoadRows`，`LoadParam` 建在它之上共用。

`LoadRows` 回**多欄**（`string[]`），所以本規格同時把「讀多欄 set」的地基鋪好，多維 Set 規格接續只需處理宣告層與變數層。

## Motivation / Why

### 一、21 個入口，7 個是死碼、6 個只有測試在用

| 型別 | 讀取方法 | 生產呼叫者 |
| --- | --- | --- |
| `CsvCtrl` | `ReadIntSet` / `ReadDoubleSet` / `ReadDateSet` | **0** |
| | `ReadParameter` | **0**（僅測試） |
| | `CreateParamTable` / `ClearData` | **0** |
| | `ReadTable` | 1（FJSP import） |
| | `ReadMatrixCsv` | 1（Sudoku import） |
| | `ReadStrSet` / `BuildParameter` | 有（收成內部實作） |
| `OracleDBCtrl` | `ReadStrSet` / `ReadDoubleSet` / `ReadIntSet` / `ReadDateSet` / `ReadSet` | **0**（整串死碼） |
| | `CreateParamTable` / `CreateResultTable` | **0** |
| `SetBase` | `LoadCsv` / `LoadInline` | **0**（僅測試） |
| `CsvDataSource` / `DbDataSource` | `LoadTable` | **0**（僅測試） |
| `IDataSource` | `LoadSet` / `LoadParam` | 有 |

`OracleDBCtrl.ReadSet(columnName, tableName)` 還會組出 `SELECT DISTINCT {col} FROM {table}` —— 表名慣例的 magic，與「DB 只能明寫 query SQL」的既定設計相衝，正好一併移除。

### 二、為什麼會長這麼多

沒有共同原語，所以三個軸每組合一次就長一個方法：

```text
來源（CSV / DB / InMemory） × 形狀（Set / Param / Table / Matrix） × 型別（string / int / double / DateTime）
```

`ReadIntSet` / `ReadDoubleSet` / `ReadDateSet` 就是第三軸展開的產物——但 `SetBase.ParseElement` 早就在做同一件轉型，它們一出生就是多餘的，`OracleDBCtrl` 還把這三個名字複製了一遍。

### 三、每個方法各自是一條路，導致行為不一致

因為沒有共用實作，同一件事各行其是。實測出三個不對稱：

| # | Set 路徑 | Parameter 路徑 | 後果 |
| --- | --- | --- | --- |
| 1 | 不 Trim | 每欄 `.Trim()` | Set 檔尾多一個空白 → 該成員所有 Parameter 列報 `Dangling`，肉眼看兩邊一模一樣 |
| 2 | 不跳空行 | 跳空行 | Set 檔中間空行 → 多出成員 `""` 或 `FormatException` |
| 3 | `InvariantCulture` | `Convert.ChangeType`（CurrentCulture） | 日期 / 小數在某些 locale 兩邊解出不同值 |

收斂成一條路之後，這三個不對稱不修也會消失——反而要刻意寫分支才能保持不一致。

### 四、`CsvCtrl` 同時是格式層與位址層

做 RFC4180 解析（格式），又寫死 `FolderDir.Data.GetFilePath()`（位址）。後果：讀不了 `Data/` 以外的檔、CSV 解析無法純字串單測、`CsvDataSource` 被架空成 86 行純轉呼叫。

## Scope

### In Scope

- `IDataSource` 收斂成兩個方法：`LoadRows(name)`（多欄）與 `LoadParam<T>(file)`
- 各來源只實作 `LoadRows`；`LoadParam` 為共用實作（`DataSourceBase` 或 default implementation）
- `SetBase` 從四個入口收成兩個：`Load(source, name)`（讀）與 `LoadFrom(IEnumerable<T>)`（注入已有資料）
- `CsvCtrl` 讀取面收成**單一函式** `ParseCsv(TextReader) → IEnumerable<string[]>`（純格式，不認得 set / param / 維度），位址層還給 `CsvDataSource`
- 新增**對位層**：`string[]` → 型別化的值（表頭按名對位、型別轉換），Set 與 Parameter 共用，統一 `InvariantCulture`
- `CsvCtrl.BuildParameter` 與 `DbDataSource.MapRows` 的**重複對位邏輯合併成一份**
- **三個不對稱一次消掉**：Trim / 空行 / culture
- **表頭判定顯式化**：第一列欄名對得上宣告的 property 名 → 表頭；對不上 → 當資料列按序對位。取代「最後一欄 parse 不成 double 就當表頭」的啟發式
- **刪除清單**（見 Data Model 段）全部直接移除，不標 `[Obsolete]`
- 兩處 import 路徑遷移到 `LoadRows`（FJSP / Sudoku）
- 測試遷移：`LoadInline` → `LoadFrom`、`LoadCsv` → `Load(source, name)`；死碼相關測試刪除

### Out of Scope

- **多維 Set**（`[OptSet<T1,T2>]`、tuple 成員、`BuildVars` 攤平、同檔共讀）→ 接續的 `2026-08-08-multidim-set.md`
- 寫出面（`WriteSolution` / `WriteSet` / `WriteParam`）不動
- `DbDataSource` 實作 `IDataSource`（位址是 SQL 而非名稱，維持現況的兩種來源模型）
- `OracleDBCtrl` 移出 Core、Oracle driver 相依
- 檔案切分（`CsvSolutionSink` / `OracleSolutionSink` 各自獨立檔）
- `DataValidator` / `DataContext` / `EngineBase` / solver engine

## User Stories / Use Cases

1. As a 框架維護者, I want to 讀取只有一條路, so that 改一次行為就全部一致，不必追七個方法。
2. As a 建模者, I want to `Set_*.csv` 尾巴多空白或中間有空行時仍正確載入, so that 不必為看不見的字元 debug `Dangling`。
3. As a 框架維護者, I want to CSV 解析能用純字串單測, so that 測試不必真的寫檔。
4. As a 新進者, I want to 打 `source.` 只看到兩個讀取方法, so that 不必猜該用 `LoadSet` 還是 `LoadCsv` 還是 `ReadStrSet`。

## Acceptance Criteria

### 結構指標

- [ ] public 讀取入口從 **21 降到 2**（`LoadRows` / `LoadParam`）
- [ ] `SetBase` 的 public 載入方法從 4 降到 2（`Load` / `LoadFrom`）
- [ ] 各 `IDataSource` 實作只需寫 `LoadRows` 一個方法
- [ ] 「字串 → 型別」轉換與表頭對位只出現在**一個**地方（`CsvCtrl.BuildParameter` 與 `DbDataSource.MapRows` 兩份合一）
- [ ] `CsvCtrl` **讀取面只剩 `ParseCsv` 一個函式**；public 方法從 13 降到 4（1 讀 + 3 寫）
- [ ] `CsvCtrl` 的 CSV 解析可用 `TextReader` 純字串單測，不需寫檔

### 刪除

- [ ] Data Model 段的刪除清單全部移除，且 `dotnet build` 全綠
- [ ] 移除後全 repo 搜不到對它們的引用（含 Templates、AI-Modeling/Projects）

### 行為變更（各需一條測試證明）

- [ ] Set CSV 每欄 Trim（與 Parameter 一致）
- [ ] Set CSV 跳過空白行（與 Parameter 一致）
- [ ] Set 與 Parameter 的數值 / 日期解析統一 `InvariantCulture`
- [ ] 表頭判定改為顯式（欄名對得上才算表頭）

### 相容性

- [ ] `set.Load(source)` / `source.LoadParam<T>()` 兩種消費端句型的簽名與行為不變
- [ ] 四個 Templates 的**解值全部相同**
- [ ] Tutorial / RosteringProblem **一行不改**
- [ ] FJSP / Sudoku 的 **import 路徑各改一處**（`ReadTable` / `ReadMatrixCsv` → `LoadRows`），求解路徑不動
- [ ] 既有 69 個 xUnit 遷移後全過（`LoadInline` → `LoadFrom` 等）

## Module Interactions

| # | 檔案 | 改什麼 |
| --- | --- | --- |
| 1 | `IO/csv/IDataSource.cs` | 收成兩個方法：`LoadRows` / `LoadParam`；`LoadSet` 移除 |
| 2 | `IO/csv/CsvCtrl.cs` | 讀取面收成單一 `ParseCsv(TextReader)`；刪除九個方法（含 `ReadStrSet`）；`BuildParameter` 搬到對位層。寫出面三個方法不動 |
| 3 | `IO/csv/CsvDataSource.cs` | 只實作 `LoadRows`（位址解析）；`LoadTable` 移除 |
| 4 | `IO/csv/InMemoryDataSource.cs` | 只實作 `LoadRows`；`AddSet` 改收多欄 |
| 5 | `IO/db/DbDataSource.cs` | `LoadRows(sql)` 取前 n 欄（現在 `LoadSet` 寫死 `ItemArray[0]`）；`LoadTable` 移除 |
| 6 | `IO/db/OracleDBCtrl.cs` | 刪除七個死方法（含表名慣例的 `ReadSet`） |
| 7 | `DesignBases.cs`（`SetBase<T>`） | 四入口收成兩個；載入邏輯集中到單一內部點 |
| 8 | 新增共用層 | 「字串 → 型別」轉換 + 表頭對位，Set 與 Parameter 共用 |
| 9 | `Templates/FJSP_BASIC_BRICK/Data/Dataload.cs:64` | `CsvCtrl.ReadTable` → `LoadRows` |
| 10 | `Templates/Sudoku_SHC279/Data/Dataload.cs:38` | `CsvCtrl.ReadMatrixCsv` → `LoadRows`，矩陣攤平自己做 |
| 11 | 測試（6 檔） | `LoadInline` → `LoadFrom`、`LoadCsv` → `Load`；死碼測試刪除 |

**不動**：`DataValidator`、`DataContext`、`EngineBase`、`VariableBuilder`、generator、Cplex / Gurobi engine、寫出面。

## API Design

### 收斂後的介面

```csharp
public interface IDataSource
{
    /// <summary>讀 set：一張純 index 的多欄表。也是所有讀取的底層原語。</summary>
    IEnumerable<string[]> LoadRows(string name);

    /// <summary>讀 param：一張帶值欄的表，按 property 名對位成物件。</summary>
    List<TParamClass> LoadParam<TParamClass>(string file = null) where TParamClass : ModelElementBase, new();
}
```

各來源只需實作 `LoadRows`；`LoadParam` 是建在它上面的共用實作：

```text
CsvDataSource.LoadRows("Set_Item")   → 解析 Data/Set_Item.csv
InMemoryDataSource.LoadRows("Item")  → 取註冊表
DbDataSource.LoadRows(sql)           → 跑 query 取前 n 欄
```

### 積木端

```csharp
set_Item.Load(source);                        // 讀（走 LoadRows）
set_Item.LoadFrom(generatedSequence);         // 注入已有資料（import / 程式生成 / 測試）
parameter_Demand = source.LoadParam<Parameter_Demand>();
```

`LoadFrom` 不是「讀」而是「填」——import 模式把不規則來源攤平後要餵回積木，測試也需要。它不經來源、不做字串轉型，因此不算第三條讀取路徑。

### import 路徑改用同一個原語

不規則的原始檔本來就是「一張任意多欄的表」，`LoadRows` 直接服務它，不需要 `ReadTable` / `ReadMatrixCsv` 這種客製方法：

```csharp
// Sudoku：原本 CsvCtrl.ReadMatrixCsv(rawFile) 回 double[,]
var rows = source.LoadRows(rawFile);
// 攤平成 (Row, Col, Digit) 三欄，自己做——這是 import 的職責，不是框架的
```

### 三層職責

```text
CsvCtrl.ParseCsv       文字 → string[] 的列        不知道 set / param，也不知道維度
      ↓
對位層（新增，共用）    string[] → 型別化的值       知道哪幾欄是 index、哪幾欄是值
      ↓
SetBase / Parameter    型別值 → 積木               知道自己是幾維
```

```csharp
// 格式層（internal，可純字串單測）
internal static IEnumerable<string[]> ParseCsv(TextReader reader);

// 位址層（CsvDataSource）
public IEnumerable<string[]> LoadRows(string name)
    => CsvCtrl.ParseCsv(File.OpenText(FolderDir.Data.GetFilePath(EnsureCsv(name))));
```

維度是**最下層**的事。多維 Set 上線時，只有 `SetBase` 那層要改——`ParseCsv` 與對位層都不動。

## Data Model

### 刪除清單（全部零生產呼叫者，直接移除）

| 型別 | 方法 |
| --- | --- |
| `CsvCtrl` | `ReadIntSet` / `ReadDoubleSet` / `ReadDateSet` / `ReadParameter` / `CreateParamTable` / `ClearData` / `ReadTable` / `ReadMatrixCsv` / `ReadStrSet` |
| `OracleDBCtrl` | `ReadStrSet` / `ReadDoubleSet` / `ReadIntSet` / `ReadDateSet` / `ReadSet` / `CreateParamTable` / `CreateResultTable` |
| `SetBase` | `LoadCsv` / `LoadInline` |
| `CsvDataSource` | `LoadTable` |
| `DbDataSource` | `LoadTable` |
| `IDataSource` | `LoadSet`（由 `LoadRows` 取代） |

共 **21 個方法**。`ReadTable` / `ReadMatrixCsv` 各有一個 import 呼叫者，遷移到 `LoadRows` 後移除。

**搬移（不是刪除）**：`CsvCtrl.BuildParameter` 與 `DbDataSource.MapRows`（private）做的是同一件事——表頭按名對位、大小寫不敏感、多餘欄忽略、`InitClassBySets` 轉型。兩份合併搬到對位層。這也是 DB 路徑的 culture 行為與 CSV 不一致的原因：它們從來沒共用過。

### `CsvCtrl` 收斂後的完整面貌

| 方向 | 方法 | 說明 |
| --- | --- | --- |
| 讀 | `ParseCsv(TextReader)` | **唯一**。internal，可純字串單測 |
| 寫 | `WriteSolution<TVariable>` | 寫解 |
| 寫 | `WriteSet(ISetBrick, fileName)` | 寫 set（import 用） |
| 寫 | `WriteParam<TParameter>(rows, fileName)` | 寫 param |

13 個 public 方法收成 4 個。**`CsvCtrl` 不認得 set / param，也不認得維度**——它只做「文字 ↔ 多欄列」的格式轉換，所以多維 Set 上線時它的讀取端一行都不用改。

### 檔案格式（不變）

```text
Data/Set_Item.csv          單欄、無表頭
ItemA
ItemB
```

```text
Data/Parameter_Demand.csv  帶表頭、按名對位
ITEM,DATE,QTY
ItemA,2026-01-01,60
```

- 沿用既有 RFC4180 解析（引號包住可含逗號、`""` 跳脫、不支援欄位內換行）
- **表頭判定改為顯式**：第一列欄名對得上 property 名才算表頭
- 多餘欄忽略（`DATA_ID` / `VAR_TYPE` / `USER`）
- Trim、跳空行、`InvariantCulture` 對 set 與 param 一致

## Edge Cases & Error Handling

- **Trim 後成員變空字串**：視為空行處理（跳過），set 與 param 一致
- **Set 檔多欄**：本規格 `LoadRows` 已回多欄，但 arity 1 的 `SetBase<T>` 只取第一欄；欄數 > 1 時的處置留給多維 Set 規格
- **表頭判定失敗**：欄名對不上就當資料列 → 後續型別轉換會丟例外，訊息要指出「疑似表頭但欄名對不上，期望：…，實際：…」
- **`LoadRows` 串流**：回 `IEnumerable` 而非 `List`，不要求全表進記憶體；但 `SetBase.LoadFrom` 的重複檢查會逐筆消費
- **DB `LoadRows` 的欄數**：取前 n 欄，n 由消費端決定；欄數不足 → 例外含 SQL 與實際欄數
- **刪除造成的 compile error**：全 repo 搜過確認零生產呼叫者，若 build 出現非預期引用 → 停下來評估，NEVER 為了通過 build 而留下相容 shim

## Non-Functional Requirements

- **相容性**：消費端兩種句型（`set.Load(source)` / `source.LoadParam<T>()`）簽名與行為不變；被刪的方法全 repo 零生產呼叫者，屬移除死碼而非破壞 API
- **Performance**：載入路徑不得變慢；`LoadRows` 串流讀取
- **可測試性**：`ParseCsv` 可純字串單測列入驗收
- **Observability**：載入摘要格式不變

## Open Questions

- [ ] `LoadParam` 的共用實作放哪：`IDataSource` 的 default implementation，還是新增 `DataSourceBase` 抽象類別？（傾向 default implementation，少一層繼承）
- [ ] `InMemoryDataSource.AddSet` 改收多欄後，既有的 `IEnumerable<string>` overload 保不保留（傾向保留，一行轉換）
- [ ] `SetNaming` 只服務 CSV / InMemory，DB 不經它——收斂後要不要一併簡化（傾向不動，另一輪）

## Implementation Plan

### Stub 階段（先做）

- [ ] `IDataSource` 改成兩個方法的形狀 + `LoadParam` 共用實作骨架（TODO）
- [ ] `CsvCtrl.ParseCsv(TextReader)` 簽名 + TODO
- [ ] `SetBase` 兩入口 + 單一內部載入點的簽名 + TODO
- [ ] 共用「字串 → 型別 + 表頭對位」層的簽名 + TODO
- [ ] 三個來源的 `LoadRows` 簽名 + TODO
- [ ] `dotnet build` 全綠

### 逐層實作

- [ ] 共用轉換層：字串 → 型別、表頭顯式判定、Trim / 空行 / culture 統一
- [ ] `CsvCtrl`：`ParseCsv` 實作、位址層分離
- [ ] 三個 `IDataSource` 實作收斂成 `LoadRows`
- [ ] `SetBase`：四入口收成兩個
- [ ] **執行刪除清單**（20 個方法）
- [ ] 遷移 FJSP / Sudoku 的 import 路徑
- [ ] 遷移測試（`LoadInline` → `LoadFrom`、`LoadCsv` → `Load`），刪除死碼測試
- [ ] 新增測試：`ParseCsv` 純字串、三項行為變更、`LoadRows` 三來源
- [ ] 四個 Templates 回歸（解值相同）
- [ ] `developer-guide.md` 更新讀取層章節

## References

- 接續規格：`2026-08-08-multidim-set.md`（多維 Set — 本規格的 `LoadRows` 已鋪好多欄地基）
- 受影響的既有規格：`framework-dev-spec.md`（資料防護規格）、`cplex-project-dev-spec.md`
- 設計原則：無值多維 = Set、有值多維 = Parameter；讀取只有兩件事
- 後續可能的規格：`DbDataSource` 實作 `IDataSource`、`OracleDBCtrl` 移出 Core、檔案切分、`SetNaming` 簡化
