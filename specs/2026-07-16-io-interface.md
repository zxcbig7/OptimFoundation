# OptimFoundation IO 介面規格（現況）

定位: `Core/IO/` 資料讀取與解輸出的權威行為描述。日期 2026-07-16，取代先前散在 developer-guide §4.5 / optset spec §7 的片段描述。
實作: `src/OptimFoundation.Core/IO/`。驗證: 94 xUnit（`DataSourceTests` / `NamedReadTests` / `SetBaseTests`）全綠 + Tutorial 三模式實跑 Optimal。

## 0. 設計天條（改動前必讀）

- **顯式勝過魔法**: Dataload ctor 每行一句自己讀檔，NOT 反射自動載入。曾有 `DataloadBase` 反射掃欄位，已廢除——唯一 paved path 不該有兩條路。
- **DB 只能 query**: `DbDataSource` 不提供「猜表名」慣例（`SELECT * FROM 表` 對真實 DB 太受限）；參數一律明寫 SQL。
- **set 名統一檔名形式** `Set_{X}`: 三來源邊界各自轉位址，使用端只認一種寫法。
- **錯誤左移**: 能在載入當下 fail fast 的就不留到 solve。

## 1. 統一讀取命名 `LoadParam` / `LoadSet`

共用方法名，第一引數是「該來源的位址」（CSV = 檔名、DB = SQL、InMemory = 忽略/key）：

```csharp
List<T> LoadParam<T>(...);      // 讀某參數型別全部列（typed，對 class 對位）
List<string> LoadSet(...);      // 讀一維 set（取第一欄）
DataTable LoadTable(...);       // 讀整張表 raw DataTable（set/param 以外的通用用途；僅 CSV / DB，不轉型）
```

**名稱可定址來源（CSV / InMemory）**共用介面 `IDataSource`：

```csharp
public interface IDataSource
{
    // file = 資料檔名（自由，省略 = 型別名）——檔名無限制，契約是「欄位對得上 class 的 property」，對不上即丟例外
    List<TParamClass> LoadParam<TParamClass>(string file = null) where TParamClass : ModelElementBase, new();

    // name = 邏輯名稱（檔名形式 Set_{X}），各實作自行解析位址
    List<string> LoadSet(string name);
}
```

`Dataload(IDataSource source)` 只依賴本介面 → CSV↔InMemory 換來源不動模型 code。**DB 是 query-only**（第一引數是 SQL 而非名稱，語意不同），故 `DbDataSource` **刻意不實作 `IDataSource`**，但共用 `LoadParam`/`LoadSet` 命名——避免「同名不同意」的漏抽象。

### 三個來源

| 來源 | LoadParam | LoadSet | 用途 |
| --- | --- | --- | --- |
| `CsvDataSource : IDataSource` | `LoadParam<T>("檔名")` → `Data/{檔名 或 型別名}.csv` | `LoadSet("Set_X")` → `Data/Set_X.csv` | 預設，讀 `Data/` |
| `InMemoryDataSource : IDataSource` | 以型別為 key（file 忽略） | key = `SetNaming.Logical(name)` | demo / 測試 / 程式生成 |
| `DbDataSource`（獨立） | `LoadParam<T>(sql, binds)` 明寫 SELECT | `LoadSet(sql, binds)` 明寫 SELECT | Oracle 等，query-only |

## 2. CSV 來源 `CsvDataSource`

- **建構即備好 `Data/` 資料夾**（`CsvDataSource()` → `FolderDir.Data.CreateFolder()`，idempotent）：引用 CSV 來源就把輸入資料夾建好（即使空的），使用者知道往哪放檔；缺檔的錯誤從 `DirectoryNotFound` 降為明確的 `FileNotFound`。與輸出端對稱（`Solution/` 等寫入時本就自動建）。
- 參數: `LoadParam<T>("任意檔名")` → `Data/任意檔名.csv`。**檔名自由、無命名限制**，契約是**欄位對得上 class 的 property**——帶表頭按名對位（大小寫不敏感、多餘欄忽略），表頭缺任一 property 對應欄即丟 `InvalidDataException`（`CsvCtrl.BuildParameter`）；無表頭則按 property 宣告順序。省略檔名 = 型別名（便利預設，非限制）。
- set: `LoadSet("Set_X")` → `Data/Set_X.csv`（一欄一列、無表頭）；`SetNaming.File` 補 `Set_` 前綴，故 `"X"` 與 `"Set_X"` 皆可。
- raw 表: `LoadTable("檔名")` → `Data/檔名.csv` 讀成 `DataTable`（第一列 = 欄名、全欄 string，不對 class 對位、不轉型）——set/param 以外的通用用途。
- 檔名帶不帶 `.csv` 皆可（`CsvCtrl.EnsureCsv` 統一補）。

