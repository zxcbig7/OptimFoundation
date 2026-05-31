# OptimFoundation CPLEX Project 開發說明書

> API 細節請參閱 [developer-guide.md](developer-guide.md)。  
> 本文件專注於：**新建專案的目錄結構、各類別的完整範本、csproj 設定、開發 Checklist**。

---

## 1. 目錄結構

```
ProjectName/
├── ProjectName.csproj
├── Program.cs
├── ProblemName.cs                  Execute() 主流程
├── Data/
│   ├── Dataload.cs                 Sets、Parameters、資料初始化
│   └── Parameter_Xxx.cs            一個 Parameter 一個檔案
├── VariablesClass/
│   ├── VariableCreate.cs           BuildBVs / BuildIVs / BuildCVs
│   ├── VariableB_Xxx.cs            Binary 變數
│   ├── VariableI_Xxx.cs            Integer 變數（若有）
│   └── VariableX_Xxx.cs            Continuous 變數（若有）
└── Constraints/
    ├── BuildModel.cs               依序呼叫所有 Build()
    ├── ObjectiveFunction.cs        目標式
    └── Constraint_Xxx.cs           各限制式，一個一個檔案
```

---

## 2. csproj 設定

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <!-- OptimFoundation -->
  <ItemGroup>
    <ProjectReference Include="..\..\OptimFoundation\src\OptimFoundation.Core\OptimFoundation.Core.csproj" />
    <ProjectReference Include="..\..\OptimFoundation\src\OptimFoundation.Cplex\OptimFoundation.Cplex.csproj" />
  </ItemGroup>

  <!-- IBM CPLEX DLL（路徑依安裝版本調整） -->
  <ItemGroup>
    <Reference Include="ILOG.Concert">
      <HintPath>C:\IBM\ILOG\CPLEX_Studio2211\cplex\bin\x64_win64\ILOG.Concert.dll</HintPath>
    </Reference>
    <Reference Include="ILOG.CPLEX">
      <HintPath>C:\IBM\ILOG\CPLEX_Studio2211\cplex\bin\x64_win64\ILOG.CPLEX.dll</HintPath>
    </Reference>
  </ItemGroup>

  <!-- 選用：Oracle 資料來源 -->
  <ItemGroup>
    <PackageReference Include="Oracle.ManagedDataAccess.Core" Version="23.6.1" />
  </ItemGroup>
</Project>
```

---

## 3. 命名規則速查

| 類別 | 前綴 | 建立方法 | 數值欄位 |
|------|------|---------|---------|
| Binary variable | `VariableB_` | `BuildBVs<T>()` | 無 |
| Integer variable | `VariableI_` | `BuildIVs<T>()` | 無 |
| Continuous variable | `VariableX_` | `BuildCVs<T>()` | 無 |
| Parameter | `Parameter_` | — | `QTY`（最後一個） |
| Constraint | `Constraint_` | — | — |

**Variable key 格式**：`VariableB_ShiftAssign@2026-01-01@E1@D`（DateTime 固定 `yyyy-MM-dd`）

---

## 4. Variable 類別範本

```csharp
// VariablesClass/VariableB_ShiftAssign.cs
public class VariableB_ShiftAssign : VariableBase
{
    public DateTime Date     { get; set; }
    public string   Employee { get; set; }
    public string   Group    { get; set; }
    // ❌ 不寫任何建構子
}

// VariablesClass/VariableX_BelowAVG.cs
public class VariableX_BelowAVG : VariableBase
{
    public string Employee { get; set; }
}
```

**規則：只宣告 properties，property 順序 = `@` 分隔 key 的順序，不寫任何建構子。**

---

## 5. Parameter 類別範本

```csharp
// Data/Parameter_ShiftDemand.cs
public class Parameter_ShiftDemand : ParameterBase
{
    public DateTime Date  { get; set; }
    public string   Group { get; set; }
    public double   QTY   { get; set; }   // 數值欄位永遠放最後
    // ❌ 不寫任何建構子
}

// Data/Parameter_Budget.cs（Scalar：只有一筆）
public class Parameter_Budget : ParameterBase
{
    public double QTY { get; set; }
}
```

使用方式（object initializer）：
```csharp
new Parameter_ShiftDemand { Date = d, Group = "D", QTY = 5.0 }
new Parameter_Budget { QTY = 760000.0 }
```

---

## 6. Dataload 類別範本

```csharp
// Data/Dataload.cs
public class Dataload
{
    // ── Sets ─────────────────────────────────────────────
    public List<string>   Employee = new List<string>();
    public List<string>   Group    = new List<string>();
    public List<DateTime> Date     = new List<DateTime>();

