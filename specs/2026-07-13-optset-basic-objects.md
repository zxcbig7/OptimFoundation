# OptSet 積木：建模基本物件設計說明

status: **implemented（core + generator，2026-07-14）** —— 原設計 Vic 逐項確認，實作全綠並經 smoke + 10 xUnit 驗證
定位: 本檔為 OptSet 基本物件的權威行為描述；實作見 `src/OptimFoundation.Core/SetBase.cs` + `src/OptimFoundation.Generators/AutoSetsGenerator.cs`
關聯: 總規格 `LLMDevFramework/specs/2026-07-13-optim-ai-spec-consolidation.md`（D5 段，執行 Phase 1）
驗證: scratchpad smoke（`[OptSet<DateTime>]`/`[OptVar<>]`/`[OptParam<>]` build 綠 + 生成碼正確 + key 格式 `@2026-08-01@Alice@2` + 未載入防呆）+ `SetBaseTests` 10 例；full sln 0 error、83 tests 全綠

---

## 核心概念：積木與底板

Set 從「Dataload 裡的散裝 `List<string>` 欄位」變成標準積木；Dataload 從 god-class 變成底板，只負責把積木扣上去並載入資料。與既有 `[OptVar]`/`[OptParam]` 完全對稱，`(名字, 型別)` 全系統只在 Set 積木宣告一次，其餘全機械推導。

```text
Set 積木  [OptSet<T>]     (名字, 型別) 唯一宣告處 ── generator ──▶ : SetBase<T>
      │ 泛型參數引用
      ├──▶ Param 積木 [OptParam<Set_X, ...>]  ── generator ──▶ 維度 property + QTY + ctor
      └──▶ Var 積木   [OptVar<Set_X, ...>]    ── generator ──▶ 維度 property（key 順序 = 泛型參數順序）
      ▲
Dataload 底板             組合積木 + 三檔位載入（inline / CSV / Oracle）
```

---

## 基本物件一覽

| 物件 | 誰寫 | 職責 |
| --- | --- | --- |
| Set 積木 `Set_<Name>` | 建模者，2 行 | `[OptSet<T>]` 宣告一個 index set 的名字與元素型別；合法 `T` 清單由 generator 診斷把守（/sdd 定案，與 `ConvertSetsToStringLists` 支援域對齊） |
| `SetBase<T>` | 框架（Core） | 積木基底：載入、防呆、`IEnumerable<T>` |
| Param 積木 `Parameter_<Name>` | 建模者，2 行 | 引用 Set 積木宣告參數維度；`QTY` 自動注入 |
| Var 積木 `Variable{B\|I\|X}_<Name>` | 建模者，2 行 | 引用 Set 積木宣告變數維度；型別由前綴決定（既有天條） |
| Dataload 底板 | 建模者 | 組合積木 + 選擇資料檔位；全 repo 唯一碰資料來源的地方 |
| `AutoSetsGenerator` | 框架（Generators） | 編譯期把上述宣告補成完整 class；命名違規 → OPTF 診斷 |

---

## 1. Set 積木

宣告（`Set/` 一檔一積木）：

```csharp
[OptSet<DateTime>]                    // 元素型別直接吃泛型參數
public partial class Set_Date { }

[OptSet<string>]                      // 元素型別 MUST 顯式寫出，string 也不例外
public partial class Set_Employee { }
```

generator 生成（一行 body）：

```csharp
public partial class Set_Date : global::OptimFoundation.Core.SetBase<global::System.DateTime> { }
```

規則：

- 類名 MUST `Set_<PascalName>`，違反 → OPTF 診斷（沿用 OPTF001/002 機制）
- 元素型別走泛型 `[OptSet<T>]`，與 `[OptParam<>]`/`[OptVar<>]` 三者語法統一；`OptSetAttribute` 與 `OptSetAttribute<T>` 同名不同 arity 合法並存
- **寫法定案（2026-07-20）**：無參數版 `[OptSet]` 仍合法、generator 持續視為 `SetBase<string>`（舊 code 不需遷移），但**預設一律寫 `[OptSet<string>]`** —— Why: 宣告處看得出元素型別；兩者 codegen 完全相同，顯式零成本
- 封閉域由 generator 診斷把守：`T` 不在合法清單（string/DateTime/int…，/sdd 定案）→ OPTF error，訊息列出合法清單 —— Why: C# 無 union constraint，寫不出「只准三種型別」的 `where`
- attribute 引數不能放 tuple 或裸型別名（C# 限制），這是整套泛型 attribute 設計的由來

