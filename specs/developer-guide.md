# OptimFoundation 開發手冊

> 給基於此框架開發新最佳化問題的開發者

---

## 目錄

1. [架構概覽](#1-架構概覽)
2. [Variable — 定義決策變數](#2-variable--定義決策變數)
3. [Parameter — 定義模型參數](#3-parameter--定義模型參數)
4. [Dataload — 資料初始化](#4-dataload--資料初始化)
5. [VariableCreate — 建立變數](#5-variablecreate--建立變數)
6. [Pool API — 建立限制式](#6-pool-api--建立限制式)
7. [Objective Function — 目標式](#7-objective-function--目標式)
8. [BuildModel — 組裝模型](#8-buildmodel--組裝模型)
9. [CplexConfig — 求解器設定](#9-cplexconfig--求解器設定)
10. [執行與結果](#10-執行與結果)
11. [軟性限制式（Soft Constraints）](#11-軟性限制式soft-constraints)
12. [變數界限動態修改](#12-變數界限動態修改)
13. [進階：繼承 OptEngine](#13-進階繼承-optengine)
14. [進階：Benders Decomposition](#14-進階benders-decomposition)
15. [常見錯誤 FAQ](#15-常見錯誤-faq)

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
    └── .GetSetVarValues<T>()     ← 取得解值
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
| `VariableB_` | Binary（0/1） | `BuildBVs<T>()` |
| `VariableX_` | Continuous（連續） | `BuildCVs<T>()` |
| `VariableI_` | Integer（整數） | `BuildIVs<T>()` |

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

---

## 5. VariableCreate — 建立變數

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

    Logging.Info($"Variables created: {optEngine.varCount}");
}
```

### 完整 Build 方法列表

| 方法 | 類型 | LB | UB |
| --- | --- | --- | --- |
| `BuildBVs<T>(sets)` | Binary | 0 | 1 |
| `BuildCVs<T>(sets)` | Continuous | 0 | 1E100 |
| `BuildCVs<T>(lb, ub, sets)` | Continuous | 自訂 | 自訂 |
| `BuildIVs<T>(sets)` | Integer | 0 | 1E100 |
| `BuildIVs<T>(lb, ub, sets)` | Integer | 自訂 | 自訂 |

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

### 範例：等號限制式

```csharp
// 每位員工每天只能排一個班別
// Σ_g X[d,e,g] = 1
dataload.Group.ForEach(g =>
    engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = g }));

engine.AddRHS(1);
engine.CreateEqual($"OneGroup@{d:yyyy_MM_dd}@{e}");
ConstraintCount++;
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
        try
        {
            Logging.Info("【建構目標式】");
            new ObjectiveFunction { Engine = engine, Data = dataload }.Build();

            Logging.Info("【建構限制式】");
            new Constraint_OneGroup   { Engine = engine, Data = dataload }.Build();
            new Constraint_SixDayWork { Engine = engine, Data = dataload }.Build();
            // ... 其他限制式
        }
        catch (Exception)
        {
            throw;
        }
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
| `false` | 只顯示框架 log（ObjVal、Status 等） | 只有框架 log |

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

```csharp
public class MyEngine : OptEngine
{
    public MyEngine(CplexConfig config) : base(config) { }

    public override void Build()
    {
        base.Build();
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

當需要存取 dual values、追蹤特定限制式物件或擴充 OptEngine 功能時，可以繼承 `OptimFoundation.Cplex.OptEngine`。

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
3. 啟用 `enableLog = true` 觀察 CPLEX 求解過程

---

### Q：CPLEX log 顯示亂碼

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
