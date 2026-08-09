# OptimFoundation Tutorial Template

本範例示範現行 API：row-based Set/Parameter、統一 `Load<T>`、B/C/I Variable、owner-based constraint naming、`OptModel` / `OptProject` 與 solution sink。

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
├── Data/*.csv
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

raw import 若需要產出 Template CSV，Set 與 Parameter 都用：

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
engine.CreateGreatEqual(this, product, date);
```

新 code 傳 owner 與原始維度值，framework 統一產生名稱。模型名稱日期為 `yyyy_MM_dd`；CSV 日期為 `yyyy-MM-dd`。

## 執行

```powershell
dotnet run
dotnet run -- exp
```

預設模式求解並驗證/輸出；`exp` 使用相同模型比較 solver config。

## 驗收

- class、CSV 與 Dataload 一一對應。
- CSV 表頭與 property 名稱、型別、順序一致。
- `BuildVars` domain 與 Variable 維度一致。
- constraints 可逐條反推回 Model.md。
- build、tests 與小型求解通過。