## 2. SetBase&lt;T&gt; 契約

```csharp
public abstract class SetBase<T> : ISetBrick, IReadOnlyList<T>
{
    // List<T> 保序（key 組成順序穩定）+ HashSet<T> 陪跑（Contains O(1)）
    public int Count { get; }
    public T this[int index] { get; }        // 積木即唯讀 List，免另存 List 視圖
    public bool Contains(T item);            // Dataload 失同步驗證用

    public void LoadInline(params T[] items);
    public void LoadFrom(IEnumerable<T> items);
    public void Load(IDataSource source, string name = null);  // paved path（CSV/InMemory）：source.LoadSet(name ?? SetName) → ParseElement；set 名檔名形式 Set_{X}（見 IO/SetNaming）
    public void LoadCsv(string name);        // CSV 便利入口 → CsvCtrl.ReadStrSet + ParseElement（string→T 單一真相）
    // DB set：積木 LoadFrom(db.LoadSet("SELECT ..."))——DB query-only，不經 IDataSource
}

// ISetBrick 維持 marker interface（泛型 attribute 的 where T : ISetBrick 約束用）；
// Dataload ctor 顯式呼叫 Load / LoadParam，不靠反射自動載入
```

四道防呆（錯誤左移，全部丟明確例外）：

1. 未載入就 enumerate → `InvalidOperationException` —— NEVER 空集合靜默解出退化解（MILP 最陰的 bug）
2. 載入後空集合 → 例外
3. 二次載入 → 例外（載入即封存；`Reload` 與否 /sdd 定）
4. 重複成員 → 例外（重複會生出重複變數 key）

型別特化用 extension method，不進 generator：

```csharp
public static void LoadRange(this SetBase<DateTime> set, DateTime start, int days);
```

已驗證相容性：`SetBase<T> : IEnumerable<T>` 直接餵進既有 `Build*Vs(params object[])` ——`ConvertSetsToStringLists` 以 pattern match 吃 `IEnumerable<T>`（`Core/VariableBuilder.cs`, search:`ConvertSetsToStringLists`），框架消費端零改動；DateTime key 序列化沿用既定 `yyyy-MM-dd`。

## 3. Param 積木

```csharp
/// <summary>每日各班別人力需求 Demand[d,g]</summary>
[OptParam<Set_Date, Set_Group>]
public partial class Parameter_ShiftDemand { }
```

語法說明：`[OptParam(Set_Date, ...)]` 裸型別名在 C# 不合法（引數只能放值，型別要當值傳只有 `typeof`）。paved path 用 **C# 11 泛型 attribute**（net8 + LangVersion latest 已支援），視覺上等同直接塞；配 `where T : ISetBrick` constraint，引用非積木 → **CS0311 原生 compile error**（免寫診斷）。arity 宣告 `<T1>`…`<T1..T6>`（無變長泛型，6 維夠用）。非泛型 `typeof` 版留作逃生口（alias + 舊寫法遷移）。

generator 生成：

```csharp
public partial class Parameter_ShiftDemand : global::OptimFoundation.Core.ParameterBase
{
    public global::System.DateTime Date { get; set; }   // ← Set_Date 推導
    public string Group { get; set; } = string.Empty;   // ← Set_Group 推導
    public double QTY { get; set; }                     // ← 固定注入、永遠最後（既有天條）

    public Parameter_ShiftDemand(params object[] sets) => InitClassBySets(sets);
    public Parameter_ShiftDemand() { }
}
```

## 4. Var 積木

```csharp
/// <summary>員工 e 在日期 d 是否排入班別 g</summary>
[OptVar<Set_Date, Set_Employee, Set_Group>]
public partial class VariableB_ShiftAssign { }   // B 前綴 = Binary（既有天條：B/I/X）
```

隱形天條：**泛型參數順序 = property 順序 = `InitClassBySets` 的 key 組成順序**（`VariableB_ShiftAssign@2026-08-01@Alice@Night`）。調換順序 = 不同變數。

