---
title: 資料讀取層收斂 + 多維 Set — 單一多欄表原語，BuildVars 以稀疏定義域建變數
status: draft
created: 2026-08-08
updated: 2026-08-08
modules: [core-data, core-variable, generator, io-csv, io-db]
---

# 資料讀取層收斂 + 多維 Set

## Summary

把框架讀取模型資料的方式收斂成**單一原語**——「給我這張表的列」（`IEnumerable<string[]>`），Set 與 Parameter 都消費它，差別只在**有沒有值欄**。在這個統一的讀取層上加多分量支援，`[OptSet<T1,T2>("NodeFrom","NodeTo")]` 就成立，`BuildVars` 也就能把一顆多維 Set 當**一個維度**展開，只建實際存在的組合。

分類原則（本規格的組織骨幹）：

> **無值的多維資料 = Set 的工作；有值的多維資料 = Parameter 的工作。**
> 兩者是同一種形狀的資料（多欄表），Set 是 Parameter 的退化情形。

**變數宣告端零改動**：`VariableX_Flow` 照舊用兩個 `[OptDim<Set_Node>(…)]`，變的只是餵給 `BuildVars` 的東西。因此變數 key 格式、`ReadVar`、`WriteSolution` 全部不動。

## Motivation / Why

### 一、讀取架構分裂成兩套機制

```text
Set:        set_Item.Load(source)           ← 積木自己去拉（pull）
Parameter:  source.LoadParam<Parameter_X>() ← 來源把資料推過來（push）
```

方向相反、程式碼不共用，導致同一件事（字串 → 型別）散在三處各寫各的（`SetBase.ParseElement`、`ModelElementBase.InitClassBySets`、`CsvCtrl.BuildParameter`）。實測出三個不對稱：

| # | Set 路徑 | Parameter 路徑 | 後果 |
| --- | --- | --- | --- |
| 1 | 不 Trim | 每欄 `.Trim()` | Set 檔尾多一個空白 → 該成員所有 Parameter 列報 `Dangling`，肉眼看兩邊一模一樣 |
| 2 | 不跳空行 | 跳空行 | Set 檔中間空行 → 多出成員 `""` 或 `FormatException` |
| 3 | `InvariantCulture` | `Convert.ChangeType`（CurrentCulture） | 日期 / 小數在某些 locale 兩邊解出不同值 |

### 二、`SetBase` 四個載入入口，一個繞過抽象層

`Load(IDataSource)` / `LoadCsv(fileName)` / `LoadInline` / `LoadFrom`。其中 `LoadCsv` 直接叫 `CsvCtrl`，**繞過 `IDataSource`**——「換來源不動模型 code」在 set 這側是假的。規範已把消費端收斂到 2 個入口（`LoadCsv` / `LoadInline` 列在禁用黑名單），但**架構留了四個洞，靠文字規範去堵**。

### 三、`CsvCtrl` 同時是格式層與位址層

做 RFC4180 解析（格式），又寫死 `FolderDir.Data.GetFilePath()`（位址）。後果：讀不了 `Data/` 以外的檔、CSV 解析無法純字串單測、`CsvDataSource` 被架空成 86 行純轉呼叫。

### 四、變數爆炸

`BuildVars` 只會笛卡兒展開。FJSP 的 `VariableB_Precede`（Lot × Op × Lot × Op）在 100 lots × 10 ops 是 10^6 顆，合法的可能不到 5%。多出來的變數若沒被任何式子引用，solver presolve 會清掉——**成本在框架端**：每顆變數一個字串 key 加 dictionary entry 的記憶體與 build 時間。

### 五、模型與程式對不起來

教科書、AMPL、GAMS、OPL、Pyomo 一致把 arc 寫成 set（`∑_{(i,j)∈A}`）。框架沒有稀疏 set，只能降級成 `Parameter(HasValue=false)` 名單——Model.md 上是 SET、程式裡是 PARAM，違反「程式類別名直接對應 Model.md 符號」的天條。

## Scope

### In Scope

> **前置規格**：`2026-08-08-io-read-surface.md` —— 讀取面已收斂成 `LoadRows`（多欄）與 `LoadParam` 兩件事，三個不對稱與表頭顯式判定也在那份完成。本規格接在它之上，只處理宣告層與變數層。

