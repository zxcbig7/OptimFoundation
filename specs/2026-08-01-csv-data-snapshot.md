---
title: CsvCtrl 讀寫對稱化——把載好的 set / parameter 積木寫回 Data/，成為下次計算的標準輸入
status: shipped
created: 2026-08-01
updated: 2026-08-01
modules: [core, io, templates, tests]
---

# CsvCtrl 資料輸出（讀寫對稱化）

## Summary

替 `CsvCtrl` 補上與現有讀取端對稱的**寫入端**：`WriteSet` / `WriteParam<T>`，把已經載好的 set 成員與 parameter 列寫成 CSV，**寫到 `Data/`——也就是 `CsvCtrl` 所有讀取方法既有的來源位置**（`bin/Debug/net8.0/Data`）。

服務的是**兩階段資料流**——不規則來源只解析一次，之後全走標準接口：

```text
第一階段  原始不規則來源 → 專案自寫 parse → set/param 積木 →〔本規格：寫回 Data/〕
第二階段  Data/*.csv → CsvDataSource（IDataSource）→ 同一組積木
```

因為輸出位置就是既有的輸入位置，第二階段**什麼都不用改**——`new CsvDataSource()` 原封不動就讀得到。前後兩個階段的載入邏輯是專案自己的 `Dataload` ctor，框架不介入；本規格只做中間那個箭頭。

## Motivation / Why

現況框架的資料流是**單向唯讀**：`IDataSource` 讀進來 → `SetBase.LoadFrom` 封存 → `DataContext.ValidateData` 驗一次 → 求解。輸出端只有解（`ISolutionSink`）與實驗指標（`IExperimentWriter`），**輸入資料本身沒有任何落地路徑**。

痛點：

- 原始資料格式亂（要 pivot / join / 去重 / 算衍生欄），整理邏輯每次計算都得跑一遍，也沒辦法交給別人用標準方式讀
- DB 來源的資料會變，今天的解明天重現不了，「模型改了」與「資料變了」混在一起
- 看不到 `LoadFrom` / `LoadInline` 程式生成的 set 實際長什麼樣，debug 與寫測試 fixture 都不方便

`CsvCtrl` 已經有 `ReadStrSet` / `BuildParameter` / `WriteSolution`，讀寫格式的慣例（表頭按名對位、數值 `"R"` InvariantCulture、UTF-8 BOM）也都定好了——缺的只是「把輸入資料也寫出去」這半邊。補齊即可，不需要新資料夾、不需要新抽象層。

## Scope

### In Scope

- `CsvCtrl.WriteSet(ISetBrick set, string fileName = null)`——單欄無表頭，寫到 `Data/Set_{名}.csv`
- `CsvCtrl.WriteParam<TParameter>(IReadOnlyList<TParameter> rows, string fileName = null)`——表頭 = property 名，寫到 `Data/{型別名}.csv`
- Templates 示範（`Sudoku_SHC279` 兩階段 `Dataload` + `Export`）
- xUnit round-trip 與兩階段等價測試

### Out of Scope

- **不新增資料夾**——不做 `FolderDir.Snapshot` / `SnapshotOf`，輸出就落在既有的 `Data/`
- **不動讀取端簽名**——`ReadStrSet` / `BuildParameter` 等維持原樣，不加 folder 參數
- **不動 `CsvDataSource`**——維持只讀 `Data/`
- **不做多版本保存**——每次輸出覆寫同一組 `Data/*.csv`。要留版本自己 copy 資料夾（見 Open Questions）
- **不做 manifest / SHA-256 / 比對擋下**——本次明確砍掉
- **不做框架級收集器**（`CsvSnapshot` 之類）——呼叫端一行一個積木明寫，與 `Dataload` 顯式 ctor 同一哲學
- **不動 `SetBase` / `ParameterBase` / `ModelElementBase` / `DataContext`**——輸入資料維持不可變（載入即封存），本功能全程唯讀消費
- **不寫 DB**——維持 DB query-only 天條

## User Stories / Use Cases

1. As a 建模者，我拿到的原始資料格式很亂，我想**只寫一次**整理邏輯、輸出成標準 CSV，之後每次計算都用標準接口讀，不必再碰那堆 parse code。
2. As a 建模者（DB 來源），我想把這次從 DB 拉的資料落成 CSV，之後不連 DB 也能用同一份資料重跑。
3. As a 開發者，我想看到 `LoadFrom` / `LoadInline` 程式生成的 set 實際內容，用來 debug 與產生測試 fixture。

## Acceptance Criteria

