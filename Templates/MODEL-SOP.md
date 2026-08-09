# OptimFoundation Model Template SOP

## 1. Set

一維與多維 Set 使用同一種寫法：

```csharp
[OptSet]
[OptDim<int>("A")]
public sealed partial class Set_A { }

[OptSet]
[OptDim<string>("From")]
[OptDim<string>("To")]
public sealed partial class Set_Arc { }
```

Set 至少一維。每個 `OptDim` 生成一個 property；多維 Set 表示實際存在的組合。

## 2. Parameter

```csharp
[OptParam]
[OptDim<string>("From")]
[OptDim<string>("To")]
public sealed partial class Parameter_ArcCost { }
```

Parameter 可零到多維，generator 最後固定加 `double QTY`。scalar Parameter 只寫 `[OptParam]`。

## 3. Variable

```csharp
[OptVar]
[OptDim<string>("From")]
[OptDim<string>("To")]
public sealed partial class VariableB_UseArc { }
```

| 前綴 | 型別 | 一般建立方式 |
| --- | --- | --- |
| `VariableB_` | Binary | `engine.BuildVars<T>(...)` |
| `VariableC_` | Continuous | `engine.BuildVars<T>(...)` |
| `VariableI_` | Integer | `engine.BuildVars<T>(...)` |

## 4. CSV

所有 CSV 都有表頭：

```csv
# Set_A.csv
A
1
2
```

```csv
# Set_Arc.csv
From,To
A,B
B,C
```

```csv
# Parameter_ArcCost.csv
From,To,QTY
A,B,12.5
B,C,8
```

scalar Parameter：

```csv
QTY
0.95
```

## 5. Dataload

```csharp
public sealed partial class Dataload : DataContext
{
    public List<Set_Arc> set_Arc = new();
    public List<Parameter_ArcCost> parameter_ArcCost = new();

    public Dataload() : this(new CsvDataSource()) { }

    public Dataload(IDataSource source)
    {
        set_Arc = source.Load<Set_Arc>("Set_Arc");
        parameter_ArcCost = source.Load<Parameter_ArcCost>("Parameter_ArcCost");
    }
}
```

Set 與 Parameter 統一由 `Load<T>` 依欄位讀取與轉型。

## 6. Constraint

```csharp
engine.AddLHS(coef, variableSpec);
engine.AddRHS(value);
engine.CreateLessEqual(this, dim1, dim2);
```

Model.md 左右側原樣放進 `AddLHS` / `AddRHS`。新 code 傳 owner 與原始維度值，由 framework 建立限制式名稱。

## 7. Program

```csharp
var data = OptData.Load(() => new Dataload());

var model = new OptModel("Canonical")
    .AddVariables(e => e.BuildVars<VariableB_UseArc>(data.set_Arc))
    .AddObjective(e => new ObjectiveFunction(/* dependencies */).Build(e))
    .AddConstraints(e => new Constraint_Flow(/* dependencies */).Build(e));
```

## 8. 驗收

- Set / Parameter class、CSV 與 Dataload 一一對應。
- `OptDim` 名稱、型別、順序與 CSV 表頭一致。
- Variable 維度總寬度與 `BuildVars` 傳入 domain 一致。
- 限制式可反向翻譯回 Model.md 原式。
- `dotnet build`、tests 與小型求解全過。