- `[OptSet<T1…Tn>(分量名…)]` arity 1–6；成員型別 = 具名 ValueTuple；arity 1 寫法與行為**完全不變**
- 多欄 Set CSV：專用檔無表頭按序、共讀檔帶表頭按分量名挑欄
- **同檔共讀**：同一份 CSV 可被 Set 與 Parameter 各取所需欄，arc 清單只存在一份
- `BuildVars<T>(多維Set)` 把該 Set 當**一個維度**展開；可與一維 Set 任意混用、任意順序
- `BuildVars<T>` 加 arity 防呆（攤平後分量總數 vs `typeof(T)` property 數）
- 所有載入 / 寫出路徑對齊多維（見「載入路徑對照」）

### Out of Scope

- **`IDataSource` 與 DB 的型別統一** —— DB 的差異降級成「位址是 SQL 而非名稱」，資料形狀已一致；要不要讓 `DbDataSource` 實作介面另開一輪（會動 `Dataload` 建構子簽名）
- `OracleDBCtrl` 移出 Core（動 csproj 相依與 `dlls/`）
- **Parameter 以多維 Set 為定義域** + `[FullGrid]` 的稀疏覆蓋語意
- **積木式泛型** `[OptSet<Set_Node, Set_Node>]`（分量綁來源 Set、可驗分量 dangling）
- `ISetBrick` 擴充（`Arity` / 分量型別列舉）—— 驗證器不需要看懂多維 Set
- nullable 值欄、DateTime 時分秒粒度、空集合禁止的放寬、`[OptSet]` 支援 enum
- `ParamLookup` / 缺格查詢 log
- LLMDevFramework 規範同步（Model Design 二維集合寫法、API guide、checklist）——等框架 ship 後另一輪
- Solver engine 改動、既有專案改寫

## User Stories / Use Cases

1. As a 建模者, I want to 把「哪些 arc 存在」宣告成一顆 Set, so that Model.md 的 `∀(i,j) ∈ ARC` 與程式的 `Set_Arc` 對得起來。
2. As a 建模者, I want to `BuildVars<VariableX_Flow>(arcs)` 只建實際存在的邊, so that 不必為 N² 顆變數付記憶體。
3. As a 建模者, I want to 把 `from,to,cost` 這張表**直接**餵給 Set 與 Parameter, so that arc 清單不必人工同步兩個檔。
4. As a 建模者, I want to `Set_*.csv` 尾巴多一個空白或中間有空行時仍正確載入, so that 不必為看不見的字元 debug `Dangling`。
5. As a 框架維護者, I want to 多維 Set 的分量解析只出現在一個地方, so that 之後加維度型別不必再改七處。

## Acceptance Criteria

### 相容性（最高優先）

- [ ] 既有 69 個 xUnit 全過
- [ ] 四個 Templates（Tutorial / RosteringProblem / Sudoku_SHC279 / FJSP_BASIC_BRICK）**一行不改**，build 過且**解值相同**
- [ ] 消費端 public API 不變：`set.Load(source)` / `source.LoadParam<T>()` 簽名與行為維持（除下方三項刻意變更）
- [ ] arity 1 的 `[OptSet<string>]` 宣告與行為完全不變

### 刻意的行為變更（各需一條測試證明）

- [ ] Set CSV 每欄 Trim（與 Parameter 一致）
- [ ] Set CSV 跳過空白行（與 Parameter 一致）
- [ ] Set 與 Parameter 的數值 / 日期解析統一 `InvariantCulture`
- [ ] 表頭判定改為顯式：第一列欄名對得上宣告的分量名 / property 名才算表頭；對不上就當資料列按序對位

### 讀取層收斂

- [ ] `IDataSource` 只有一個抽象成員 `LoadRows`；`LoadSet` / `LoadParam<T>` 為 default implementation
- [ ] `CsvCtrl` 的 CSV 解析可用純字串（`TextReader`）單測，不需寫檔
- [ ] `SetBase` 的四個 public 入口全部轉呼叫單一內部 `LoadRows`
- [ ] **結構指標**：多維 Set 的分量解析只出現在**一個**地方

### 多維 Set