    // ── Parameters ───────────────────────────────────────
    public List<Parameter_ShiftDemand> parameter_ShiftDemand = new List<Parameter_ShiftDemand>();

    // ── Scalar（罰分、設定常數）──────────────────────────
    public double Penalty_SixDay = 1.0;

    public Dataload()
    {
        // Sets 初始化
        Group.AddRange(new[] { "O", "D", "E", "N", "C" });
        for (int i = 1; i <= 16; i++) Employee.Add($"E{i}");

        int year = 2026, month = 1;
        for (int d = 1; d <= DateTime.DaysInMonth(year, month); d++)
            Date.Add(new DateTime(year, month, d));

        // Parameters 初始化
        Date.ForEach(d =>
        {
            parameter_ShiftDemand.Add(new Parameter_ShiftDemand { Date = d, Group = "D", QTY = 5 });
            parameter_ShiftDemand.Add(new Parameter_ShiftDemand { Date = d, Group = "N", QTY = 2 });
        });

        // 選用：從 CSV 讀取
        // this.Employee = CSVCtrl.ReadStrSet("Set_Employee.csv");
        // this.parameter_ShiftDemand = CSVCtrl.BuildParameter<Parameter_ShiftDemand>("Param_ShiftDemand");
    }

    public void WriteToCSV(OptEngine engine)
    {
        CSVCtrl.SaveToCSV<VariableB_ShiftAssign>(
            engine.GetSetVarSol<VariableB_ShiftAssign>(), DATA_ID: "V1", USER_ID: "USER");
    }
}
```

---

## 7. VariableCreate 類別範本

```csharp
// VariablesClass/VariableCreate.cs
public class VariableCreate
{
    private OptEngine optEngine;
    private Dataload  dataload;

    public VariableCreate(Dataload dataload, OptEngine engine)
    {
        this.dataload  = dataload;
        this.optEngine = engine;
    }

    public void Build()
    {
        // Binary：Set 順序必須對應 Variable class 的 property 宣告順序
        optEngine.BuildBVs<VariableB_ShiftAssign>(dataload.Date, dataload.Employee, dataload.Group);
        optEngine.BuildBVs<VariableB_SixDayWork> (dataload.Date, dataload.Employee);

        // Continuous
        optEngine.BuildCVs<VariableX_BelowAVG>(dataload.Employee);

        // Integer（含自訂界限）
        // optEngine.BuildIVs<VariableI_WorkCount>(0, 30, dataload.Employee);

        Logging.Info($"Variables created: {optEngine.varCount}");
    }
}
```

| 方法 | 預設 LB | 預設 UB |
|------|---------|---------|
| `BuildBVs<T>(sets…)` | 0 | 1 |
| `BuildCVs<T>(sets…)` | 0 | 1E100 |
| `BuildCVs<T>(lb, ub, sets…)` | 自訂 | 自訂 |
| `BuildIVs<T>(sets…)` | 0 | 1E100 |
| `BuildIVs<T>(lb, ub, sets…)` | 自訂 | 自訂 |

---

## 8. Constraint 類別範本

### Pool API 建構順序

```
AddLHS(coef, varObj)   → LHS 變數項
AddLHS(constant)       → LHS 常數（若有）
AddRHS(value)          → RHS 常數
AddRHS(coef, varObj)   → RHS 變數項（若有）
Create[Equal|LessEqual|GreatEqual](name)  → 送出，Pool 自動清空
```

**嚴格規則：AML 左側 → `AddLHS`，AML 右側 → `AddRHS`，禁止移項、改號。**

### 等式限制範本

```csharp
// Constraints/Constraint_FullfillDemand.cs
public class Constraint_FullfillDemand : ConstraintBase
{
    private OptEngine optEngine;
    private Dataload  dataload;

    public Constraint_FullfillDemand(Dataload dataload, OptEngine engine)
    {
        this.optEngine = engine;
        this.dataload  = dataload;
    }