## 5. Generator 運作流程（編譯期，每次 build 自動發生）

```text
Step 1  掃描：找出所有掛 [OptSet<T>] / [OptParam] / [OptVar] 的 partial class

Step 2  先解析 Set 積木（名稱 & 型態的唯一來源）
        [OptSet<DateTime>] + class 名 Set_Date
        → 登記：名稱 "Date"（去 Set_ 前綴）、CLR 型別 DateTime（泛型參數直讀，無對映表）

Step 3  解析 Param/Var 的 Set 引用（自動去抓，宣告處零型別資訊）
        [OptParam<Set_Date, Set_Group>]
        → 對每個泛型參數，用 semantic model 找到該 Set 積木
        → property 名抓 class 名、CLR 型別抓它的 [OptSet<T>] 泛型參數
        （逃生口 typeof/字串引數同流程處理）

Step 4  生成 body：維度 property（依泛型參數順序）+ QTY（Param 固定最後）+ InitClassBySets ctor
```

關鍵性質：Param/Var 宣告處不寫（也寫不了）任何名稱與型別——100% 從 Set 積木抓。改 `Set_Date` 的 enum，下次 build 所有引用它的 Param/Var property 型別自動跟著變，一處改全域生效。

編譯期診斷保證（全部擋在 build，不進 runtime）：

| 情境 | 結果 |
| --- | --- |
| 泛型參數 / `typeof` 指到不存在的 class | CS0246（C# 原生） |
| Param/Var 泛型參數塞非積木 class | CS0311（`where T : ISetBrick` constraint，C# 原生） |
| `[OptSet<T>]` 的 `T` 不在合法元素型別清單 | OPTF 診斷（新增）：訊息列出合法清單 |
| 逃生口 `typeof` 指到沒掛 `[OptSet<T>]` 的 class | OPTF 診斷（新增）：「引用的不是 Set 積木」 |
| Set/Param/Var 類名前綴違規 | OPTF 診斷（沿用 OPTF001/002 機制） |

## 6. 成員名稱推導（全機械，唯一命名動作 = 幫積木取名）

| 位置 | 規則 | 例（`Set_TruckFleet`） |
| --- | --- | --- |
| 積木類名 | `Set_<PascalName>`，違反 → OPTF 診斷 | `Set_TruckFleet` |
| Var/Param property | 去 `Set_` 前綴，不轉換 | `TruckFleet` |
| 同 set 雙維度 | alias 直接當 property 名（語法 /sdd 定） | `From` / `To` |
| `QTY` | Param 固定生成、永遠最後 | `QTY` |
| Dataload 欄位 | UPPER_SNAKE（Pascal 機械轉換） | `TRUCK_FLEET` |
| CSV 欄頭 / Oracle bind | 框架既有行為（property 名 `.ToUpper()`） | 既定 |

注意：舊文件「Set 屬性全大寫+底線（`PRODUCT_INDEX`）」規則與 generator 實際輸出（PascalCase）矛盾，以本表為準。

## 7. Dataload 底板：顯式 ctor（寫讀檔的家）

paved path = Dataload ctor **每行一句、顯式**——一眼看得出每個 set / parameter 從哪來。
換來源只換傳入的 `IDataSource`（InMemory / Csv / Db），`Set/ Parameter/ Variable/ Constraint/ Objective/` 全部零改動。

```csharp
public class Dataload
{
    public Set_Date DATE = new();
    public Set_Employee EMPLOYEE = new();
    public Set_Group GROUP = new();
    public List<Parameter_ShiftDemand> shiftDemand = new();

    public Dataload() : this(new CsvDataSource()) { }   // 預設 = CSV

    public Dataload(IDataSource source)
    {
        DATE.Load(source);                              // 慣例（= Set_Date）
        EMPLOYEE.Load(source, "Set_HeadCount");         // 就地指定檔名形式 Set_{X}（三來源統一，見 IO/SetNaming）
        GROUP.Load(source);
        shiftDemand = source.LoadParam<Parameter_ShiftDemand>();   // CSV/InMemory 參數走名稱慣例
        // DB 參數只能明寫 SQL（用型別化 DbDataSource ctor）：
        //   shiftDemand = db.LoadParam<Parameter_ShiftDemand>("SELECT ... WHERE data_id=:id", (":id","V1"));
        ValidateSetsCoverParameters();                  // 失同步驗證，明明白白寫在這
    }
}
```