- [ ] `[OptSet<string,string>("NodeFrom","NodeTo")]` 可編譯，generator 產出 `: SetBase<(string NodeFrom, string NodeTo)>`
- [ ] `foreach (var arc in arcs)` 可用 `arc.NodeFrom` / `arc.NodeTo` 具名存取
- [ ] 專用多欄檔（無表頭）欄數與 arity 不符 → 例外（含檔名、行號、期望/實際欄數）
- [ ] **同檔共讀**：`set_Arc.Load(source, "ArcCost")` 與 `source.LoadParam<Parameter_ArcCost>("ArcCost")` 讀同一份帶表頭 CSV，各取所需欄
- [ ] 成員唯一性以**整組 tuple** 判定：`(N1,N2)` 與 `(N2,N1)` 不同；`(N1,N2)` 重複 → 例外
- [ ] 四道防呆對多維成員成立（未載入 / 空 / 二次載入 / 重複）
- [ ] `BuildVars<T>(arcs)` 建出的變數數 **= arcs 成員數**（非笛卡兒積）
- [ ] `BuildVars<T>(arcs, dates)` 建出 `|arcs| × |dates|` 顆；順序互換亦正確
- [ ] 變數 key 格式**不變**：分量攤平為 `VariableX_Flow@N1@N3@2026-01-01`
- [ ] **arity 防呆**：攤平後分量總數 ≠ `typeof(T)` property 數 → 當場丟例外（列出兩邊數字與各維度貢獻幾個分量）
- [ ] 分量值含 `@` → 載入期例外，訊息指出第幾個分量
- [ ] **七條載入 / 寫出路徑各有測試**：`Load` / `LoadCsv` / `LoadInline` / `LoadFrom` / DB / `InMemoryDataSource.AddSet` / `WriteSet`
- [ ] `WriteSet` 寫出的多欄檔可被 `LoadCsv` 原樣讀回（round-trip），供 import 模式使用

### Benchmark

- [ ] FJSP 的 `VariableB_Precede` 改用多維 Set 後，記錄改造前後的變數數、build 時間、peak memory

## Module Interactions

| # | 檔案 | 改什麼 |
| --- | --- | --- |
| 1 | `IO/csv/IDataSource.cs` | 收斂成單一抽象成員 `LoadRows`；`LoadSet` / `LoadParam<T>` 改 default implementation |
| 2 | `IO/csv/CsvCtrl.cs` | 抽出格式層 `ParseCsv(TextReader)`；位址層方法保留原簽名轉呼叫；`WriteSet` 支援多欄 |
| 3 | `IO/csv/CsvDataSource.cs` | 只實作 `LoadRows`（位址解析）；`CsvSolutionSink` 搬出獨立檔 |
| 4 | `IO/csv/InMemoryDataSource.cs` | 只實作 `LoadRows`；`AddSet` 加多欄 overload |
| 5 | `IO/db/DbDataSource.cs` | `LoadRows(sql)` 取前 n 欄（現在 `LoadSet` 寫死 `ItemArray[0]`） |
| 6 | `DesignBases.cs`（`SetBase<T>`） | 四個入口轉呼叫單一內部 `LoadRows`；多分量解析只加在這一點 |
| 7 | `AutoSetsGenerator.cs` | 注入 arity 2–6 的 `OptSetAttribute<…>`；OptSet 分支的 base class 改成 `SetBase<(T1 n1, T2 n2)>`（字串拼接，不 emit 額外型別） |
| 8 | `VariableBuilder.cs` | `ConvertSetsToStringLists` 加 ValueTuple 分支——一個維度貢獻 n 個 key 分量；`GenVarParts` 攤平；arity 防呆 |
| 9 | 新增共用層 | 「字串 → 型別」轉換 + 表頭對位，Set 與 Parameter 共用 |

**不動**：`DataValidator`、`DataContext`、`EngineBase`、Cplex / Gurobi engine、generator 的 Var / Param 路徑、`IDbCtrl` / `DBCtrlBase` / `OracleDBCtrl` 的 DB 存取邏輯。

## API Design

### 讀取原語（框架內部，消費端看不到）

```csharp
public interface IDataSource
{
    /// <summary>唯一的讀取原語：給我這張表的列。位址怎麼算是各來源自己的事。</summary>
    IEnumerable<string[]> LoadRows(string name);

    // 以下皆建立在 LoadRows 之上（default implementation），消費端 API 不變
    List<string> LoadSet(string name);
    List<TParamClass> LoadParam<TParamClass>(string file = null) where TParamClass : ModelElementBase, new();
}
```

走一遍最小成本流的例子。輸入檔只有兩份：

```text
Data/Set_Node.csv          ← 專用 Set 檔：單欄、無表頭
N1
N2
N3
N4
```

```text
Data/ArcCost.csv           ← 共讀檔：多欄、帶表頭
NODEFROM,NODETO,QTY
N1,N2,4.5
N1,N3,7.0
N2,N4,3.0
N3,N4,2.5
```

