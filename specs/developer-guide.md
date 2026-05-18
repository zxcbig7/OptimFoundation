# OptimFoundation 開發手冊

> 給基於此框架開發新最佳化問題的開發者

---

## 快速開始

新建一個最佳化問題只需完成以下四件事：

1. 定義變數（Variable class）
2. 定義資料（Parameter class + Dataload）
3. 建立變數（VariableCreate）
4. 寫限制式與目標式（Constraint class）

---

## Step 1 — 定義變數

在 `VariablesClass/` 下新增 class，繼承對應的 base class，**只需宣告 properties，不寫任何建構子**。

| Base class | 用途 | 命名建議 |
|------------|------|----------|
| `VariableBase` (Binary) | 0/1 變數 | `VariableB_Xxx` |
| `VariableBase` (Integer) | 整數變數 | `VariableI_Xxx` |
| `VariableBase` (Continuous) | 連續變數 | `VariableX_Xxx` |

```csharp
// 三維變數：日期 × 員工 × 班別
public class VariableB_ShiftAssign : VariableBase
{
    public DateTime Date     { get; set; }
    public string   Employee { get; set; }
    public string   Group    { get; set; }
}

// 一維變數：員工
public class VariableX_BelowAVG : VariableBase
{
    public string Employee { get; set; }
}
```

> **重要**：Properties 的宣告順序決定變數的 key 格式。  
> `VariableB_ShiftAssign` 的 key 為 `VariableB_ShiftAssign@2026-01-01@E1@D`

---

## Step 2 — 定義資料

### Parameter class

在 `Data/` 下新增 class，繼承 `ParameterBase`，**只需宣告 properties，不寫建構子**。

```csharp
public class Parameter_ShiftDemand : ParameterBase
{
    public DateTime Date  { get; set; }
    public string   Group { get; set; }
    public double   QTY   { get; set; }
}
```

### Dataload

在 `Data/Dataload.cs` 中定義 Sets（`List<T>`）和 Parameters（`List<Parameter_Xxx>`），並在建構子中初始化。

```csharp
public class Dataload
{
    // Sets
    public List<string>   Employee = new();
    public List<string>   Group    = new();
    public List<DateTime> Date     = new();

    // Parameters
    public List<Parameter_ShiftDemand> parameter_ShiftDemand = new();

    public Dataload()
    {
        // 初始化 Sets
        Employee.AddRange(new[] { "E1", "E2", "E3" });
        Group.AddRange(new[] { "O", "D", "E", "N" });

        // 初始化 Parameters（使用 object initializer）
        parameter_ShiftDemand.Add(new Parameter_ShiftDemand
        {
            Date  = new DateTime(2026, 1, 1),
            Group = "D",
            QTY   = 5
        });
    }
}
```

---

## Step 3 — 建立變數

在 `VariablesClass/VariableCreate.cs` 的 `Build()` 中呼叫 engine 的批次建立方法。

```csharp
public void Build()
{
    // BuildBVs = Binary Variables
    optEngine.BuildBVs<VariableB_ShiftAssign>(dataload.Date, dataload.Employee, dataload.Group);
    optEngine.BuildBVs<VariableB_Off1Day>(dataload.Date, dataload.Employee);

    // BuildIVs = Integer Variables（可選填 LB, UB）
    optEngine.BuildIVs<VariableI_WorkCount>(dataload.Employee);
    optEngine.BuildIVs<VariableI_WorkCount>(0, 30, dataload.Employee); // LB=0, UB=30

    // BuildCVs = Continuous Variables
    optEngine.BuildCVs<VariableX_BelowAVG>(dataload.Employee);
}
```

> Sets 傳入的順序須與 Variable class 的 property 宣告順序一致。

---

## Step 4 — 寫限制式

### 4.1 建立 Project 中介 base class

每個 project 建立一個 abstract base class，讓所有限制式繼承，避免重複宣告 engine 和 data。

```csharp
// Constraints/RosterConstraintBase.cs
public abstract class RosterConstraintBase : ConstraintBase
{
    protected OptEngine Engine { get; set; }
    protected Dataload   Data   { get; set; }
    public abstract void Build();
}
```

### 4.2 撰寫限制式

每個限制式繼承中介 base，只需實作 `Build()`。

```csharp
public class Constraint_OneGroup : RosterConstraintBase
{
    public override void Build()
    {
        Data.Date.ForEach(d =>
        {
            Data.Employee.ForEach(e =>
            {
                // 建立 LHS
                Data.Group.ForEach(g =>
                    Engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = g }));

                // 建立 RHS
                Engine.AddRHS(1);

                // 送出限制式
                Engine.CreateEqual($"{ConstraintName}@{d:yyyy_MM_dd}@{e}");
                ConstraintCount++;
            });
        });

        Logging.Info($"{ConstraintName} 共：{ConstraintCount} 條");
    }
}
```