- [x] `CsvCtrl.WriteSet` 寫出的檔可被 `CsvCtrl.ReadStrSet` / `SetBase.Load` 原樣讀回，**成員順序完全一致**
- [x] `CsvCtrl.WriteParam<T>` 寫出的檔可被 `CsvCtrl.BuildParameter<T>` 原樣讀回，**每列每個 property 值一致**（含非 index、非數值欄）
- [x] round-trip 後重建的 `DataContext` 通過 `ValidateData`，且 set 數量、成員、param 列數與原始一致
- [x] **兩階段等價**：同一份資料走「不規則來源 + 自寫 parse 的 ctor」與走「輸出的 CSV + `new CsvDataSource()`」，兩邊建出的 `DataContext` 積木內容完全相同
- [x] 第二階段**零改動**——`new Dataload(new CsvDataSource())` 不需任何新參數即讀得到輸出的檔
- [x] `double` 值走 `"R"` + InvariantCulture、`DateTime` 走 `yyyy-MM-dd`，read-back 後數值不失真
- [x] `WriteParam` 的欄位來源用 `Type.GetProperties()`（與 `BuildParameter` / `InitClassBySets` 同一來源），**不得**用 `ReflectionHelper.GetMemberNames`
- [x] 含逗號 / 引號的字串欄寫出後讀回值不變（quoting 與 `SplitLine` 對稱）。**前後空白**：set 保留，parameter 會被既有 `BuildParameter` 的 `.Trim()` 移除——既有讀取行為，不在本次改動範圍
- [x] `CsvCtrl` 既有簽名一行不動，現有 157 個 xUnit 測試全過

## Module Interactions

本功能全在 OptimFoundation，無 frontend / API / DB 異動，也無新檔案（除測試）。

- **Core/IO — `CsvCtrl`**：新增 `WriteSet` / `WriteParam<T>`。既有讀取方法**不動**
- **Core — `FolderDir`**：**不改**。沿用既有 `FolderDir.Data`（`ProjectPath` = `AppDomain.CurrentDomain.BaseDirectory`，即 `bin/Debug/net8.0/`）
- **Core/IO — `CsvDataSource`**：**不改**
- **Core — `SetBase`**：**不改**。`WriteSet` 透過既有 `ISetBrick.Count` / `MembersAsObjects()` 唯讀取值，檔名走 `GetType().Name`（`SetName` 只在 `SetBase<T>` 上，介面沒有）
- **Core — `DataContext`**：**不改**
- **Templates — `Sudoku_SHC279`**：`Dataload` 補成兩階段（不規則來源 ctor + `IDataSource` ctor + `Export`），`Program.cs` 示範。現況 `Dataload()` 只是轉呼叫 `Dataload(new CsvDataSource())`，兩條路是同一條，示範不出兩階段
- **Templates — `Sudoku_SHC279.csproj`**：資料複製 glob 由 `Data\*.csv` 放寬為 `Data\**\*.csv`，讓不規則來源可放 `Data/raw/`
- **Templates — `Sudoku_SHC279/Program.cs`**：`Main` 收 `args`，`import <rawFile>` 走第一階段，無參數維持原本求解路徑
- **Tests — `OptimFoundation.Cplex.Tests/Unit/CsvWriteRoundTripTests.cs`**：15 個 round-trip / 防呆 / 對位測試

## API Design

### CsvCtrl 新增（框架端全部改動就這兩個）

```csharp
// 寫一維 set：單欄、無表頭、保序 → Data/{fileName}.csv
// fileName 省略時用 set.GetType().Name（= "Set_Row" 這個檔名形式），再過 SetNaming.File 補正
// NOTE: ISetBrick 介面上沒有 SetName（那是 SetBase<T> 才有），故用 GetType().Name——SetBase 因此不必改
public static void WriteSet(ISetBrick set, string fileName = null);

// 寫 parameter 列：第一列表頭 = property 名（大寫），其後每列一筆 → Data/{fileName}.csv
// fileName 省略時用型別名——與 BuildParameter 的預設一致
// 欄位來源 = typeof(TParameter).GetProperties()，與 BuildParameter 對稱
public static void WriteParam<TParameter>(IReadOnlyList<TParameter> rows, string fileName = null)
    where TParameter : ModelElementBase;
```

寫入位置固定 `FolderDir.Data`，與 `ReadStrSet` / `BuildParameter` 等所有讀取方法同一個位置——這就是「輸出即下次的輸入」的機制本身。

### 專案端 paved path：兩階段 Dataload

框架不介入前後兩階段的載入邏輯，但 Template 要示範標準寫法——**同一個 `Dataload` 兩條 ctor + 一個 `Export`**：