同一個原語讀這兩份檔，消費者各取所需：

```text
LoadRows("Set_Node")
→ [["N1"], ["N2"], ["N3"], ["N4"]]
→ Set_Node（1 欄，全 index）

LoadRows("ArcCost")
→ [["N1","N2","4.5"], ["N1","N3","7.0"], ["N2","N4","3.0"], ["N3","N4","2.5"]]
→ Set_Arc（依分量名取 NODEFROM / NODETO 兩欄）
→ Parameter_ArcCost（依 property 名取三欄，QTY 是值欄）
```

### 宣告（消費端）

```csharp
// Set/Set_Node.cs — 對應 Data/Set_Node.csv（單欄無表頭）
[OptSet<string>]
public sealed partial class Set_Node { }

// Set/Set_Arc.cs — 對應 Data/ArcCost.csv 的前兩欄（依分量名對表頭）
[OptSet<string, string>("NodeFrom", "NodeTo")]
public sealed partial class Set_Arc { }
```

generator 產出：

```csharp
public partial class Set_Arc : SetBase<(string NodeFrom, string NodeTo)> { }
```

Parameter 的宣告不變，property 由 `[OptDim]` 決定，值欄固定 `QTY`：

```csharp
// Parameter/Parameter_ArcCost.cs — 對應 Data/ArcCost.csv 全部三欄
[OptParam]
[OptDim<Set_Node>("NodeFrom")]
[OptDim<Set_Node>("NodeTo")]
public sealed partial class Parameter_ArcCost { }
```

```text
Data/ArcCost.csv 的欄 → 誰讀走
NODEFROM  → Set_Arc.NodeFrom  /  Parameter_ArcCost.NodeFrom
NODETO    → Set_Arc.NodeTo    /  Parameter_ArcCost.NodeTo
QTY       → Parameter_ArcCost.QTY（Set 不讀）
```

變數宣告**完全不變**（今天就支援）：

```csharp
[OptVar]
[OptDim<Set_Node>("NodeFrom")]
[OptDim<Set_Node>("NodeTo")]
public sealed partial class VariableX_Flow { }
```

### 同檔共讀

```csharp
public Dataload(IDataSource source)
{
    set_Node.Load(source);
    set_Arc.Load(source, "ArcCost");
    parameter_ArcCost = source.LoadParam<Parameter_ArcCost>("ArcCost");
}
```

兩行都顯式指名 `"ArcCost"`，沒有推導、沒有慣例——**arc 清單只存在一份**。載入結果：

```text
set_Node          4 個成員：N1, N2, N3, N4
set_Arc           4 個成員：(N1,N2), (N1,N3), (N2,N4), (N3,N4)
parameter_ArcCost 4 列：QTY = 4.5, 7.0, 3.0, 2.5
```

對照現況（沒有同檔共讀時）必須維護兩份檔，而且 arc 清單重複：

```text
Data/Set_Arc.csv       Data/Parameter_ArcCost.csv
N1,N2                  NODEFROM,NODETO,QTY
N1,N3                  N1,N2,4.5
N2,N4                  N1,N3,7.0
N3,N4                  N2,N4,3.0
                       N3,N4,2.5
```

漏改其中一份就是 `Dangling`（或反過來，Set 多一列但 Parameter 沒有 → 該 arc 的成本靜默變 0）。專用檔寫法仍然合法，共讀只是多一個選項。

### 使用

```csharp
engine.BuildVars<VariableX_Flow>(data.set_Arc);
engine.BuildVars<VariableX_Flow>(data.set_Arc, data.set_Date);

foreach (var arc in arcs)
{
    engine.AddLHS(1.0, new VariableX_Flow { NodeFrom = arc.NodeFrom, NodeTo = arc.NodeTo });
}
```

上面第一行建出的變數（**4 顆，不是 4 × 4 = 16 顆**）：

```text
VariableX_Flow@N1@N2
VariableX_Flow@N1@N3
VariableX_Flow@N2@N4
VariableX_Flow@N3@N4
```

### 載入路徑對照（每條都要對齊）