### 4.3 Pool API 說明

限制式的建構方式是先把左右兩側的項目丟入 Pool，再呼叫 `Create*` 送出。

```
AddLHS(係數, 變數)     ← 左側加入變數項
AddLHS(常數)           ← 左側加入常數
AddRHS(係數, 變數)     ← 右側加入變數項
AddRHS(常數)           ← 右側加入常數
```

送出限制式（呼叫後 Pool 自動清空）：

| 方法 | 數學意義 |
|------|----------|
| `CreateEqual(name)` | LHS = RHS |
| `CreateLessEqual(name)` | LHS ≤ RHS |
| `CreateGreatEqual(name)` | LHS ≥ RHS |
| `CreateRange(lb, ub, name)` | lb ≤ LHS ≤ ub |

**範例：**

```csharp
// LHS: SixDayWork(d,e)
// RHS: 1 - ShiftAssign(sd,e,"O")
// 限制：SixDayWork ≤ 1 - ShiftAssign_O

Engine.AddLHS(1, new VariableB_SixDayWork { Date = d, Employee = e });
Engine.AddRHS(1);
Engine.AddRHS(-1, new VariableB_ShiftAssign { Date = sd, Employee = e, Group = "O" });
Engine.CreateLessEqual($"{ConstraintName}@{d:yyyy_MM_dd}@{e}");
```

### 4.4 目標式

```csharp
public class ObjectiveFunction : RosterConstraintBase
{
    public override void Build()
    {
        Data.Date.ForEach(d =>
            Data.Employee.ForEach(e =>
                Engine.AddLHS(Data.Penalty_SixDay, new VariableB_SixDayWork { Date = d, Employee = e })));

        Data.Employee.ForEach(e =>
            Engine.AddLHS(Data.Penalty_BelowAVG, new VariableX_BelowAVG { Employee = e }));

        Engine.CreateMinimize(); // 或 CreateMaximize()
    }
}
```

---

## Step 5 — 組裝與執行

### BuildModel.cs

```csharp
public class BuildModel
{
    private OptEngine engine;
    private Dataload  dataload;

    public BuildModel(OptEngine engine, Dataload dataload)
    {
        this.engine   = engine;
        this.dataload = dataload;
    }

    public void Build()
    {
        var constraints = new List<RosterConstraintBase>
        {
            new ObjectiveFunction   { Engine = engine, Data = dataload },
            new Constraint_OneGroup { Engine = engine, Data = dataload },
            new Constraint_SixDay   { Engine = engine, Data = dataload },
            // ...
        };
        constraints.ForEach(c => c.Build());
    }
}
```

### 問題入口（Execute）

```csharp
public bool Execute()
{
    var config = new CplexConfig
    {
        epGap       = 0.03,
        timeLimit   = 300,
        workThreads = 8,
        enableLog   = true,
        exportSol   = true,
        exportLP    = true,
        projectName = "MyProblem"
    };

    optEngine = new OptEngine(config);
    optEngine.Build();

    new VariableCreate(dataload, optEngine).Build();
    new BuildModel(optEngine, dataload).Build();

    return optEngine.Solve();
}
```

---

## 輸出檔案

`Solve()` 完成後，輸出檔案依 `projectName` 和執行時間戳記命名，存放於以下目錄：

| 目錄 | 內容 |
|------|------|
| `Output/Model/` | LP 模型（`*.lp`）、MPS 模型（`*.mps`） |
| `Output/Sol/` | 解值（`*.sol`） |
| `Output/IIS/` | Infeasible 時的衝突子集（`*.ilp`） |
| `Output/Logs/` | 執行 log（`*.txt`） |

---

## 取得求解結果

```csharp
// 取得特定變數類型的所有解值
var solution = optEngine.GetSetVarValues<VariableB_ShiftAssign>();
// 回傳 Dictionary<string, double>，key = 變數名稱，value = 解值

// 取得單一變數解值
double val = optEngine.GetVariableValue("VariableB_ShiftAssign@2026-01-01@E1@D");
```

---

## 常見問題

**Q：變數建立後查不到（KeyNotFoundException）**  
確認 Variable class 的 property 宣告順序與 `BuildBVs` 傳入 set 順序相同。

**Q：限制式跑完後 Pool 沒清空，下一條被污染**  
每次 `CreateEqual / CreateLessEqual / CreateGreatEqual` 呼叫後 Pool 會自動清空，不需手動清。  
若中途要放棄一條未完成的限制式，呼叫 `engine.ClearPool()`。

**Q：Solve() 回傳 false，如何 debug**  
啟用 `exportLP = true` 確認模型結構，若狀態為 Infeasible 框架會自動計算 IIS 並輸出到 `Output/IIS/`。