就地覆寫「吃哪個檔 / Query 哪段 SQL」：set → `Load(source, name)`；CSV/InMemory parameter → `LoadParam<T>(name)`；
**DB parameter 只能明寫 SQL** → `DbDataSource.LoadParam<T>(sql, ...)` / set `LoadSet(sql)`（不猜表名，見下）。
inline 小集合 / 程式生成 → `LoadInline` / `LoadRange` / `Add` 直接寫在 ctor 裡。

> DB query-only：`SELECT * FROM 表` 的「猜表名」對真實 DB 太受限（join / 條件 / 投影 / WHERE data_id 都做不到），故 `DbDataSource` 是 query-only、**不實作 `IDataSource`**（第一引數是 SQL 而非名稱），但與 CSV/InMemory 共用 `LoadParam`/`LoadSet` 命名。DB set 進積木：`SET.LoadFrom(db.LoadSet("SELECT ..."))`。

數值保真天條照舊：所有數值 MUST 與原始問題描述完全一致，NEVER 四捨五入 / 推算 / 佔位。

## 8. 消費端（完全不變）

```csharp
// VariableCreate
BuildBVs<VariableB_ShiftAssign>(dataload.DATE, dataload.EMPLOYEE, dataload.GROUP);

// Constraint（Pool API 照舊；LHS/RHS 鐵則照舊：NEVER 移項 / 改號 / 翻轉方向）
foreach (var d in dataload.DATE)
    foreach (var g in dataload.GROUP)
    {
        foreach (var e in dataload.EMPLOYEE)
            engine.AddLHS(1.0, new VariableB_ShiftAssign(d, e, g));
        var demand = dataload.shiftDemand.FirstOrDefault(x => x.Date == d && x.Group == g)?.QTY ?? 0.0;
        engine.AddRHS(demand);
        engine.CreateGreatEqual($"FullfillDemand@{d:yyyy-MM-dd}@{g}");
    }
```

---

## 待決項定案（v1 實作採用的預設，2026-07-14）

0. **合法元素型別清單** → 定案：`string / DateTime / int / long / double / decimal`（對齊 `ConvertSetsToStringLists` 支援域）。非法型別 → OPTF004 診斷。enum 僅 `LoadInline/LoadFrom` 支援（不進 CSV/DB parse）。
1. **Reload** → v1 不做：二次載入丟例外（載入即封存）。實驗換資料集用 new 一顆積木。
2. **同 set 多維度 / 自訂 index 名** → 定案走「具名維度 `[OptDim<TSet>("name")]`」，見下方「§多維度命名規格」（**NEVER 用 alias 積木引用另一個 set**——Set 就是 Set）。
3. **衍生積木（子集 / 稀疏笛卡兒積）** → v1 不做，列 v2。
4. **舊寫法遷移** → 字串式 `[OptVar("Date:DateTime")]` 原封保留（逃生口），與泛型 `[OptVar<Set_X>]` 並存；漸進遷移、不強制。

## §多維度命名規格（2026-07-14 定義，待實作）

status: **implemented（`[OptDim<TSet>("name")]` generator 已加、smoke 驗證 From/To 同 set 多維度可用、83 tests 全綠，2026-07-14）**

### 原則（Vic 定調）

- **Set 就是 Set**：`[OptSet<T>]` 只定義「元素型別 + 集合本身」，**NEVER 引用/alias 另一個 set**
- **維度的名字與來源 set 由 Var/Param 的 attribute 決定**（不是從 set 名反推）
- **人只寫 attribute，功能全在 generator**：property body / QTY / ctor / BuildBVs 接線全部生成，不手寫 property
- **零影響 Constraint/Objective**：生成的 property 是元素型別（string/DateTime…）、可 object-initialize，現有 `new VariableB_X { Dim = val }` 完全照舊

### 兩種維度宣告（並存；★ 具名維度式為預設，Vic 定 2026-07-14）

**A. ★ 具名維度式（預設寫法）**：每維明寫 `[OptDim<TSet>("name")]`（來源 set + 維度名），一致、貼近數學 index、天生解同 set 多維度。範本 `FJSP_BASIC_BRICK` 全用此式。

