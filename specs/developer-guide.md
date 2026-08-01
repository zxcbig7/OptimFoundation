# OptimFoundation 開發手冊

> 給基於此框架開發新最佳化問題的開發者

---

## 目錄

1. [架構概覽](#1-架構概覽)
2. [Variable — 定義決策變數](#2-variable--定義決策變數)
3. [Parameter — 定義模型參數](#3-parameter--定義模型參數)
3.5. [Set 積木與OptDim逐維宣告（paved path）](#35-set-積木與optdim逐維宣告paved-path)
4. [Dataload — 資料初始化](#4-dataload--資料初始化)
4.5. [IDataSource — 資料來源抽象](#45-idatasource--資料來源抽象)
4.6. [DataContext — 資料防護（驗證 / OptData.Load）](#46-datacontext--資料防護驗證--optdataload)
5. [VariableCreate — 建立變數（BuildVars 泛型建立）](#5-variablecreate--建立變數buildvars-泛型建立)
6. [Pool API — 建立限制式](#6-pool-api--建立限制式)
7. [Objective Function — 目標式](#7-objective-function--目標式)
8. [BuildModel — 組裝模型](#8-buildmodel--組裝模型)
9. [CplexConfig — 求解器設定](#9-cplexconfig--求解器設定)
10. [執行與結果](#10-執行與結果)
11. [軟性限制式（Soft Constraints）](#11-軟性限制式soft-constraints)
12. [變數界限動態修改](#12-變數界限動態修改)
13. [進階：繼承 OptEngine](#13-進階繼承-optengine)
14. [進階：Benders Decomposition](#14-進階benders-decomposition)
15. [Experiment — Tuning 實驗記錄](#15-experiment--tuning-實驗記錄)
16. [常見錯誤 FAQ](#16-常見錯誤-faq)

---

## 1. 架構概覽

```
EngineBase<TModel, TVar, TExpr, TConstr>    ← 核心抽象（solver 無關）
    └── OptimFoundation.Cplex.OptEngine      ← IBM CPLEX 實作
    └── OptimFoundation.Gurobi.OptEngine     ← Gurobi 實作
```

### 呼叫流程

```
new OptEngine(config)
    └── .Build()                  ← 初始化 CPLEX/Gurobi 模型
          └── VariableCreate.Build()   ← BuildCVs / BuildIVs / BuildBVs
          └── BuildModel.Build()       ← AddLHS / AddRHS / Create* / CreateMinimize
    └── .Solve()                  ← 送出求解，回傳 bool
    └── .GetSetVarValues<TVariable>()     ← 取得解值
    └── .Dispose()                ← 釋放 native 資源
```

### Variable Key 格式

框架用 **class 名稱 + property 值** 組成唯一識別字串：

```
VariableB_ShiftAssign@2026-01-01@E1@D
│                    │          │  │
class 名稱           Date       E1 Group
```

DateTime 固定格式 `yyyy-MM-dd`，其餘型別直接呼叫 `ToString()`。

---

## 2. Variable — 定義決策變數

在 `VariablesClass/` 下新增 class，繼承 `VariableBase`，**只宣告 properties，不寫任何邏輯**。

```csharp
// 二元變數（Binary）
public class VariableB_ShiftAssign : VariableBase
{
    public DateTime Date     { get; set; }
    public string   Employee { get; set; }
    public string   Group    { get; set; }
}

// 連續變數（Continuous）
public class VariableX_BelowAVG : VariableBase
{
    public string Employee { get; set; }
}

// 整數變數（Integer）
public class VariableI_WorkCount : VariableBase
{
    public string Employee { get; set; }
}
```

### 命名慣例

| 前綴 | 變數類型 | 對應建立方法 |
| --- | --- | --- |
| `VariableB_` | Binary（0/1） | `BuildBVs<TVariable>()` 或 `BuildVars<TVariable>()` |
| `VariableX_` | Continuous（連續） | `BuildCVs<TVariable>()` 或 `BuildVars<TVariable>()` |
| `VariableI_` | Integer（整數） | `BuildIVs<TVariable>()` 或 `BuildVars<TVariable>()` |

> **前綴 load-bearing**：前綴不只是命名慣例，還決定型別。`[OptVar]` 已不帶 `VarType` 參數，generator 依前綴判型並產碼，前綴非法直接 compile error（`OPTF001`，訊息教正確取名）；`BuildVars<T>` 亦由前綴推型，免記 B/C/I。需自訂 bounds 時才用 `BuildCVs/BuildIVs(lb, ub, ...)`。
>
> **重要**：Properties 的**宣告順序**決定 key 格式。建立變數時傳入的 Sets 順序必須與 properties 順序一致。

---

## 3. Parameter — 定義模型參數

在 `Data/` 下新增 class，繼承 `ParameterBase`。`QTY` 放最後，代表數值欄位。

```csharp
public class Parameter_ShiftDemand : ParameterBase
{
    public DateTime Date  { get; set; }
    public string   Group { get; set; }
    public double   QTY   { get; set; }   // ← 數值欄位永遠放最後

    public Parameter_ShiftDemand(params object[] sets) => InitClassBySets(sets);
}
```

### 建構方式

```csharp
// 方式 1：params 建構子（位置對應 property 順序）
new Parameter_ShiftDemand(new DateTime(2026, 1, 1), "D", 5.0)

// 方式 2：object initializer（需要無參建構子）
// 若移除 params 建構子，則必須加 public Parameter_ShiftDemand() { }
new Parameter_ShiftDemand { Date = new DateTime(2026, 1, 1), Group = "D", QTY = 5 }
```

> **注意**：`params object[] sets` 建構子在以 0 個參數呼叫時會觸發 `InitClassBySets`，因為 properties 數量與傳入數量不符而拋 `ArgumentException`。  
> 因此，若同時使用 object initializer，必須另外提供空的 `public Parameter_ShiftDemand() { }`。

---

## 3.5 Set 積木與OptDim逐維宣告（paved path）

> 2026-07 新增，**2026-07-15 定版**：逐維具名宣告 `[OptDim<TSet>("Name")]` + 光桿 `[OptVar]`/`[OptParam]` 是**唯一 paved path**（權威範例 = `Templates/FJSP_BASIC_BRICK`）。
> 舊多參數泛型 `[OptVar<T1..T6>]`/`[OptParam<T1..T6>]` 與字串式 `[OptVar("Date:DateTime")]` 皆降級為**逃生口**（遷移期保留，新 code 不用，見本節末段）。完整設計見 `specs/2026-07-13-optset-basic-objects.md`。

### 概念

Set 從 Dataload 的裸 `List<string>` 欄位，升級成標準積木：一個 Set 一顆積木、一檔兩行，`(名字, 型別)` 全系統只宣告一次；Variable / Parameter 用**光桿 attribute + 逐維 `[OptDim<TSet>("Name")]`** 宣告每一維，attribute 順序 = key 順序，property 名與元素型別由 generator 自動抓。

### 三步宣告（Set 積木 → Parameter → Variable）

```csharp
// 1. Set 積木：一個 Set 一顆，元素型別 MUST 顯式寫出（裸 [OptSet] 仍合法 = [OptSet<string>]，但非預設寫法）
[OptSet<string>] public partial class Set_Lot { }
[OptSet<string>] public partial class Set_Operation { }
[OptSet<string>] public partial class Set_Eqp { }
[OptSet<DateTime>] public partial class Set_Date { }

// 2. Parameter：光桿 [OptParam] + 逐維 [OptDim<TSet>("Name")]，QTY 自動補在最後
[OptParam]
[OptDim<Set_Lot>("Lot")]
[OptDim<Set_Operation>("Operation")]
[OptDim<Set_Eqp>("Eqp")]
public partial class Parameter_ProcessTime { }

// 3. Variable：光桿 [OptVar] + 逐維 [OptDim<TSet>("Name")]，型別由前綴 B_/X_/I_ 決定
[OptDim<Set_Lot>("Lot")]
[OptDim<Set_Operation>("Operation")]
[OptDim<Set_Eqp>("Eqp")]
[OptVar]
public partial class VariableB_Assign { }
```

generator 生成（自動抓名稱 + 型別，順序 = `[OptDim]` 排列順序）：

```csharp
public partial class Parameter_ProcessTime : global::OptimFoundation.Core.ParameterBase
{
    public string Lot { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Eqp { get; set; } = string.Empty;
    public double QTY { get; set; }
    public Parameter_ProcessTime(params object[] sets) => InitClassBySets(sets);
    public Parameter_ProcessTime() { }
}
```

### 同 set 多維度（角色名區分）

同一個 Set 積木可以在同一個 Variable 引用多次，各自取不同維度角色名——泛型參數是**來源 set**（直接綁、不 alias），字串是**維度角色名**，兩者不必同名：

```csharp
// 同機台先後序：(LotA,OperationA) 先於 (LotB,OperationB)
// LotA/LotB 都 ∈ Set_Lot、OperationA/OperationB 都 ∈ Set_Operation
[OptVar]
[OptDim<Set_Lot>("LotA")]
[OptDim<Set_Operation>("OperationA")]
[OptDim<Set_Lot>("LotB")]
[OptDim<Set_Operation>("OperationB")]
public partial class VariableB_Precede;
```

### Scalar（零維）變數

不需要任何 set 時，光桿 `[OptVar]` 不加 `[OptDim]` 即為 0 維純量，key 直接是類名、無 `@` 索引：

```csharp
[OptVar]
public partial class VariableX_Makespan { }
// key = "VariableX_Makespan"（純量，無索引）
```

### 命名推導（全機械）

| 位置 | 規則 |
| --- | --- |
| 積木類名 | `Set_<PascalName>`，違反 → OPTF003 |
| `[OptDim<TSet>("Name")]` 維度名 | 字串參數直接就是 property 名（可與積木類名不同，供同 set 多角色） |
| attribute 順序 | = property 順序 = `InitClassBySets` 的 key 組成順序（調換 = 不同變數） |
| 合法元素型別 | `string / DateTime / int / long / double / decimal`，其餘 → OPTF004 |

### 編譯期診斷

| 情境 | 結果 |
| --- | --- |
| 泛型參數塞非積木 class | CS0311（`where T : ISetBrick`，C# 原生） |
| `[OptSet<T>]` 元素型別非法 | OPTF004 |
| 引用的型別無 `[OptSet<T>]` | OPTF005 |
| `DataContext` 子類的 `Set_*`/`Parameter_*` 欄位漏掛 `[OptSet<T>]`/`[OptParam]` | OPTF006（見 §4.6——否則該欄位靜默不註冊、永不受驗） |

### `SetBase<T>` 契約

`SetBase<T> : ISetBrick, IReadOnlyList<T>`（支援 `[i]`/`Count`/foreach/LINQ/`Contains`，免另存 List 視圖），可直接餵進 `BuildVars<T>`/`BuildBVs/BuildIVs/BuildCVs(params object[])`（經 `VariableBuilder.ConvertSetsToStringLists`），DateTime key 沿用 `yyyy-MM-dd`。四道防呆（全丟明確例外）：

1. 未載入就列舉 → `InvalidOperationException`（NEVER 空集合靜默解出退化解）
2. 載入後為空 → `InvalidOperationException`
3. 二次載入 → `InvalidOperationException`（載入即封存；v1 無 Reload）
4. 重複成員 → `ArgumentException`

`ISetBrick`（`SetBase<T>` 實作的 marker interface）額外提供型別安全的非泛型讀值面，供 `DataValidator` 在零 runtime reflection 前提下讀值（見 §4.6）：

| 成員 | 用途 |
| --- | --- |
| `int Count` | 集合成員數 |
| `Type ElementType` | 元素型別（`SetBase<T>` 的 `T`），供辨別 dangling 與 type mismatch |
| `bool ContainsObject(object value)` | 先型別檢查再比對，非本集合元素型別一律回 `false` |
| `IEnumerable<object> MembersAsObjects()` | 逐一 box 回傳成員（保序），供 `[FullGrid]` 完整性檢查組笛卡兒積 |

### 資料載入（paved path = Load(IDataSource)，來源回 string 由 ParseElement 依 T 轉型）

```csharp
// paved path：set 名用「檔名形式」Set_{X}（三來源統一，見 §4.5 SetNaming）；name 省略 = 類名慣例
// 換 CSV↔DB↔記憶體只換傳入的 IDataSource（Dataload ctor 每行一句，見 §4.5）
LOT.Load(source);                  // 慣例（= Set_Lot）
LOT.Load(source, "Set_OldLots");   // 就地指定要吃哪個檔

// inline / 程式生成（測試 / 小題目 / enum）
DATE.LoadInline(new DateTime(2026, 8, 1), new DateTime(2026, 8, 2));
DATE.LoadFrom(someSequence);

// CSV 便利別名：LoadCsv(檔名)（= Load 的 CSV 特化）。DB set 用 LoadFrom(db.LoadSet("SELECT ..."))
LOT.LoadCsv("Set_Lot");
```

### 逃生口（遷移用，新 code NEVER 用）

- **多參數泛型**：`[OptParam<Set_Date, Set_Employee>]` / `[OptVar<Set_Date, Set_Employee>]`——維度名固定 = 積木類名去 `Set_` 前綴，無法像 `[OptDim]` 一樣同 set 取多個角色名（如 `LotA`/`LotB`）
- **字串式**：`[OptVar("Date:DateTime")]` / `[OptParam("Item", "Date:DateTime")]`——generator 舊語法，早期專案殘留

兩者行為仍受支援（generator 繼續產碼），但治理文件與新專案一律教 `[OptDim<TSet>("Name")]`。

---

## 4. Dataload — 資料初始化

在 `Data/Dataload.cs` 定義所有 Sets 和 Parameters，並在建構子中填入資料。

```csharp
public class Dataload
{
    // ── Sets ──────────────────────────────────────────
    public List<string>   Employee = new List<string>();
    public List<string>   Group    = new List<string>();
    public List<DateTime> Date     = new List<DateTime>();

    // ── Parameters ────────────────────────────────────
    public List<Parameter_ShiftDemand> parameter_ShiftDemand = new List<Parameter_ShiftDemand>();

    public Dataload()
    {
        // 初始化 Sets
        Employee.AddRange(new[] { "E1", "E2", "E3" });
        Group.AddRange(new[] { "O", "D", "E", "N" });

        int year = 2026, month = 1;
        int days = DateTime.DaysInMonth(year, month);
        for (int d = 1; d <= days; d++)
            Date.Add(new DateTime(year, month, d));

        // 初始化 Parameters
        foreach (var date in Date)
        {
            parameter_ShiftDemand.Add(new Parameter_ShiftDemand(date, "D", 5.0));
            parameter_ShiftDemand.Add(new Parameter_ShiftDemand(date, "E", 3.0));
            parameter_ShiftDemand.Add(new Parameter_ShiftDemand(date, "N", 2.0));
        }
    }
}
```

上面是最基礎的手動初始化寫法（測試 / 小題目），本身不觸發任何驗證。paved path 用 Set 積木 + `IDataSource` 抽象（見下節），且正式專案的 `Dataload` 應繼承 `DataContext`、透過 `OptData.Load` 建構，才會取得自動資料驗證（見 §4.6）。

---

## 4.5 IDataSource — 資料來源抽象

**檔案**：`OptimFoundation.Core/IO/IDataSource.cs`、`CsvDataSource.cs`、`DbDataSource.cs`

Dataload 只依賴 `IDataSource` 介面，換資料來源（CSV / DB / 記憶體）**只換傳入的 IDataSource 物件，模型與驗證 code 全不動**——與「換 solver 只換 OptEngine」同一哲學。

```csharp
public interface IDataSource
{
    // 讀某參數型別的全部列；name 就地覆寫資料位址（CSV = 檔名、DB = 表名），null 走慣例 = 型別名
    List<TParamClass> LoadParam<TParamClass>(string name = null) where TParamClass : ModelElementBase, new();

    // 讀無法由 Parameter 衍生的一維 set（獨立名單檔/表）；name 為邏輯名稱，各實作自行解析
    List<string> LoadSet(string name);
}
```

### 兩個實作

| 實作 | 用途 | 讀法 |
| --- | --- | --- |
| `CsvDataSource` | 讀 `Data/` 資料夾 CSV | 參數 = `LoadParam<T>("檔名")`（`{型別名}.csv`，帶表頭按名對位）；set 檔 = `Set_{name}.csv` |
| `InMemoryDataSource` | demo / 測試 / 程式生成 | `AddParameters` / `AddSet` 鏈式註冊；`LoadParam` 以型別為 key |
| `DbDataSource`（不實作 IDataSource） | 讀資料庫（透過 `IDbCtrl`，如 Oracle） | **只走明寫 SQL**：參數 `LoadParam<T>(sql)`、set `LoadSet(sql)`。欄名對位、多餘欄忽略、可用 `AS` 別名。**無「猜表名」慣例** |

> DB 為何只給 SQL：`SELECT * FROM 表` 對真實 DB 太受限——join、條件、投影、`WHERE data_id` 都做不到，「猜表名」的預先處理既不直觀又限制實用。故 `DbDataSource` 是 query-only、**不實作 `IDataSource`**（第一引數是 SQL 而非名稱，語意不同），但與 CSV/InMemory 共用 `LoadParam`/`LoadSet` 命名。

### Dataload ctor = 「寫讀檔的家」（paved path）

Dataload ctor **每行一句、顯式**——一眼看得出每個 set / parameter 的資料從哪來。慣例走預設，特例就地指定：

```csharp
// public partial + : DataContext：見 §4.6，讓 OptData.Load 建構後自動驗證資料
public partial class Dataload : DataContext
{
    public Set_Lot LOT = new();
    public Set_Operation OPERATION = new();
    public Set_Eqp EQP = new();
    public List<Parameter_ProcessTime> parameter_ProcessTime = new();

    public Dataload() : this(new CsvDataSource()) { }   // 預設來源 = CSV

    public Dataload(IDataSource source)
    {
        // Set：慣例（Set_{名}.csv）。特例：LOT.Load(source, "Set_OldLots") 就地指定檔 / 邏輯名
        LOT.Load(source);
        OPERATION.Load(source);
        EQP.Load(source);

        // Parameter：慣例（型別名）。特例見下方「就地覆寫」
        parameter_ProcessTime = source.LoadParam<Parameter_ProcessTime>();

        // 不在這裡手寫驗證——OptData.Load 建構完成後自動跑 DataValidator（見 §4.6）
    }
}

// 換來源（CSV↔InMemory）：OptData.Load(() => new Dataload(new InMemoryDataSource()...))
// DB 來源：DB 參數只能明寫 SQL，故用專屬 ctor（見下方「混合 / DB 來源」），非套進本 IDataSource ctor
// 建構一律走 OptData.Load(() => new Dataload(...))（blessed path，見 §4.6）；直接 new Dataload(...) 仍可編譯但不觸發驗證
```

### 就地覆寫「吃哪個檔 / Query 哪段 SQL」

| 需求 | 寫法 |
| --- | --- |
| set 檔 / 邏輯名 ≠ 類名慣例 | `LOT.Load(source, "Set_OldLots")` |
| CSV 參數檔 ≠ 型別名 | `source.LoadParam<Parameter_X>("Parameter_Legacy")` |
| **DB 參數（唯一途徑）** | `db.LoadParam<Parameter_X>("SELECT ... WHERE data_id=:id", (":id","V1"))` |
| DB set | `SET.LoadFrom(db.LoadSet("SELECT DISTINCT eqp FROM t"))` |

`LoadParam` / `LoadSet` 是 `DbDataSource` 專屬（型別化 `DbDataSource db`，非 `IDataSource`）——SQL 本就綁 DB。欄名對 property 名（大小寫不敏感、多餘欄忽略），欄名不符用 `AS` 別名對過去（`unit_profit AS qty`）。

### 混合 / DB 來源（顯式 ctor 的回報）

`IDataSource` 逐次呼叫，混用零成本；DB 因只給 SQL，用型別化 `DbDataSource` 的專屬 ctor：

```csharp
public Dataload(CsvDataSource csv, DbDataSource db)
{
    LOT.Load(csv, "Set_Lot");                                    // 維度小表放 CSV
    parameter_ProcessTime = db.LoadParam<Parameter_ProcessTime>(  // 主檔放 DB，明寫 Query
        "SELECT lot, operation, eqp, proc_time AS qty FROM route WHERE data_id = :id", (":id", "V1"));
    // 不在這裡手寫驗證——OptData.Load 建構完成後自動跑 DataValidator（見 §4.6）
}
```

**set 名統一（`Core/IO/SetNaming.cs`）**：使用端一律用檔名形式 `Set_{X}`（= 磁碟 `Set_X.csv`、Set 積木類名），各來源在邊界自動轉位址——CSV 讀 `Set_X.csv`、DB 交 resolver 邏輯名 `X`、InMemory 當 key。`"Product"` 與 `"Set_Product"` 經此一律等價，三來源不再各行其是。

### 兩階段：不規則來源解析一次，之後全走標準接口（`WriteSet` / `WriteParam`）

原始資料格式不合框架（要 pivot / join / 去重 / 算衍生欄）時，**整理邏輯只寫一次**：第一階段自寫 parse，把積木輸出成標準 CSV；之後每次計算走 `IDataSource`，不再碰那堆 parse code。

```text
第一階段  不規則來源 → 專案自寫 parse → 積木 → CsvCtrl.WriteSet / WriteParam → Data/*.csv
第二階段  Data/*.csv → new CsvDataSource() → 同一組積木
```

輸出位置就是 `CsvCtrl` 所有讀取方法的來源位置（`Data/`），所以第二階段**不需要任何新參數**。

```csharp
public partial class Dataload : DataContext
{
    // 第一階段：吃不規則來源（rawFile 相對於 Data/）
    public Dataload(string rawFile)
    {
        var grid = CsvCtrl.ReadMatrixCsv(rawFile);
        ROW.LoadFrom(Enumerable.Range(1, grid.GetLength(0)));
        for (int r = 0; r < grid.GetLength(0); r++)
            for (int c = 0; c < grid.GetLength(1); c++)
                if (grid[r, c] > 0)
                    parameter_Given.Add(new Parameter_Given(r + 1, c + 1, (int)grid[r, c]));
    }

    // 第二階段：標準接口
    public Dataload(IDataSource source)
    {
        ROW.Load(source, "Set_Row");
        parameter_Given = source.LoadParam<Parameter_Given>("Parameter_Given");
    }

    // 中間那個箭頭。檔名 MUST 與第二階段 ctor 讀取時用的名稱一致，否則下次讀不到
    public void Export()
    {
        CsvCtrl.WriteSet(ROW, "Set_Row");
        CsvCtrl.WriteParam(parameter_Given, "Parameter_Given");
    }
}
```

```csharp
var data = OptData.Load(() => new Dataload("raw/Puzzle_SHC279"));   // 第一次
data.Export();
var data2 = OptData.Load(() => new Dataload(new CsvDataSource()));  // 之後每次
```

| 項目 | 行為 |
| --- | --- |
| 檔名預設 | set → `Set_{類名去前綴}`；parameter → 型別名（與讀取端慣例一致） |
| 表頭 | set 無表頭單欄；parameter 第一列 = property 名大寫（`BuildParameter` 按名對位、大小寫不敏感） |
| 數值 | `double` 走 `"R"` + InvariantCulture，read-back 不失真 |
| `DateTime` | 固定 `yyyy-MM-dd`（與變數 key 格式一致）；**帶時分秒直接丟例外**，NEVER 靜默截斷 |
| 欄位來源 | `typeof(T).GetProperties()`——與 `BuildParameter` / `InitClassBySets` 同一來源 |
| 覆寫 | 同名檔直接覆寫（輸出位置就是讀取位置），log 印 `overwritten=True` |

**保證的是「用同一份資料」，不是「偵測資料變了」**——沒有 hash / manifest / 比對擋下。要確保資料不變就讀輸出的那份。

> **`PreserveNewest` 會咬人**：專案 csproj 的 `<None Update="Data\**\*.csv" CopyToOutputDirectory="PreserveNewest" />` 依時間戳決定是否複製。`Export()` 寫出的檔較新 → 下次 build 不會被專案原始 `Data/` 蓋掉；但手改了專案原始 CSV（變更新）→ build 時會蓋掉輸出。手改優先是合理的，但踩到會困惑。

### 失同步驗證——已收進框架，NEVER 專案端手寫

> **舊版**曾教在 ctor 尾端手寫 `ValidateSetsCoverParameters()`（逐 parameter 手動比對 set），**此寫法已過時**：這類「set 若改由檔案 / 資料表定義，可能與 parameter 失同步」的檢查，連同重複 key、缺格、數值 sanity 等機械邏輯，**2026-07-18 起集中到框架的 `DataContext`/`DataValidator`（見 §4.6）**，`Dataload` 繼承 `DataContext` 並透過 `OptData.Load` 建構即自動取得，涵蓋範圍比手寫版更完整（dangling / type mismatch / duplicate key / missing cell / numeric sanity 一次全報）。**專案端 NEVER 再手寫這類驗證方法。**

### BigM 由數據推導，NEVER 寫死

BigM 一律從已載入的 Parameter 算出（如「最壞情況全序列排程長度」），不得寫死常數：

```csharp
public double BigM => parameter_ProcessTime
    .GroupBy(p => new { p.Lot, p.Operation })
    .Sum(g => g.Max(p => p.QTY));
```

若推導涉及除法（比值型 BigM，如「產能上限 / 最小單位工時」），改用 `Numeric.SafeRatio` 防呆除零 / 非有限 / 過大量級（見 §4.6 有完整門檻說明），Tutorial 的實際寫法：

```csharp
public double BigM => Numeric.SafeRatio(
    parameter_Capacity.Max(c => c.QTY),
    parameter_MachineHours.Where(h => h.QTY > 0).Min(h => h.QTY),
    context: "BigM");
```

### 解回讀：GetSetVarValues 批量取解

求解後用 `engine.GetSetVarValues<TVariable>()` 一次抓整個變數型別的解值字典（key = 完整變數名稱），取代逐 key 呼叫 `GetVariableValue`：

```csharp
var assign = engine.GetSetVarValues<VariableB_Assign>();
var makespanVar = engine.GetSetVarValues<VariableX_Makespan>();
double makespan = makespanVar["VariableX_Makespan"];   // scalar：key 無 @ 索引

string assignedEqp = EqpSet.FirstOrDefault(e => assign[$"VariableB_Assign@{lot}@{op}@{e}"] > 0.5) ?? "?";
```

輸出解用 `CsvSolutionSink.WriteSolution<T>(engine)`（`ISolutionSink` 抽象，另有 `OracleSolutionSink`）：

```csharp
var sink = new CsvSolutionSink();
sink.WriteSolution<VariableB_Assign>(engine);   // → Solution/VariableB_Assign.csv
```

### 輸出 transaction：多變數型別原子寫入（`BeginBatch`）

單一 `WriteSolution<T>` 逐次呼叫時彼此獨立；若要多個變數型別包進**同一個** transaction（要嘛全寫、要嘛全不寫），改用 `ISolutionSink.BeginBatch` 開一個 `ISolutionBatch`：

```csharp
using (var batch = new OracleSolutionSink(dbCtrl, "SOLUTION_TABLE").BeginBatch(dataId: "V1"))
{
    batch.Write<VariableX_Produce>(engine);
    batch.Write<VariableB_Setup>(engine);
    batch.Write<VariableI_Batch>(engine);
    batch.Commit();   // 未呼叫 Commit 即 Dispose → 整批捨棄，DB 無殘留
}
```

- `OracleSolutionSink`：`Write<T>` 先緩衝，`Commit()` 時才把全部緩衝包進單一 `IDbCtrl.ExecuteInTransaction` 一次送出；任一步失敗全 rollback。單一變數型別的實際寫入走 `IDbCtrl.ExecuteBatch`（Oracle 端用 array-bind 一次送出整批列），非逐列 `Execute`——MILP 解動輒數十萬列，逐列 INSERT 會讓輸出從秒級退化成分鐘級。
- `CsvSolutionSink`：`BeginBatch` 為 no-op batch（逐檔寫、`Commit()` 空實作），行為與直接呼叫 `WriteSolution` 逐字相同。
- `IDbCtrl` 新增的 primitive：`void ExecuteInTransaction(Action<IDbCtrl> work)`（巢狀呼叫參與外層交易，不重複 begin/commit）、`void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows)`。交易編排（ambient 連線/交易、成功 commit、例外 rollback 後 rethrow）集中在 `DBCtrlBase`，與 Oracle 無關；具體驅動只需覆寫 `CreateRawConnection`。

> **誠實標記已知限制**：以上交易編排邏輯與 `OracleSolutionSink` 對 `IDbCtrl` 的呼叫序**已用假 `IDbConnection`/假 `IDbCtrl` 單元測試驗證**；但 `OracleDBCtrl.CreateRawConnection` 對真實 `OracleConnection` 的行為、`ExecuteBatch` 對真實 Oracle 的 array-bind 型別綁定**未對真實 Oracle DB 驗證過**（無可用測試 DB），僅保證編譯與 code review 層級的正確性。本手冊 NEVER 暗示 Oracle 端的 transaction / array-bind 已對真實 DB 驗證。

### CSV 解析：RFC4180（引號 / 逗號 / 跳脫）

`CsvCtrl` 的解析（`SplitLine`，供 `ReadLines`/`ReadTable`/`ReadParameter`/`BuildParameter` 共用）已升級為 RFC4180：

- `"a, b",c` → `["a, b", "c"]`（引號內的逗號不裂欄）
- `"he said ""hi"""` → `he said "hi"`（`""` 跳脫成一個字面 `"`）
- 前後空白、無引號欄位行為與舊版一致，不 regression
- **不支援欄位內換行**：行結束時引號未閉合 → 丟 `InvalidDataException`（訊息含行號），維持逐行讀取，非跨行掃描

---

## 4.6 DataContext — 資料防護（驗證 / OptData.Load）

> 2026-07-18 新增：把「load 之後」的機械邏輯（參照完整性、key 唯一性、數值 sanity……）從各專案手寫、複製，集中進框架寫一次。**LOAD 本身維持顯式手寫**（§4.5 的每行 `.Load()` 一行未改）；改變的只是「驗證」與「建構入口」。

### `Dataload : DataContext` + `OptData.Load`（blessed 建構路徑）

`Dataload` MUST 宣告成 `public partial class Dataload : DataContext`；建構統一走 `OptData.Load`：

```csharp
public partial class Dataload : DataContext
{
    // ... Set 積木 / Parameter 欄位 / ctor 同 §4.5，不需改動 ...
}

// 建構（Program.cs / 問題入口）：
var dataload = OptData.Load(() => new Dataload());
var dataload2 = OptData.Load(() => new Dataload(new InMemoryDataSource()));   // 任意 ctor 皆可，含多來源
```

```csharp
public static class OptData
{
    public static T Load<T>(Func<T> factory) where T : DataContext;   // 唯一多載，支援任意 ctor
}
```

**只有這一個多載**（`Func<T> factory`）；不存在 `Load<T>(IDataSource)` 這種第二多載——換來源 / 混合來源一律靠 `factory` lambda 內自行決定怎麼 `new Dataload(...)`，`OptData.Load` 本身不關心建構細節。

`OptData.Load` 內部：`factory()` 建出實例 → 呼叫 generator 為 `Dataload` emit 的註冊碼（登記每顆 set / 每批 parameter）→ 自動跑 `ValidateData()`。**直接 `new Dataload(...)` 仍可編譯，但不會觸發驗證**——`OptData.Load` 是唯一 blessed path，Tutorial 只示範這條路徑。

### `DataValidator`：五類檢查 + opt-in 完整性，一次全報

建構完成時聚合檢查，任何違規 → 一次丟出 `DataValidationException`（`Issues` 屬性含**全部**問題，不是遇到第一個就中止）：

| Kind | 觸發時機 | 範例訊息 |
| --- | --- | --- |
| `MissingSet` | parameter 宣告的 index-set 名找不到對應已註冊的 Set（每個 parameter 只驗一次，不掛在列迴圈——否則零列 parameter + set 名打錯會完全靜默） | `index 欄位 'Product' 找不到對應的 Set（Set 未註冊或名稱打錯）` |
| `TypeMismatch` | index 欄位值型別與宣告的 Set 元素型別不符（辨別「宣告打錯」而非「值不存在」） | `第 3 列 index 欄位 'Date' 值 '...' 型別為 String，與 Set 'Date' 元素型別 DateTime 不符` |
| `Dangling` | index 欄位值型別正確，但不在對應 Set 內 | `第 7 列 index 欄位 'Product' 值 'Ghost' 不在 Set 'Product' 內。` |
| `DuplicateKey` | 同一 parameter 出現重複 index-key | `index key (Desk,2026-08-01) 重複：第 1 列與第 7 列。` |
| `Numeric` | 任一 `double` 欄位（含 `QTY`）為 `NaN` / `±Infinity` / 超過量級門檻 | 見下方「數值量級門檻」 |
| `MissingCell`（opt-in，見下） | 標了 `[FullGrid]` 的 parameter 缺格 | `[FullGrid] 缺 1 格（共 6 格）：(Chair,2026-08-02)` |

**專案端 NEVER 再手寫這類驗證**（如舊版 `ValidateSetsCoverParameters()`，見 §4.5）——邏輯零複製，繼承 `DataContext` 即得。

驗證失敗時的例外：

```csharp
public sealed class DataValidationException : Exception
{
    public IReadOnlyList<DataIssue> Issues { get; }   // 每筆：Kind / Parameter / Detail
}
```

驗證通過時，`Logging.Info` 印載入摘要（各 set 成員數、各 parameter 列數），非靜默成功。

### `[FullGrid]`：opt-in 完整性檢查

稀疏資料在 MILP 中合法（如「只列有需求的日期」），故完整性檢查**預設不檢查**；某 parameter 語意上必須全格覆蓋時才標 `[FullGrid]`：

```csharp
[OptParam]
[FullGrid]
[OptDim<Set_Product>("Product")]
[OptDim<Set_Date>("Date")]
public partial class Parameter_Demand { }
```

缺格時報 `MissingCell`，訊息列出缺的 `(set₁,set₂,…)` 組合（笛卡兒積過大——超過 1,000,000 格——則不逐一枚舉，改報格數不符，避免記憶體/時間爆掉）。

### `OPTF006`：漏掛 attribute → compile error

`DataContext` 子類（`Dataload`）裡引用的 `Set_*`/`Parameter_*` 型別，若忘記掛 `[OptSet<T>]`/`[OptParam]`，該欄位會被 generator 的註冊碼**靜默排除**——不報錯但永遠不受驗證，是最危險的靜默失效。故升級成 compile error `OPTF006`，訊息教你補上對應 attribute（見 §3.5 編譯期診斷表）。

### `Numeric.SafeRatio`：衍生值的除法防呆

```csharp
public static double SafeRatio(double numerator, double denominator,
    double magnitudeCeiling = 1e9, string context = null);
```

`den == 0` / 結果非有限（`NaN`/`Infinity`）/ `|結果| > magnitudeCeiling` 一律 throw `InvalidOperationException`（訊息含 `context`），否則回傳比值。供 BigM 這類「由資料推導的衍生值」防呆用（見 §4.5「BigM 由數據推導」）。

### 兩個量級門檻，刻意不同、NEVER 統一

| 門檻 | 用途 | 預設值 | 對象 |
| --- | --- | --- | --- |
| `Numeric.SafeRatio` 的 `magnitudeCeiling` | 衍生值（如 BigM）的比值 sanity | **1e9**（較嚴） | solver 數值穩定性（BigM 過大會讓 relaxation 鬆弛、branch 爆炸） |
| `DataValidator.MaxMagnitude` | 原始資料值（parameter 的 `double` 欄位）的數值 sanity | **1e15**（較寬） | 超過此值連 `double` 的整數精度都保不住，任何 solver 都會數值失穩 |

兩者服務不同對象（衍生值 vs 原始值），門檻刻意不同——**NEVER 為了「統一」而改成同一個值**。

---

## 5. VariableCreate — 建立變數（BuildVars 泛型建立）

在 `VariablesClass/VariableCreate.cs` 的 `Build()` 中呼叫建立方法。

```csharp
public void Build()
{
    // Binary：員工 × 日期 × 班別
    optEngine.BuildBVs<VariableB_ShiftAssign>(dataload.Date, dataload.Employee, dataload.Group);

    // Binary：員工 × 日期（兩維）
    optEngine.BuildBVs<VariableB_SixDayWork>(dataload.Date, dataload.Employee);

    // Continuous：員工（一維）
    optEngine.BuildCVs<VariableX_BelowAVG>(dataload.Employee);

    // Integer：指定 LB / UB
    optEngine.BuildIVs<VariableI_WorkCount>(0, 30, dataload.Employee);

}
```

`BuildVars`/`BuildBVs`/`BuildCVs`/`BuildIVs` 會由 Core 自動統計，不需手動寫 log。
每個型別建立後會輸出 `count=實際/預期`，`Solve()` 前再輸出總摘要：

```text
[變數建立完成] type=VariableB_ShiftAssign count=372/372
[變數建立摘要] count=744/744 types=3
```

預期值是輸入 sets 的笛卡兒積所產生的變數名稱數；實際值是 solver 建立後真正新增到
變數索引的數量，因此重複名稱或部分建立失敗不會被誤報為成功。

### 完整 Build 方法列表

| 方法 | 類型 | LB | UB |
| --- | --- | --- | --- |
| `BuildVars<TVariable>(sets)` | 由類別名前綴決定 | 前綴預設 | 前綴預設 |
| `BuildBVs<TVariable>(sets)` | Binary | 0 | 1 |
| `BuildCVs<TVariable>(sets)` | Continuous | 0 | 1E100 |
| `BuildCVs<TVariable>(lb, ub, sets)` | Continuous | 自訂 | 自訂 |
| `BuildIVs<TVariable>(sets)` | Integer | 0 | 1E100 |
| `BuildIVs<TVariable>(lb, ub, sets)` | Integer | 自訂 | 自訂 |

> **順序規則**：`sets` 的傳入順序必須與 Variable class 的 property 宣告順序完全一致。

---

## 6. Pool API — 建立限制式

限制式採用 **Pool 機制**：先把左右兩側的項目放入 Pool，再呼叫 `Create*` 送出。送出後 Pool 自動清空。

### Pool 操作方法

```csharp
AddLHS(double coeff, object varSpec)   // LHS 加入：係數 × 變數
AddLHS(double constant)                // LHS 加入：常數
AddRHS(double coeff, object varSpec)   // RHS 加入：係數 × 變數
AddRHS(double constant)                // RHS 加入：常數
```

### 送出限制式

| 方法 | 數學意義 |
| --- | --- |
| `CreateEqual(string name)` | LHS − RHS = 0 |
| `CreateEqual(double rhs, string name)` | LHS = rhs |
| `CreateLessEqual(string name)` | LHS − RHS ≤ 0 |
| `CreateLessEqual(double rhs, string name)` | LHS ≤ rhs |
| `CreateGreatEqual(string name)` | LHS − RHS ≥ 0 |
| `CreateGreatEqual(double rhs, string name)` | LHS ≥ rhs |
| `CreateRange(double lb, double ub, string name)` | lb ≤ LHS ≤ ub |

送出後 Pool 自動清空，不需手動呼叫 `ClearPool()`。

框架不會再靜默忽略未建立的限制式：Pool 為空時方法回傳 `false` 並記錄
`CONSTRAINT_EMPTY`；名稱重複時保留第一條限制式、清空 Pool，並記錄
`CONSTRAINT_DUPLICATE`。`AddLHS`/`AddRHS` 收到 `null` 變數時則記錄
`VARIABLE_NULL`。Event ID 只保留可搜尋的關鍵字；每筆診斷接一段簡短說明，再附上
`name`、`reason`、`result` 等定位問題需要的欄位，不能只留下看不出影響的代碼。

限制式數量由 Core 自動累計，不再使用 `ConstraintBase.ConstraintCount`。每次呼叫
`CreateEqual`/`CreateLessEqual`/`CreateGreatEqual`/`CreateRange` 或 soft constraint 都算一筆
預期；只有真正加入 solver model 才算實際。`Solve()` 前依 constraint 名稱第一個 `@`
之前的部分分組輸出：

```text
[限制式建立完成] group=Demand count=48/50
[限制式建立完成] group=Capacity count=20/20
[限制式建立摘要] count=68/70 groups=2 solverTotal=68
```

因此 constraint 名稱應遵守 `Group@索引...`，讓自動分組穩定。

### 範例：等號限制式

```csharp
// 每位員工每天只能排一個班別
// Σ_g X[d,e,g] = 1
dataload.Group.ForEach(g =>
    engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = g }));

engine.AddRHS(1);
engine.CreateEqual($"OneGroup@{d:yyyy_MM_dd}@{e}");
```

### 範例：不等號，含 RHS 變數

```csharp
// SixDayWork[d,e] ≤ 1 − ShiftAssign[d−6,e,O]
engine.AddLHS(1, new VariableB_SixDayWork { Date = d, Employee = e });
engine.AddRHS(1);
engine.AddRHS(-1, new VariableB_ShiftAssign { Date = prevDate, Employee = e, Group = "O" });
engine.CreateLessEqual($"SixDay@{d:yyyy_MM_dd}@{e}");
```

### 範例：直接指定 RHS 數值

```csharp
// Σ_d ShiftAssign[d,e,N] ≥ minNightShifts
dataload.Date.ForEach(d =>
    engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "N" }));

engine.CreateGreatEqual(minNightShifts, $"MinNight@{e}");
```

### Pool 內部機制

```
送出前：
  LHS terms: [(1.0, X[d,e,D]), (1.0, X[d,e,E]), (1.0, X[d,e,N]), (1.0, X[d,e,O])]
  LHS const: 0
  RHS terms: []
  RHS const: 1

建立的約束：
  Σ X[d,e,g] − 0  =  1 − 0
  → Σ X[d,e,g] = 1  ✓
```

框架在送出時計算 `(LHS terms − RHS terms) {sense} (RHS const − LHS const)`，因此 LHS/RHS 兩側的變數可以自由混用。

---

## 7. Objective Function — 目標式

```csharp
public void Build()
{
    // 加入各項懲罰
    dataload.Date.ForEach(d =>
        dataload.Employee.ForEach(e =>
        {
            engine.AddLHS(dataload.Penalty_SixDay,   new VariableB_SixDayWork  { Date = d, Employee = e });
            engine.AddLHS(dataload.Penalty_NightToDay, new VariableB_NightToDay { Date = d, Employee = e });
        }));

    dataload.Employee.ForEach(e =>
    {
        engine.AddLHS(dataload.Penalty_BelowAVG, new VariableX_BelowAVG { Employee = e });
        engine.AddLHS(dataload.Penalty_Weekend4Day, new VariableX_WeekendLT4 { Employee = e });
    });

    engine.CreateMinimize(); // 或 engine.CreateMaximize()
}
```

> **規則**：目標式只能呼叫 `AddLHS`，不能使用 `AddRHS`。

`CreateMinimize`/`CreateMaximize` 會自動記錄統一的開始與完成事件，不需在
`ObjectiveFunction` 或 `BuildModel` 手動輸出：

```text
[目標式建構開始] sense=Minimize terms=120
[目標式建構完成] sense=Minimize terms=120 result=success
```

空目標式會以 `result=skipped reason=no_terms` 完成；建構例外則以
`result=failed` 記錄後重新拋出。

---

## 8. BuildModel — 組裝模型

建議每個 project 建立一個抽象 base class 讓所有限制式繼承，避免重複宣告 engine/dataload。

```csharp
// Constraints/ConstraintBase.cs（project 層級）
public abstract class RosterConstraintBase : ConstraintBase
{
    protected OptEngine Engine { get; set; }
    protected Dataload   Data   { get; set; }
    public abstract void Build();
}
```

```csharp
// Constraints/BuildModel.cs
public class BuildModel
{
    private OptEngine engine;
    private Dataload  dataload;

    public BuildModel(Dataload dataload, OptEngine engine)
    {
        this.engine   = engine;
        this.dataload = dataload;
    }

    public void Build()
    {
        new ObjectiveFunction { Engine = engine, Data = dataload }.Build();
        new Constraint_OneGroup   { Engine = engine, Data = dataload }.Build();
        new Constraint_SixDayWork { Engine = engine, Data = dataload }.Build();
        // ... 其他限制式；建模 log 由 Core 自動產生
    }
}
```

---

## 9. CplexConfig — 求解器設定

```csharp
var config = new CplexConfig
{
    // ── 執行緒 ─────────────────────────────────────────────
    workThreads = 8,           // 平行執行緒數（預設 32）

    // ── 求解精度 ────────────────────────────────────────────
    epGap       = 1e-4,        // MIP Gap 容忍值（預設 1e-4，即 0.01%）
    epOpt       = 1e-6,        // Optimality tolerance（預設 1e-6）
    epRHS       = 1e-6,        // Feasibility tolerance（預設 1e-6）

    // ── 時間控制 ────────────────────────────────────────────
    timeLimit   = 3600,        // 求解上限秒數（null = 無限制）

    // ── Solution Polishing ─────────────────────────────────
    polishAfterTime = 1200,    // N 秒後啟動 Polishing，改善整數解品質

    // ── MIP 策略 ────────────────────────────────────────────
    mipEmphasis = 2,           // 0=平衡, 1=強調可行解, 2=強調最佳解, 3=路徑最佳, 4=隱藏可行解
    varSel      = 0,           // 分支變數選擇策略（0=自動）
    nodeSelect  = 0,           // Node 選擇策略（0=自動）
    algorithm   = 0,           // 根節點演算法（0=自動, 1=Simplex, 2=Dual, 4=Barrier）

    // ── 記憶體 ─────────────────────────────────────────────
    workMemory  = 4096,        // 工作記憶體上限 MB（預設 2048）
    rowRead     = 30000,       // 限制式讀入上限（預設 30000）
    nodeFileInd = 1,           // Node 資訊儲存：0=不儲存, 1=記憶體(預設), 2=磁碟, 3=磁碟壓縮

    // ── 輸出 ────────────────────────────────────────────────
    enableLog   = true,        // true = CPLEX 求解 log 即時顯示到 Console + 寫 log 檔
    exportLP    = false,       // 輸出 .lp 模型檔
    exportMPS   = false,       // 輸出 .mps 模型檔
    exportSol   = false,       // 輸出 .sol 解答檔

    // ── 其他 ────────────────────────────────────────────────
    randomSeed  = 0,           // 隨機種子（null = CPLEX 預設）
};
```

### enableLog 行為說明

| `enableLog` | Console | Log 檔 |
| --- | --- | --- |
| `true` | 即時顯示 CPLEX solver progress（node、gap、iteration） | 完整 CPLEX log + 框架 log |
| `false` | 只顯示框架 log（ObjVal、Status 等） | 完整 CPLEX log + 框架 log |

`enableLog` 現在只控制 Console 顯示，不會再丟棄 solver 診斷資料。若 solver
拋出例外，框架會先記錄 `SOLVER_EXCEPTION`、保存已捕捉的 solver log，然後
將原例外重新拋出。

### Scale Guard：`Solve()` 前的變數規模警告

`ISolverConfig` 額外提供 `int ScaleWarnThreshold { get; } = 10_000_000`（default interface member，各 Config 實作免改動即繼承此預設，需要時可覆寫）：

```csharp
public interface ISolverConfig
{
    // ... TimeLimit / MipGap / Threads / LogToConsole / LogFilePath ...
    int ScaleWarnThreshold => 10_000_000;
}
```

`engine.Solve()`（見 §12 template method 說明）在真正送出求解前，先比較 `TotalVarCount` 與 `Config.ScaleWarnThreshold`：超過門檻只 `Logging.Warn` 一則（含實際變數數與門檻），**不 throw、不中止**——大但合法的模型不該被擋，只是提醒可能拖慢求解。

---

## 10. 執行與結果

### 問題入口

```csharp
public class RosteringProblem : IDisposable
{
    public OptEngine optEngine;
    public Dataload  dataload;

    public RosteringProblem()
    {
        dataload = new Dataload();
        Logging.SetLogFileName(GetType().Name);
    }

    public bool Execute()
    {
        var config = new CplexConfig
        {
            epGap       = 0.03,
            timeLimit   = 300,
            workThreads = 8,
            enableLog   = true,
            exportSol   = true,
            exportLP    = true
        };

        optEngine = new OptEngine(config);
        optEngine.Build();
        optEngine.SetModelName(GetType().Name);

        new VariableCreate(dataload, optEngine).Build();
        new BuildModel(dataload, optEngine).Build();

        bool solved = optEngine.Solve();

        if (solved)
            WriteResults();

        return solved;
    }

    private void WriteResults()
    {
        // 取得特定變數類型的所有解值
        var shifts = optEngine.GetSetVarValues<VariableB_ShiftAssign>();
        foreach (var (key, value) in shifts)
            if (value > 0.5)
                Logging.Info($"  {key} = {value:F0}");
    }

    public void Dispose() => optEngine?.Dispose();
}
```

### 結果 API

```csharp
// ── 狀態 ────────────────────────────────────────────────────
optEngine.Status;                               // SolveStatus enum
// SolveStatus: NotSolved / Optimal / Feasible / Infeasible / Unbounded / TimeLimit / Error

// ── 目標值 ──────────────────────────────────────────────────
double obj = optEngine.GetObjectiveValue();

// ── 解值 ────────────────────────────────────────────────────
// 取得某類型所有變數解值
Dictionary<string, double> sol = optEngine.GetSetVarValues<VariableB_ShiftAssign>();

// 取得所有變數解值（依 varTypeName 過濾或不過濾）
IReadOnlyDictionary<string, double> all = optEngine.GetSolution();
IReadOnlyDictionary<string, double> bvs = optEngine.GetSolution("VariableB_ShiftAssign");

// 取得單一變數解值
double val = optEngine.GetVariableValue("VariableB_ShiftAssign@2026-01-01@E1@D");

// ── 變數名稱 ─────────────────────────────────────────────────
string[] names = optEngine.GetSetVarNames<VariableB_ShiftAssign>();
string[] all   = optEngine.GetAllVarNames();

// ── 變數數量 ─────────────────────────────────────────────────
int total      = optEngine.varCount;       // Variables dict 總數
int setTotal   = optEngine.TotalVarCount;  // VariableSets 總數
```

### 輸出目錄

| 目錄 | 內容 | 啟用條件 |
| --- | --- | --- |
| `Output/Model/` | `.lp`、`.mps` 模型檔 | `exportLP = true` 或 `exportMPS = true` |
| `Output/Sol/` | `.sol` 解答檔 | `exportSol = true` |
| `Output/IIS/` | `.ilp` 衝突子集（Infeasible 時自動產生） | 狀態為 Infeasible 時自動 |
| `Output/Logs/` | 執行 log `.txt` | 每次執行都產生 |

---

## 11. 軟性限制式（Soft Constraints）

軟性限制式用懲罰（penalty）取代硬性約束，允許違反但加重目標式。

```csharp
// LessEqual Soft：LHS ≤ rhs，違反時懲罰 penalty
engine.AddLHS(1, new VariableX_WeekendCount { Employee = e });
engine.CreateLeSoft(rhs: 4.0, penalty: 0.1);

// GreaterEqual Soft：LHS ≥ rhs，違反時懲罰 penalty
engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "D" });
engine.CreateGeSoft(rhs: minDemand, penalty: 10.0);

// Equal Soft：LHS = rhs，偏差量以 delta 變數加入目標式
engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "D" });
engine.CreateEqSoft(rhs: demand, penalty: 5.0, name: $"Demand@{d:yyyy_MM_dd}@D");
```

> 軟性限制式只能在目標式建立後呼叫，框架會根據目標式方向（Minimize/Maximize）自動決定懲罰符號。

---

## 12. 變數界限動態修改

在 `Build()` 後，可以透過 protected 方法動態修改已建立變數的 LB/UB（限繼承 OptEngine 的子類別）：

> **NEVER override `Build()` / `Solve()`**：`EngineBase.Build()`/`Solve()` 現為 **sealed template method**（`Build()` 內呼叫 `BuildCore()`；`Solve()` 先跑 scale guard `PreSolveGuard()` 再呼叫 `SolveCore()`，見 §9），**已不是 `virtual`**，`override void Build()` 會直接 CS0506 compile error。需要客製建模流程（如下例的 PreAssign）時，改覆寫 `BuildCore()`：

```csharp
public class MyEngine : OptEngine
{
    public MyEngine(CplexConfig config) : base(config) { }

    protected override void BuildCore()
    {
        base.BuildCore();
        BuildBVs<VariableB_ShiftAssign>(dates, employees, groups);

        // 固定特定排班（強制 PreAssign）
        foreach (var pre in preAssigns)
        {
            SetVarLB(new VariableB_ShiftAssign { Date = pre.Date, Employee = pre.Employee, Group = pre.Group }, 1.0);
        }
    }
}
```

| protected 方法 | 說明 |
| --- | --- |
| `SetVarLB(varSpec, lb)` | 設定下界 |
| `SetVarUB(varSpec, ub)` | 設定上界 |
| `SetVarRange(varSpec, lb, ub)` | 同時設定上下界 |

---

## 13. 進階：繼承 OptEngine

當需要存取 dual values、追蹤特定限制式物件或擴充 OptEngine 功能時，可以繼承 `OptimFoundation.Cplex.OptEngine`。擴充建模流程請覆寫 `BuildCore()`/`SolveCore()`，**NEVER** 嘗試 `override Build()`/`Solve()`（已是 sealed template method，見 §12）。

繼承後可存取以下 protected 成員：

| 成員 | 型別 | 說明 |
| --- | --- | --- |
| `Model` | `ILOG.CPLEX.Cplex` | CPLEX 原生模型物件 |
| `Variables` | `Dictionary<string, INumVar>` | 所有變數（name → INumVar） |
| `VariableSets` | `Dictionary<string, Dictionary<string, INumVar>>` | 依類型分組的變數 |
| `ReadVar(varSpec)` | `INumVar` | 查詢變數 |
| `AddLE(name, lhs, rhs)` | `IRange` | 建立 ≤ 限制式並回傳 `IRange` |
| `AddGE(name, lhs, rhs)` | `IRange` | 建立 ≥ 限制式並回傳 `IRange` |
| `AddEQ(name, lhs, rhs)` | `IRange` | 建立 = 限制式並回傳 `IRange` |
| `SetVarLB/UB/Range(...)` | void | 修改變數界限 |
| `PoolLhsTerms` | `IEnumerable<(double coef, INumVar var)>` | 存取目前 LHS pool |

### 實作範例：取得 Dual 值

```csharp
public class SubProblemEngine : OptEngine
{
    private readonly List<(string key, IRange constraint)> _tracked = new();

    public SubProblemEngine(CplexConfig config) : base(config) { }

    public void BuildConstraints(List<string> dests, Dictionary<string, double> demand)
    {
        foreach (var d in dests)
        {
            var lhs = Model.LinearNumExpr();
            // 用 ReadVar 存取已建立的變數
            foreach (var s in sources)
                lhs.AddTerm(1.0, ReadVar(new VariableX_Flow { Source = s, Dest = d }));

            // AddGE 回傳 IRange，保存供後續取 dual
            var r = AddGE($"Demand@{d}", lhs, demand[d]);
            _tracked.Add((d, r));
        }
    }

    // 求解後取 dual
    public double GetDemandDual(string dest)
        => Model.GetDual(_tracked.First(x => x.key == dest).constraint);
}
```

---

## 14. 進階：Benders Decomposition

框架提供 `ResetConstraint()`、`CopyModel()`、`MergeModel()` 和 Thread 限制式方法，支援 Benders Decomposition。

### 核心 API

```csharp
// ── 主問題 / 子問題每輪重建 ─────────────────────────────────
optEngine.ResetConstraint();
// 清空目標式與所有限制式（保留變數）
// 典型用途：子問題每輪迭代重新建立不同的 Y* 固定約束

// ── 模型複製 ────────────────────────────────────────────────
OptEngine clone = masterEngine.CopyModel(masterEngine);
// 複製目標式與第一個變數到新 OptEngine
// 典型用途：建立子問題的初始快照、Benders 模型分發

// ── 限制式合併 ──────────────────────────────────────────────
OptEngine merged = controllerEngine.MergeModel(sourceEngine, targetEngine);
// 將 sourceEngine 的 thread constraints（未加入模型）加入 targetEngine 的 CPLEX 模型
// 注意：只合併 Thread 限制式，一般限制式不能跨模型使用

// ── 跨引擎 Thread 限制式 ────────────────────────────────────
// 使用 targetEngine 的 pool 變數建立限制式，加入 sourceEngine 的模型
// 建立的 IRange 用 Le/Ge/Eq（非 AddLe/AddGe/AddEq），暫不屬於任何模型
controllerEngine.CreateGreatEqualThread(value, name, targetEngine, sourceEngine);
controllerEngine.CreateLessEqualThread(value, name, targetEngine, sourceEngine);
controllerEngine.CreateEqualThread(value, name, targetEngine, sourceEngine);

// ── Thread 限制式重置 ────────────────────────────────────────
masterEngine.ResetThreadConstraint(threadEngine1, threadEngine2);
// 從 masterEngine 的 CPLEX 模型移除兩個子引擎的 thread 限制式
```

### Benders 典型流程

```csharp
// 初始化
var master = new MasterProblemEngine(config);
master.Build();
master.InitializeVariables(data);
master.BuildInitialModel();

var sub = new SubProblemEngine(config);
sub.Build();
sub.InitializeVariables(data);

double UB = double.MaxValue, LB = double.MinValue;
for (int iter = 1; iter <= maxIter; iter++)
{
    // 1. 求解主問題
    master.Solve();
    LB = master.GetObjectiveValue();
    var yFixed = master.GetYValues();

    // 2. 子問題重建（ResetConstraint 核心用途）
    sub.RebuildWithY(yFixed);   // 內部呼叫 sub.ResetConstraint()
    sub.Solve();

    double subObj = sub.GetObjectiveValue();
    UB = Math.Min(UB, fixedCost + subObj);

    // 3. 收斂判斷
    if (UB - LB <= epsilon) break;

    // 4. 取 dual，加 Benders cut 到主問題
    var supplyDuals = sources.ToDictionary(s => s, s => sub.GetSupplyDual(s));
    var demandDuals = dests.ToDictionary(d => d, d => sub.GetDemandDual(d));
    master.AddOptimalityCut(supplyDuals, demandDuals, iter);
}
```

### Thread 限制式設計注意

| 限制式類型 | 建立方式 | 是否在模型內 | 可否跨 model |
| --- | --- | --- | --- |
| 一般限制式 | `AddLE / AddGE / AddEQ` | ✅ 立即加入 | ❌ 不行 |
| Thread 限制式 | `CreateGreatEqualThread` 等 | ❌ 暫存 | ✅ 可以（未屬於任何 model） |

Thread 限制式建立後存入 `_threadConstraints`，需透過 `MergeModel()` 才會加入目標模型的 CPLEX model。

---

## 15. 常見錯誤 FAQ

**Q：`KeyNotFoundException: 找不到變數 'VariableB_ShiftAssign@...'`**

Variable class 的 property 宣告順序與 `BuildBVs` 傳入 sets 順序不一致。對照 Variable class 的 properties 逐一確認 set 傳入順序。

---

**Q：`ArgumentException: 【Parameter_Cost】期望 N 個參數，收到 0 個`**

使用 object initializer（`new Parameter_Cost { ... }`）但類別只有 `params object[]` 建構子，C# 以 0 個參數呼叫 → `InitClassBySets` 計數不符。

解法：改用帶參數建構子 `new Parameter_Cost("A", "D1", 2.0)`，或加上 `public Parameter_Cost() { }` 無參建構子。

---

**Q：`MultipleUseException: attempt to use modeling element in more than one model`**

試圖把已在某個 CPLEX model 中的 `IRange` 加入另一個 model。

原因：`_constraints`（一般限制式）已 attached 到 model，不能跨 model 使用。只有 `_threadConstraints`（Thread 限制式，用 `Le/Ge/Eq` 建立）可以跨 model。

解法：使用 `MergeModel()` 而非自行操作 `_constraints`。

---

### Q：Pool 加入後下一條被污染

每次 `CreateEqual / CreateLessEqual / CreateGreatEqual` 呼叫後 Pool 自動清空。
若中途要放棄一條未完成的限制式，呼叫 `engine.ClearPool()`。

---

### Q：`Solve()` 回傳 `false`，如何 debug

1. 啟用 `exportLP = true`，用 CPLEX Interactive Optimizer 或 CPLEX Studio 開啟 `.lp` 確認模型結構
2. 狀態為 `Infeasible` 時框架自動計算 IIS 並輸出到 `Output/IIS/*.ilp`，開啟即可看到衝突的限制式
3. 開啟該次 framework log 檔查看完整 CPLEX 輸出；需要即時觀察時再設 `enableLog = true`

---

### Q：CPLEX log 顯示亂碼

Framework log 固定格式為：

```text
yyyy-MM-dd HH:mm:ss | LEVEL | message
```

時間精度到秒；診斷事件使用 `CONSTRAINT_EMPTY`、`SOLVER_EXCEPTION` 這類純關鍵字，
不加 framework 前綴，也不自動附加 namespace。Event ID 後必須有簡短的人類可讀說明，
再以 `key=value` 記錄發生原因與處理結果，例如：

```text
2026-08-01 14:32:10 | WARN  | [CONSTRAINT_EMPTY] 未建立限制式 | name=Demand@D1 reason=pool_empty result=skipped
```

啟動前確認 Console 的 encoding：

```csharp
Console.OutputEncoding = System.Text.Encoding.UTF8;
```

框架的 `Logging` static constructor 已自動設定，但若在 `Logging` 呼叫前有其他輸出可能被蓋掉。
另外確認 source .cs 檔案本身以 UTF-8 儲存（VS → 另存新檔 → 儲存時選 UTF-8 with signature）。

---

### Q：Benders 子問題重建後 dual 為 0 或不正確

`Model.GetDual(IRange)` 在以下情況返回 0：

- 限制式未 binding（slack > 0）
- 模型尚未求解
- 求解為 MIP（dual 只在 LP 有效）

確認子問題只含連續變數（不含 Binary/Integer），CPLEX 才能提供有效 dual。
