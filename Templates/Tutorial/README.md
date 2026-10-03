# OptimFoundation Tutorial Template

本範例示範現行 API：row-based Set/Parameter、統一 `Load<T>`、B/C/I Variable、owner-based constraint naming、`OptModel` / `OptProject`（`Solve` 與 `Experiment`）與 solution sink。

## 結構

```text
Tutorial/
├── Model/Tutorial_Model.md
├── Set/Set_*.cs
├── Parameter/Parameter_*.cs
├── Variable/Variable[B|C|I]_*.cs
├── Objective/ObjectiveFunction.cs
├── Constraint/Constraint_*.cs
├── Solution/TutorialSolution.cs
├── Data/Dataload.cs
├── Data/*.csv（範例資料；執行時一律讀 FolderDir.Input，不會自動複製）
└── Program.cs
```

## Set / Parameter

```csharp
[OptSet]
[OptDim<string>("Product")]
public sealed partial class Set_Product { }

[OptParam]
[OptDim<string>("Product")]
[OptDim<DateTime>("Date")]
public sealed partial class Parameter_Demand { }
```

Set 至少一維且沒有 `QTY`；Parameter 可零到多維並固定生成 `QTY`。

## CSV

所有 CSV 都有表頭：

```csv
# Set_Product.csv
Product
Desk
Chair
```

```csv
# Parameter_Demand.csv
Product,Date,QTY
Desk,2026-08-01,10
Chair,2026-08-01,12
```

## Dataload

```csharp
public Dataload(IDataSource source)
{
    set_Product = source.Load<Set_Product>("Set_Product");
    parameter_Demand = source.Load<Parameter_Demand>("Parameter_Demand");
}
```

CSV、InMemory 與 DB 都走 `IDataSource`。DB 的名稱引數是完整 SQL；需 bind parameters 時使用具體 `DbDataSource.Load<T>(sql, parameters)` overload。

raw import-data 若需要產出 Template CSV，Set 與 Parameter 都用：

```csharp
CsvCtrl.WriteRows(rows, "TypeName");
```

## Variable

```csharp
[OptVar]
[OptDim<string>("Product")]
[OptDim<DateTime>("Date")]
[OptDim<int>("Shift")]
public sealed partial class VariableC_Produce { }

engine.BuildVars<VariableC_Produce>(data.set_Product, data.set_Date, data.set_Shift);
```

前綴 B/C/I 分別是 Binary/Continuous/Integer。

## Constraint

```csharp
engine.AddLHS(1.0, variable);
engine.AddRHS(required);
engine.CreateGreaterEqual(this, product, date);
```

新 code 傳 owner 與原始維度值，framework 統一產生名稱。模型名稱日期為 `yyyy_MM_dd`，帶時分秒時為 `yyyy_MM_dd_HH_mm_ss`；CSV 日期對應為 `yyyy-MM-dd` 與 `yyyy-MM-dd HH:mm:ss`。

## 執行

```powershell
dotnet run
dotnet run -- exp
dotnet run -- read-model Tutorial_LP_<時間戳>.lp
dotnet run -- read-model Tutorial_LP_<時間戳>.lp exp
```

CLI 是兩軸自由組合：

- 模型來源：預設讀 CSV 建構 canonical；`read-model <file>` 改讀既有模型檔（.lp / .mps / .sav，相對路徑以 `FolderDir.Model` 為基準），不讀 CSV。
- 執行方式：預設正式求解並驗證/輸出（read-model 沒有資料，不跑解驗證）；`exp` 使用同一模型比較 solver config。read-model 的實驗名會加上模型名，累積檔裡跟 canonical 同一輪的紀錄分得開（Experiment 欄不同）。

兩條來源共用同一組 `projectConfig` / `productionBaseline`，新舊模型的 Trial 可以直接對照。

## 驗收

- class、CSV 與 Dataload 一一對應。
- CSV 表頭與 property 名稱、型別、順序一致。
- `BuildVars` domain 與 Variable 維度一致。
- constraints 可逐條反推回 Model.md。
- build、tests 與小型求解通過。