    public void Build()
    {
        dataload.Date.ForEach(d =>
        {
            dataload.Group.Where(g => g != "O").ToList().ForEach(g =>
            {
                dataload.Employee.ForEach(e =>
                    optEngine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = g }));

                double demand = dataload.parameter_ShiftDemand
                    .FirstOrDefault(x => x.Date == d && x.Group == g)?.QTY ?? 0;
                optEngine.AddRHS(demand);
                optEngine.CreateEqual($"{ConstraintName}@{d:yyyy_MM_dd}@{g}");
                ConstraintCount++;
            });
        });
        Logging.Info($"[{ConstraintName}] {ConstraintCount}");
    }
}
```

### 不等式限制（RHS 含變數）範本

```csharp
public void Build()
{
    dataload.Date.ForEach(d =>
    {
        dataload.Employee.ForEach(e =>
        {
            dataload.parameter_NightToDay.ForEach(rule =>
            {
                var preD = d.AddDays(-1);
                // LHS: violation[d,e]
                optEngine.AddLHS(1, new VariableB_NightToDay { Date = d, Employee = e });
                // RHS: assign[d-1,e,preGroup] + assign[d,e,group] - 1
                optEngine.AddRHS(1, new VariableB_ShiftAssign { Date = preD, Employee = e, Group = rule.PreGroup });
                optEngine.AddRHS(1, new VariableB_ShiftAssign { Date = d,    Employee = e, Group = rule.Group });
                optEngine.AddRHS(-1);
                optEngine.CreateGreatEqual($"{ConstraintName}@{d:yyyy_MM_dd}@{e}");
                ConstraintCount++;
            });
        });
    });
    Logging.Info($"[{ConstraintName}] {ConstraintCount}");
}
```

### LINQ 讀取 Parameter 的正確寫法

```csharp
// ✅ 先存成變數，再傳入 AddLHS/AddRHS
var cost = dataload.parameter_Cost
    .FirstOrDefault(x => x.PRODUCT == p)?.QTY ?? 0.0;
optEngine.AddLHS(cost, new VariableX_Amount { PRODUCT = p });

// ❌ 禁止：LINQ 直接嵌入 AddLHS 參數
optEngine.AddLHS(dataload.parameter_Cost.FirstOrDefault(...)?.QTY ?? 0, ...);
```

---

## 9. ObjectiveFunction 類別範本

```csharp
// Constraints/ObjectiveFunction.cs
public class ObjectiveFunction
{
    private OptEngine optEngine;
    private Dataload  dataload;

    public ObjectiveFunction(Dataload dataload, OptEngine engine)
    {
        this.optEngine = engine;
        this.dataload  = dataload;
    }

    public void Build()
    {
        dataload.Date.ForEach(d =>
            dataload.Employee.ForEach(e =>
            {
                optEngine.AddLHS(dataload.Penalty_SixDay,        new VariableB_SixDayWork    { Date = d, Employee = e });
                optEngine.AddLHS(dataload.Penalty_GroupMismatch, new VariableB_GroupMismatch { Date = d, Employee = e });
            }));

        dataload.Employee.ForEach(e =>
            optEngine.AddLHS(dataload.Penalty_BelowAVG, new VariableX_BelowAVG { Employee = e }));

        optEngine.CreateMinimize(); // 或 CreateMaximize()
    }
}
```

> 目標式只能呼叫 `AddLHS`，不使用 `AddRHS`。

---

## 10. BuildModel 類別範本

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
        Logging.Info("【建構目標式】");
        new ObjectiveFunction(dataload, engine).Build();

        Logging.Info("【建構限制式】");
        new Constraint_FullfillDemand(dataload, engine).Build();
        new Constraint_OneGroup      (dataload, engine).Build();
        new Constraint_PreAssign     (dataload, engine).Build();
        new Constraint_SixDayWork    (dataload, engine).Build();
        new Constraint_NightToDay    (dataload, engine).Build();
        // 依問題新增其他限制式...
    }
}
```

---

## 11. 主專案類別（ProblemName.cs）範本

```csharp
// ProblemName.cs
public class ProblemName : IDisposable
{
    public OptEngine optEngine;
    public Dataload  dataload;

    public Stopwatch buildModelTimer = new Stopwatch();
    public Stopwatch totalTimer      = new Stopwatch();
    public TimeSpan  totalTimeSpan   = new TimeSpan();

    private string _projectName => GetType().Name;

    public ProblemName()
    {
        dataload = new Dataload();
        Logging.SetLogFileName(_projectName);
    }

    public bool Execute()
    {
        totalTimer.Restart();

        CplexConfig config = new CplexConfig
        {
            epGap       = 0.01,
            timeLimit   = 300,
            workThreads = 8,
            enableLog   = true,
            exportLP    = true,
            exportSol   = true
        };

        optEngine = new OptEngine(config);
        optEngine.Build();

        buildModelTimer.Restart();
        new VariableCreate(dataload, optEngine).Build();
        Logging.Info("【建構變數完成】", buildModelTimer);

        new BuildModel(dataload, optEngine).Build();
        Logging.Info("【建構模型完成】", buildModelTimer);
        buildModelTimer.Stop();

        bool isSuccess = optEngine.Solve();

        if (isSuccess)
            dataload.WriteToCSV(optEngine);

        totalTimeSpan = totalTimer.Elapsed;
        totalTimer.Stop();
        return isSuccess;
    }

    public void Dispose()
    {
        optEngine?.Dispose();
    }
}
```