```csharp
public partial class Dataload : DataContext
{
    public Set_Row ROW = new();
    public Set_Column COLUMN = new();
    public Set_Digit DIGIT = new();
    public List<Parameter_Given> parameter_Given = new();

    // 第一階段：吃不規則來源（9x9 題盤矩陣，0 = 空格），專案自己 parse 成長格式
    // rawFile 相對於 Data/（ReadMatrixCsv 的位址慣例），例：raw/Puzzle_SHC279
    public Dataload(string rawFile)
    {
        var grid = CsvCtrl.ReadMatrixCsv(rawFile);
        int rows = grid.GetLength(0);
        int cols = grid.GetLength(1);

        ROW.LoadFrom(Enumerable.Range(1, rows));
        COLUMN.LoadFrom(Enumerable.Range(1, cols));
        DIGIT.LoadFrom(Enumerable.Range(1, rows));

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (grid[r, c] > 0)
                    parameter_Given.Add(new Parameter_Given(r + 1, c + 1, (int)grid[r, c]));
    }

    // 第二階段：標準接口，什麼都不用想
    public Dataload(IDataSource source)
    {
        ROW.Load(source, "Set_Row");
        COLUMN.Load(source, "Set_Column");
        DIGIT.Load(source, "Set_Digit");
        parameter_Given = source.LoadParam<Parameter_Given>("Parameter_Given");
    }

    // 中間那個箭頭：把積木寫回 Data/，成為第二階段的輸入。一行一個積木，明寫。
    // 檔名 MUST 與第二階段 ctor 讀取時用的名稱一致。
    public void Export()
    {
        CsvCtrl.WriteSet(ROW, "Set_Row");
        CsvCtrl.WriteSet(COLUMN, "Set_Column");
        CsvCtrl.WriteSet(DIGIT, "Set_Digit");
        CsvCtrl.WriteParam(parameter_Given, "Parameter_Given");
    }
}
```

呼叫端：

```csharp
// 第一次：解析不規則來源 → 寫回 Data/（Template 走 dotnet run -- import raw/Puzzle_SHC279）
var data = OptData.Load(() => new Dataload("raw/Puzzle_SHC279"));
data.Export();

// 之後每次：只走標準接口，零參數
var data2 = OptData.Load(() => new Dataload(new CsvDataSource()));
```

`Export` 是**專案端的方法**（Template 示範用），框架只提供它裡面呼叫的 `CsvCtrl.WriteSet` / `WriteParam`——不做框架級收集器。

## Data Model

無 DB schema 異動。輸出位置 = 既有輸入位置（`ProjFolder.ProjectPath` = `AppDomain.CurrentDomain.BaseDirectory`）：

```text
bin/Debug/net8.0/Data/
  Set_Row.csv
  Set_Column.csv
  Set_Digit.csv
  Parameter_Given.csv
```

**set 檔**（單欄、無表頭、保序，檔名 `Set_{名}.csv` 與 `SetNaming.File` 一致）：

```csv
R1
R2
R3
```

**param 檔**（第一列表頭 = property 名大寫，與 `BuildParameter` 按名對位相容）：

```csv
ROW,COLUMN,DIGIT,QTY
R1,C1,D5,1
R1,C3,D7,1
```

## Edge Cases & Error Handling