| 路徑 | 現況 | 多維做法 |
| --- | --- | --- |
| `LoadFrom(IEnumerable<T>)` | 泛型 | **零改動**，T = tuple 自動可用 |
| `LoadInline(params T[])` | 泛型 | **零改動**，`LoadInline(("N1","N2"), …)` |
| `Load(IDataSource, name)` | `LoadSet` 回 `List<string>` | 走 `LoadRows`，內部單一解析點 |
| `LoadCsv(fileName)` | 直接叫 `CsvCtrl`（繞過抽象） | 改走 `LoadRows`；標 `[Obsolete]` |
| DB（`LoadFrom(db.LoadSet(sql))`） | 寫死取 `ItemArray[0]` | `DbDataSource.LoadRows(sql)` 取前 n 欄 |
| `InMemoryDataSource.AddSet` | `IEnumerable<string>` | 加 `IEnumerable<string[]>` overload |
| `CsvCtrl.WriteSet` | 單欄寫出 | 多欄寫出（與讀取對稱；import 模式需要） |

**免費拿到、不需改的**：四道防呆與 `Contains` / `ContainsObject`——內部 `HashSet<T>` 對 ValueTuple 的 structural equality 自動成立。

### 新診斷碼

| Code | 觸發 | 訊息要點 |
| --- | --- | --- |
| OPTF007 | arity ≥ 2 但分量名數量不符 | 期望 n 個分量名，實際 m 個 |
| OPTF008 | 分量型別不在合法域 | 合法：string / DateTime / int / long / double / decimal |

## Data Model

CSV 的具體長相見 API Design 的走查；本節是格式規則。

### 檔案形態只有兩種

| 形態 | 表頭 | 對位方式 | 誰讀 | 何時用 |
| --- | --- | --- | --- | --- |
| 專用 Set 檔 | 無 | 按序（欄數 MUST = arity） | 只有 Set | 這份名單不帶任何值 |
| 帶表頭檔 | 有 | 按名（Set 用分量名、Param 用 property 名） | Set 與 / 或 Parameter | 帶值，或要被共讀 |

- **表頭判定是顯式的**：第一列的欄名對得上宣告的分量名 / property 名 → 表頭；對不上 → 當資料列按序對位。取代現行「最後一欄 parse 不成 double 就當表頭」的啟發式
- 共讀檔 MUST 帶表頭——沒有表頭 Set 無從得知該讀哪幾欄
- 按名對位大小寫不敏感；多餘欄忽略（`DATA_ID` / `VAR_TYPE` / `USER` 等）
- 沿用既有 RFC4180 解析（引號包住可含逗號、`""` 跳脫、不支援欄位內換行）
- Trim、跳空行、`InvariantCulture` 三項對兩種形態一致（本規格的行為變更）

### 三維以上

```text
Data/Set_LotOpEqp.csv        專用檔、無表頭、3 欄
L1,OP1,EQ1
L1,OP2,EQ1
L2,OP1,EQ2
```

```csharp
[OptSet<string, string, string>("Lot", "Operation", "Eqp")]
public sealed partial class Set_LotOpEqp { }
```

### 值欄的型別

值欄 MUST 是數值。非數值的「值」其實是另一個維度，應宣告成 Set 的分量：

```text
❌ Employee,Group          當成「值為 string 的 Parameter」
✅ Set_EmployeeGroup.csv   當成二維 Set（Employee, Group）
```

### 變數 key（格式不變）

```text
VariableX_Flow@N1@N3
VariableX_Flow@N1@N3@2026-01-01
```

## Edge Cases & Error Handling

- **對角線 `(N1,N1)`**：框架不表態，照資料收。要不要排除是建模決定
- **整組重複**：整組相同才算重複成員
- **空 Set**：沿用現有防呆（載入後為空 → 例外）
- **單維與多維混用**：笛卡兒積只發生在**維度參數之間**，多維 Set 內部不展開。`BuildVars<T>(arcs, dates)` = `|arcs| × |dates|`
- **多維 Set 相鄰時的順序陷阱**：光看參數個數看不出 property 該有幾個——靠 arity 防呆擋
- **`BuildVars` 順序**：key 分量順序 = 傳入順序攤平後的順序，必須與 `[OptDim]` 宣告順序一致（沿用現有天條，測試需覆蓋）
- **共讀檔缺表頭**：Set 無從得知該讀哪幾欄 → 明確例外，訊息指出「共讀需要表頭」
- **共讀檔表頭有分量名但欄序與宣告不同**：按名對位，順序無所謂
- **Trim 後成員變空字串**：視為空行處理（跳過），與 Parameter 一致
- **多維 Set 被 Parameter 引用**：本次不支援；`ContainsObject` 拿 string 比 tuple 一律回 false，會整份報 `Dangling` → 加診斷碼擋掉（見 Open Questions）
- **`LoadInline` / `LoadCsv` 標 `[Obsolete]`**：warning 不是 error，既有 code 不會壞