```csharp
// 一般多維（各 set 各用一次）
[OptDim<Set_Lot>("Lot")]
[OptDim<Set_Operation>("Operation")]
[OptDim<Set_Eqp>("Eqp")]
[OptVar]                        // 型別由前綴 B/X/I 決定
public partial class VariableB_Assign { }

// 同 set 多維度：LotA/LotB 都 ∈ Lot（同一個 Set_Lot、角色名不同）
[OptDim<Set_Lot>("LotA")]
[OptDim<Set_Operation>("OperationA")]
[OptDim<Set_Lot>("LotB")]
[OptDim<Set_Operation>("OperationB")]
[OptVar]
public partial class VariableB_Precede { }

// scalar（0 維）：無 set，直接 [OptVar]，key = "VariableX_Makespan"
[OptVar]
public partial class VariableX_Makespan { }
```

Param 同款（`[OptParam]` 自動補 QTY）：

```csharp
[OptDim<Set_Lot>("Lot")]
[OptDim<Set_Operation>("Operation")]
[OptDim<Set_Eqp>("Eqp")]
[OptParam]                      // → Lot/Operation/Eqp + QTY
public partial class Parameter_ProcessTime { }
```

**B. 泛型式（可選簡寫）**：維度名 = set 名時可用 `[OptVar<Set_Lot, Set_Operation, Set_Eqp>]`（零字串），但同 set 多維度會撞名、不適用。保留供簡單情況。

```csharp
[OptVar<Set_Lot, Set_Operation, Set_Eqp>]   // → Lot / Operation / Eqp（維度名 = set 名）
public partial class VariableB_Assign { }
```

數學對應（同 set 多維度）：`Precede_{lA,oA,lB,oB},  lA,lB ∈ Lot,  oA,oB ∈ Operation`。

- `[OptDim<TSet>("name")]`：泛型參數 = 來源 set（`Set_Eqp` **直接綁**，NEVER alias）；字串 arg = 維度名
- `AllowMultiple = true`，**宣告順序 = 維度順序 = key 組成順序**
- 生成 `public string From { get; set; }`（型別從 `Set_Eqp` 的 `[OptSet<T>]` 自動抓）
- Param 版 `[OptParam][OptDim<...>...]` 照樣自動補 `QTY`（最後）

### 唯一的字串（誠實標註）

維度**名字**是唯一的字串（`"From"`）——C# attribute 無法不用字串鑄一個新識別名；但**來源 set 與元素型別全程 type-safe**（`Set_Eqp` 是真型別、經 `where TSet : ISetBrick` 檢查）。常見情況（A 式）連名字都免寫、全零字串。

### generator 責任

1. 注入 `OptDimAttribute<TSet>(string name)`（`AllowMultiple`、`where TSet : ISetBrick`）
2. Var/Param 有 `[OptDim<>]` → 依宣告順序生成 property（名字=arg、型別=TSet 的 `[OptSet<T>]`）+ QTY(param) + 兩 ctor
3. 生成靜態 `dim → set` 對照（供 BuildBVs 自動接線同 set 傳多次）
4. `[OptDim<TSet>]` 的 TSet 非積木 → CS0311；混用 `[OptVar<>]` 泛型與 `[OptDim<>]` → OPTF 診斷擋（二選一）

### 消費端（Constraint/Objective 不變）

```csharp
BuildBVs<VariableB_Transfer>(dataload.EQP, dataload.EQP);          // 同 set 傳兩次
engine.AddLHS(1.0, new VariableB_Transfer { From = e1, To = e2 }); // 照舊，string 值
```

---

## 遷移注意

- 字串式維度寫法**永久合法**（逃生口）；新 code 建議用泛型 `[OptSet<T>]` / `[OptVar<Set_X>]`，同 set 多維度用 `[OptDim<TSet>("name")]`
- Phase 1 剩餘出貨步驟（loop 續跑）：更新 `developer-guide.md` 新章（本檔為底稿）→ 同步 AI-Modeling `CPLEX_API_REFERENCE.md` 鏡像 → 跑 `setup-dlls.ps1 -Build` 回填消費端 `dlls/` + 刷新 `Templates/dlls/OptimFoundation.Generators.dll`