---

## 12. Program.cs 範本

```csharp
// Program.cs
using OptimFoundation.Core;
using ProjectNamespace;

internal class Program
{
    static void Main(string[] args)
    {
        using (ProblemName project = new ProblemName())
        {
            project.Execute();
            Logging.Info("整體運作時間:", project.totalTimer);
        }
    }
}
```

---

## 13. CplexConfig 參數說明

| 參數 | 說明 | 建議值 |
|------|------|--------|
| `epGap` | MIP gap 容忍度 | LP: `0` / MILP: `0.01`~`0.05` |
| `timeLimit` | 求解時間上限（秒） | LP: `300` / IP: `1800` / MILP: `3600` |
| `workThreads` | 平行執行緒數 | `8`~`16` |
| `mipEmphasis` | MIP 策略 0=平衡 1=可行解 2=最佳解 | LP: `0` / IP: `1` / MILP: `2` |
| `polishAfterTime` | N 秒後啟動 Solution Polishing | `timeLimit * 0.5` |
| `enableLog` | 顯示 CPLEX 求解 log | `true` |
| `exportLP` | 輸出 `.lp` 模型檔（debug 用） | `true` |
| `exportMPS` | 輸出 `.mps` 模型檔 | 選用 |
| `exportSol` | 輸出 `.sol` 解答檔 | `true` |

### 輸出目錄

| 目錄 | 內容 |
|------|------|
| `Output/Model/` | `.lp`、`.mps` |
| `Output/Sol/` | `.sol` |
| `Output/IIS/` | `.ilp`（Infeasible 時自動產生）|
| `Output/Logs/` | 執行 log |

---

## 14. 常見錯誤

| 錯誤 | 原因 | 解法 |
|------|------|------|
| `KeyNotFoundException: 找不到變數 'xxx'` | `AddLHS/AddRHS` 的 key 與 `Build*Vs` 建立時的 key 不符 | 確認 property 宣告順序與 `Build*Vs` 傳入 set 順序一致 |
| `ArgumentException: 期望 N 個參數，收到 0 個` | 用 object initializer 但 Parameter 只有 `params object[]` 建構子 | 移除 `params` 建構子，改用 properties-only |
| `InvalidOperationException: 缺少建構子` | Variable/Parameter class 有非預設建構子 | 移除所有建構子 |
| `NullReferenceException` 在 LINQ 查 Parameter | `FirstOrDefault` 返回 `null` | 使用 `?.QTY ?? 0.0` |
| `Build 後 varCount == 0` | `Build*Vs` 傳入空 List | 確認 Dataload 建構子內 Set 有初始化資料 |
| Infeasible / 解不到 | LHS/RHS 方向錯誤（移項） | 對照 AML 確認每個 `AddLHS/AddRHS` |

---

## 15. 開發 Checklist

```
□  1. 新建 .csproj，加入 OptimFoundation.Core、OptimFoundation.Cplex 參考
□  2. 確認 ILOG.Concert.dll / ILOG.CPLEX.dll 路徑正確
□  3. 建立 Data/Parameter_Xxx.cs（properties only，QTY 最後）
□  4. 建立 Data/Dataload.cs（Sets 初始化 + Parameters 初始化）
□  5. 建立 VariablesClass/Variable[B|I|X]_Xxx.cs（properties only）
□  6. 建立 VariablesClass/VariableCreate.cs（Build*Vs，順序對應 properties）
□  7. 建立 Constraints/ObjectiveFunction.cs（AddLHS + CreateMinimize/Maximize）
□  8. 建立 Constraints/Constraint_Xxx.cs（AddLHS + AddRHS + Create* + ConstraintCount++）
□  9. 建立 Constraints/BuildModel.cs（依序呼叫所有 Build()）
□ 10. 建立 ProblemName.cs（CplexConfig → Build → VariableCreate → BuildModel → Solve）
□ 11. 建立 Program.cs
□ 12. dotnet build → 確認 0 errors
□ 13. 執行，確認 CPLEX log 顯示 Optimal 或預期狀態
□ 14. 開啟 Output/Model/*.lp 確認模型結構正確
```