## Non-Functional Requirements

- **相容性（最高優先）**：消費端 public API 不變；變數 key 格式不變；`IDataSource` 的收斂靠 default implementation，既有實作不破
- **Performance**：arity 1 的 `BuildVars` 與載入路徑不得變慢；多維 Set 成員查找維持 O(1)；`LoadRows` 用 `IEnumerable` 串流，不要求全表進記憶體
- **記憶體**：本規格的核心收益——以 FJSP 記錄改造前後的變數數與 peak memory
- **可測試性**：CSV 解析可純字串單測（不寫檔）列入驗收
- **Observability**：載入摘要顯示維度數（例：`Arc: 20 個成員（2 維）`）

## Open Questions

- [ ] Parameter 若引用多維 Set，加診斷碼擋掉還是留著不管？（傾向擋掉，錯誤左移）
- [ ] `LoadCsv` / `LoadInline` 標 `[Obsolete]` 是否這次就做（規範已列黑名單）
- [ ] arity 上限取 6（對齊現有 `OptParam` / `OptVar`）是否足夠
- [ ] `LoadRows` 的 `name` 對 DB 是 SQL、對 CSV 是檔名——是否需要在型別上區分（傾向不區分，維持現況的「兩種來源模型」）

## Implementation Plan

### Stub 階段（先做）

- [ ] `IDataSource.LoadRows` 簽名 + `LoadSet` / `LoadParam` 的 default implementation 骨架（TODO）
- [ ] `CsvCtrl.ParseCsv(TextReader)` 簽名 + TODO；既有 public 方法保留原簽名
- [ ] `SetBase<T>` 內部 `LoadRows` 入口簽名 + 四個 public 方法的轉呼叫骨架
- [ ] 共用「字串 → 型別」轉換層 + 表頭對位的簽名 + TODO
- [ ] 七條載入 / 寫出路徑的簽名各補齊（`CsvCtrl.WriteSet` 多欄、`DbDataSource.LoadRows`、`InMemoryDataSource` 多欄）
- [ ] Generator：注入 arity 2–6 的 attribute 宣告 + 兩個診斷碼描述子（不產碼）
- [ ] `VariableBuilder` 的 ValueTuple 分支與 arity 防呆簽名 + TODO
- [ ] `dotnet build` 全綠、既有 69 測試全過（stub 未被呼叫到）

### 逐層實作

- [ ] 共用轉換層：字串 → 型別、表頭顯式判定、Trim / 空行 / culture 統一
- [ ] `CsvCtrl`：格式層與位址層分離；多欄讀 / 寫
- [ ] `IDataSource` 三個實作收斂成 `LoadRows`
- [ ] `SetBase`：四入口轉呼叫、多分量解析、唯一性以整組判定
- [ ] Generator：base class 字串拼接 + 分量名檢查 + 診斷碼
- [ ] `VariableBuilder`：ValueTuple 維度攤平展開 + arity 防呆
- [ ] 檔案切分（`CsvSolutionSink` / `OracleSolutionSink` / `ISolutionSink` / `ISolutionBatch`）
- [ ] 測試：三項行為變更 / 七條載入路徑 / 同檔共讀 / round-trip / BuildVars / 混用 / arity 防呆 / 診斷碼 / arity 1 回歸
- [ ] Templates 回歸驗證（四個專案零改動、解值相同）
- [ ] FJSP benchmark
- [ ] `developer-guide.md` 補讀取層與多維 Set 章節

## References

- 受影響的既有規格：`framework-dev-spec.md`（資料防護規格）、`cplex-project-dev-spec.md`
- 分類原則：無值多維 = Set、有值多維 = Parameter；值必須是數值，非數值的「值」其實是維度
- 外部慣例：AMPL `set ARCS within {NODES,NODES}`、GAMS `set arc(n,n)`、OPL `tuple` set、Pyomo `Set(within=...)`
- 後續可能的規格：`DbDataSource` 實作 `IDataSource`、Parameter 以多維 Set 為定義域 + `[FullGrid]` 稀疏覆蓋、積木式泛型 `[OptSet<Set_Node,Set_Node>]`、`OracleDBCtrl` 移出 Core、`ParamLookup` 缺格 log、LLMDevFramework 規範同步