- **覆寫既有輸入檔**：寫入位置就是讀取位置，同名檔直接覆寫——這是本功能的機制，不是意外。但寫入前後在 log 印出「覆寫了哪個檔」，讓誤覆寫看得見
- **build 的 `PreserveNewest` 會不會把輸出蓋回去**：Template csproj 有 `<None Update="Data\**\*.csv" CopyToOutputDirectory="PreserveNewest" />`（本次由 `Data\*.csv` 放寬，讓 `Data/raw/` 也被複製）。輸出的檔時間戳較新 → 下次 build **不會**被專案原始 `Data/` 蓋掉；但若之後手改了專案原始 CSV（變更新）→ build 時會蓋掉輸出。行為合理（手改優先），但要寫進 developer-guide 免得踩到
- **檔名不一致導致讀回失敗**：`WriteSet` / `WriteParam` 的 `fileName` 必須與第二階段 ctor 讀取時給的名稱相同（Sudoku 給 `"Set_Row"` / `"Parameter_Given"`）。省略 `fileName` 時 set 用 `Set_{SetName}`、param 用型別名——與讀取端預設慣例一致，明寫過檔名的才需要一起帶
- **未載入的 set**：`ISetBrick.Count` / `MembersAsObjects()` 會丟 `InvalidOperationException`（既有防呆），`WriteSet` 不預先檢查，讓既有錯誤訊息原樣浮出
- **`DateTime` set 帶時分秒**：一律寫 `yyyy-MM-dd`，與 `ModelElementBase.ToString()` 的變數 key 格式一致——index set 本來就不支援時分秒粒度。`WriteSet` 偵測到非午夜時間即丟例外，NEVER 靜默截斷
- **`double` 精度**：一律 `ToString("R", InvariantCulture)`（與 `WriteSolution` 同）。`NaN` / `Infinity` 在 `DataValidator` 的 numeric sanity 已被擋，此處不另處理
- **含逗號 / 引號 / 前後空白的字串欄**：走與 `SplitLine` 對稱的 quoting——含這三者之一即用 `"` 包住，內含的 `"` 跳脫成 `""`
- **`ReflectionHelper.GetMemberNames` 的陷阱**：它會撈 field + static member，與 `BuildParameter` 用的 `GetProperties()` 不是同一組。`WriteParam` MUST 用 `GetProperties()`，否則 round-trip 欄位對不上
- **空 param 列表**：仍寫出只有表頭的檔（讀回為空 list）
- **`Data/` 不存在**：寫入前 `CreateDirectory`（idempotent，沿用 `ProjFolder.TryCreateFile` 的既有行為；`CsvDataSource` ctor 本來就會建）

## Non-Functional Requirements

- **Performance**：寫入為 O(總列數)，單次求解資料量（萬列級）目標 < 1s
- **相容性**：`net8.0`；`CsvCtrl` 既有簽名一行不動，既有呼叫端零改動
- **不可變性**：`WriteSet` / `WriteParam` 全程唯讀消費 `ISetBrick` 與 `IReadOnlyList<T>`，NEVER 提供任何寫回 set / param 的路徑
- **Observability**：每次寫出印 `[CsvCtrl] data written: {path}（rows=N, overwritten=true/false）`，與既有 `WriteSolution` 的 log 格式一致
- **Security**：輸出是明文 CSV，NEVER 把連線字串 / 帳密寫進任何輸出

## Open Questions

- [ ] 多版本保存（想留住「上次那份」而不是被覆寫）目前要靠自己 copy `Data/` 資料夾。若之後真有需求，再談要不要加輸出資料夾參數——本次不做
- [ ] 之後若要「偵測資料變了」（hash 比對擋下），是在 `CsvCtrl` 加還是另開一層？本次不做

## Implementation Plan

### Stub 階段（先做）

- [x] `CsvCtrl`：加 `WriteSet` / `WriteParam<T>` 簽名，body `throw new NotImplementedException()`
- [x] `Sudoku_SHC279/Data/Dataload.cs`：加第一階段 ctor 與 `Export()` 簽名，body TODO
- [x] 跑 `dotnet build` 確認全解決方案編譯過、既有測試仍通過（157 passed）

### 逐層實作

- [x] `CsvCtrl.WriteSet`（含 quoting、`DateTime` 格式、非午夜時間防呆、覆寫 log）
- [x] `CsvCtrl.WriteParam<T>`（`GetProperties()` 對位、表頭、`"R"` 數值格式）
- [x] Templates：`Sudoku_SHC279` 兩階段 `Dataload` + `Export` 實作，`Program.cs` 示範
- [x] Tests：set round-trip（含 `DateTime` set）、param round-trip（含字串欄 / 含逗號欄 / 空列表）、**兩階段等價**（自寫 parse vs 讀 CSV 建出的積木相同）、重跑 Sudoku 得同一解
- [x] 更新 `CodeMap.md` 與 `specs/developer-guide.md`（新增資料輸出章節 + `PreserveNewest` 注意事項）

## References

- 受影響的既有規格：`specs/2026-07-16-io-interface.md`（IDataSource / ISolutionSink 抽象）、`specs/2026-07-18-framework-data-guard.md`（DataContext 註冊與驗證）
- 相關設計討論（2026-08-01 對話）逐步收斂的結果：
  1. set / parameter 維持唯讀，需要的是單向 export 而非讀寫
  2. 開發方向定為擴充 `CsvCtrl`，而非新增走 `DataContext` registry 的 `DataSnapshot`（registry 的 `ParamRow` 只存 index + double 欄，會漏非數值欄）
  3. manifest / hash / `Verify` 比對機制**不做**
  4. 輸出位置就是 `Data/`（`bin/Debug/net8.0/Data`）當 IO 的 I——因此不需要新資料夾、不需要讀取端 folder 參數、不需要 `CsvDataSource` 新 ctor