## 3. DB 來源 `DbDataSource`（query-only，獨立於 IDataSource）

```csharp
public DbDataSource(IDbCtrl db);

// 讀參數：明寫 SELECT，欄名對 property（大小寫不敏感、多餘欄忽略），可用 AS 對名。與 CsvDataSource.LoadParam 同名
List<T> LoadParam<T>(string sql, params (string name, object value)[] parameters);

// 讀一維 set：明寫 SELECT 取第一欄。與 CsvDataSource.LoadSet 同名
List<string> LoadSet(string sql, params (string name, object value)[] parameters);

// 讀整張表 raw DataTable：直接回 IDbCtrl.Query 結果，不對位/不轉型。與 CsvDataSource.LoadTable 同名
DataTable LoadTable(string sql, params (string name, object value)[] parameters);
```

- **無 `tablePrefix` / `dataId` / `setSqlResolver`**（舊版有，全拔除）；多情境過濾自己寫進 SQL 的 `WHERE data_id = :id`。
- 為何 query-only: `SELECT * FROM 表` 做不到 join / 條件 / 投影 / `WHERE data_id`，「猜表名」的預先處理既不直觀又限制實用。
- 為何不實作 IDataSource: `IDataSource.LoadSet(name)` 對 CSV 是檔名、對 DB 會變成 SQL，同名不同意是漏抽象；分開讓型別系統擋掉 `SetBase.Load(db, "Set_X")` 這種 footgun。
- 欄名對位由私有 `MapRows` 完成；缺 property 對應欄 → `InvalidDataException`（列出需要 / 實得欄位）。
- DB set 進積木: `PRODUCT.LoadFrom(db.LoadSet("SELECT DISTINCT ..."))`（不經 `SetBase.Load`）。

## 4. set 名統一 `SetNaming`（internal）

```csharp
internal static class SetNaming
{
    public static string Logical(string name);  // 去 Set_ 前綴："Set_Product"→"Product"，"Product"→"Product"
    public static string File(string name);     // 補 Set_ 前綴："Product"→"Set_Product"，"Set_Product"→"Set_Product"
}
```

使用端一律用**檔名形式** `Set_{X}`（= 磁碟 `Set_X.csv`、Set 積木類名）。名稱可定址來源邊界轉位址: CSV `File` 讀檔、InMemory `Logical` 當 key。`"Product"` 與 `"Set_Product"` 一律等價。（DB 是 query-only，set 直接明寫 SQL，不經本類。）

## 5. Set 載入 `SetBase<T>`

`SetBase<T> : ISetBrick, IReadOnlyList<T>`（支援 `[i]`/`Count`/foreach/LINQ/`Contains`，免另存 List 視圖）。

```csharp
void Load(IDataSource source, string name = null);  // paved path（CSV/InMemory）：source.LoadSet(name ?? SetName) → ParseElement
void LoadInline(params T[] items);                  // 手打字面值
void LoadFrom(IEnumerable<T> items);                // 既有序列 / 程式生成 / enum / DB：LoadFrom(db.LoadSet(sql))
void LoadCsv(string fileName);                      // = CsvCtrl.ReadStrSet + ParseElement（便利入口）
```

`ParseElement` 支援 string / DateTime / int / long / double / decimal（與 `VariableBuilder` 同域）。

### 四道防呆（全丟明確例外）

| 情境 | 例外 |
| --- | --- |
| 未載入就 enumerate / `[i]` / `Count` | `InvalidOperationException` |
| 載入後為空集合 | `InvalidOperationException` |
| 二次載入（載入即封存） | `InvalidOperationException` |
| 重複成員 | `ArgumentException` |

## 6. 解輸出介面 `ISolutionSink`

```csharp
public interface ISolutionSink
{
    void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null);
}
```

| 實作 | 目的地 |
| --- | --- |
| `CsvSolutionSink` | `Solution/{變數型別}.csv`（帶表頭，可被 `CsvCtrl.BuildParameter` round-trip 讀回） |
| `OracleSolutionSink(db, tableName)` | Oracle 表（`OracleDBCtrl.SaveToDB`，array bind） |

`dataId` / `userId` 供多情境與稽核欄位；CSV 實作忽略、Oracle 實作寫入。

## 7. Dataload 使用範式

### 單一來源（CSV / InMemory）

```csharp
public Dataload(IDataSource source)
{
    LOT.Load(source, "Set_Lot");                                      // set：檔名形式
    parameter_ProcessTime = source.LoadParam<Parameter_ProcessTime>("Parameter_ProcessTime");
    ValidateSetsCoverParameters();                                    // 失同步驗證，手寫看得見
}
```

### 混合 / DB 來源（型別化 ctor，DB 明寫 SQL）

```csharp
public Dataload(CsvDataSource csv, DbDataSource db)
{
    LOT.Load(csv, "Set_Lot");                                         // 維度小表放 CSV
    parameter_ProcessTime = db.LoadParam<Parameter_ProcessTime>(      // DB 明寫 SQL，同名 LoadParam
        "SELECT lot, operation, eqp, proc_time AS qty FROM route WHERE data_id = :id", (":id", "V1"));
    ValidateSetsCoverParameters();
}
```

`ValidateSetsCoverParameters()` 是 Dataload 手寫的（非框架反射）: parameter 出現的值必須 ⊆ 對應 set，否則 `InvalidDataException`。跨來源不放寬。

## 8. 錯誤語意總表

| 來源 / 動作 | 失敗情境 | 結果 |
| --- | --- | --- |
| CsvDataSource.LoadParam | 檔案不存在（`Data/` 已由 ctor 建好） | `FileNotFoundException`（含路徑，指明缺哪個檔） |
| CsvDataSource.LoadParam | 表頭缺 property 對應欄 | `InvalidDataException`（列出需要 / 檔內表頭） |
| CsvDataSource.LoadParam | 檔案存在但空 | 靜默回空 list（**已知缺口**，靠 `ValidateSetsCoverParameters` 或後續 `First()` 才炸） |
| DbDataSource.LoadParam | 缺 property 對應欄 | `InvalidDataException` |
| DbDataSource.LoadParam / LoadSet | SQL 為空 | `ArgumentNullException` |
| InMemoryDataSource.LoadParam | 型別未註冊 | `KeyNotFoundException` |
| InMemoryDataSource.LoadSet | set 未註冊 | `KeyNotFoundException` |
| SetBase.Load | 空集合 / 重複 / 二次載入 / 未載入即用 | 見 §5 |
| Dataload | parameter 值不在 set | `InvalidDataException`（手寫） |

## 9. File Index

| 檔 | 內容 |
| --- | --- |
| `IO/IDataSource.cs` | `IDataSource`, `ISolutionSink` |
| `IO/CsvDataSource.cs` | `CsvDataSource`, `CsvSolutionSink` |
| `IO/DbDataSource.cs` | `DbDataSource`（query-only） |
| `IO/InMemoryDataSource.cs` | `InMemoryDataSource` |
| `IO/SetNaming.cs` | `SetNaming`（internal，set 名慣例單一真相） |
| `IO/CsvCtrl.cs` | `CsvCtrl`（static 讀寫底層：`BuildParameter` / `Read*Set` / `WriteSolution`） |
| `IO/OracleDBCtrl.cs` | `OracleDBCtrl : IDbCtrl`, `OracleSolutionSink` |
| `SetBase.cs` | `ISetBrick`（marker）, `SetBase<T>`（載入 + 防呆） |

## 10. 已知缺口 / 待辦

- CSV 空參數檔靜默回空 list（§8）——與 SetBase 的空集合防呆不對稱。若要補: `CsvCtrl.BuildParameter` 或 Dataload 端加「自動載入的 parameter 不得為空」檢查。
- `SetBase.LoadCsv` 仍直呼 `CsvCtrl.ReadStrSet`（未過 `SetNaming.File`）——與 `Load` 路徑的前綴正規化不完全一致；目前呼叫端都傳完整 `Set_X`，無實害，但可收斂。
