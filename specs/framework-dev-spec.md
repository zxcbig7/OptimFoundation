# OptimFoundation 開發規格書

## 1. 架構概覽

框架分三層，上層依賴下層，下層不知道上層存在。

```
┌─────────────────────────────────────┐
│  Project Layer  (e.g. RosteringProblem) │  問題定義、資料、限制式
├─────────────────────────────────────┤
│  Solver Layer   (Cplex / Gurobi)        │  Solver 實作
├─────────────────────────────────────┤
│  Core Layer     (OptimFoundation.Core)  │  抽象合約、共用邏輯
└─────────────────────────────────────┘
```

---

## 2. Core Layer

### 2.1 ModelElementBase

所有模型元素（Variable、Parameter、Constraint）的共同根基。

**職責：**
- 透過 `InitClassBySets(params object[] sets)` 以反射將值依序填入 properties
- `ToString()` 產生唯一 key，格式為 `ClassName@val1@val2@...`（DateTime 格式化為 `yyyy-MM-dd`）

**子類設計規則：**

| 類型 | 繼承自 | 寫法 |
|------|--------|------|
| Variable class | `VariableBase` | properties only，無建構子 |
| Parameter class | `ParameterBase` | properties only，無建構子 |
| Constraint class | 可繼承 `ConstraintBase` 或一般 class | ctor 只收實際需要的 Set / Parameter / 界限與 engine；`Build()` 只含本條公式 |

**零建構子原則（Zero-Constructor）：**  
只適用於 Variable / Parameter class；它們只宣告 properties，不寫任何建構子。Constraint / Objective 反而 MUST 用顯式 ctor 列出所需資料，避免耦合整包 Dataload。

```csharp
// Variable / Parameter
new VariableB_ShiftAssign { Date = d, Employee = e, Group = g }
new Parameter_ShiftDemand { Date = d, Group = "D", QTY = 5 }

```

---

### 2.2 VariableBuilder

**職責：** 從多個 Set 的笛卡爾積產生所有變數名稱，供 EngineBase.BatchBuild 使用。

**核心機制：**
- `GetCtor(Type)` — 以 `Expression.Lambda` 編譯建構子 delegate，快取於 `ConcurrentDictionary<Type, Func<string[], object>>`
- 優先使用無參數建構子（零建構子設計）；向下相容 `object[]`、`string[]` 建構子
- `GenVarParts(List<string>[])` — 直接 yield `string[]`，避免字串拼接再 Split 的 overhead
- `GetVarNames<TVariable>(object[] sets)` — 主要對外方法，回傳所有變數名稱

**支援的 Set 型別：**  
任何 `IEnumerable<T>`（`List<T>`、`T[]` 皆可），T = `DateTime` / `int` / `long` / `double` / `decimal` / `string` / enum。
單獨傳一個 `string[]` 會被 C# 陣列共變誤 bind 成 `params object[]` 本身——`ConvertSetsToStringLists` 偵測「元素全為裸 string」時自動還原成單一 set。

---

### 2.3 EngineBase\<TModel, TVar, TExpr, TConstr\>

Solver 實作的抽象基底。所有 Pool API、VariableSet 管理、批次建變數邏輯都在這裡。

#### 2.3.1 Solver Contract（必須 override）

| 方法 | 說明 |
|------|------|
| `Configuration(ISolverConfig)` | 建立 Model 物件、套用所有 Config 參數 |
| `AddVariable(name, lb, ub, VarType)` | 向 solver 新增單一變數，同時寫入 `Variables` |
| `LinearExpr(terms)` | 從 `(coef, var)` list 建立 solver 線性表達式 |
| `AddConstraint(name, lhs, sense, rhs)` | 新增一般限制式（`<=` `==` `>=`） |
| `AddRangeConstraint(name, expr, lb, ub)` | 新增範圍限制式 |
| `SetObjective(expr, sense)` | 設定目標式方向 |
| `SetVariableBounds(var, lb, ub)` | 修改已建立變數的 LB / UB |
| `Build()` | 入口，呼叫 `Configuration(Config)` |
| `Solve()` | 求解，回傳 `bool`（true = Optimal 或 Feasible） |
| `GetObjectiveValue()` | 取得目標值 |
| `GetVariableValue(name)` | 取得變數解值 |
| `Dispose()` | 釋放 solver 資源 |

#### 2.3.2 可選 override（有 default 實作）

| 方法 | Default 行為 | Override 目的 |
|------|-------------|---------------|
| `AddVariables(names, lb, ub, type)` | 逐筆呼叫 `AddVariable` | 使用 solver 原生 batch API（CPLEX `NumVarArray` / Gurobi `AddVars`），減少 interop 次數 |
| `BuildCVs/BuildIVs/BuildBVs` | 呼叫 `BatchBuild<TVariable>` | 通常不需 override |
| `CreateLeSoft/CreateGeSoft/CreateEqSoft` | `throw NotImplementedException` | 實作軟性限制式（penalty 法） |

#### 2.3.3 VariableSet 管理

```
Variables          Dictionary<string, TVar>                      所有變數的平坦查找表
VariableSets       Dictionary<string, Dictionary<string, TVar>>  依型別名稱分組
```

建立變數後透過 `BuildBVs<TVariable> / BuildIVs<TVariable> / BuildCVs<TVariable>` 自動分組。  
查詢時 `ReadVar(varObj)` 先用型別名稱找到 Set，再用 `varObj.ToString()` 查 key。

#### 2.3.4 Pool API（限制式 / 目標式建構）

呼叫順序：
```
AddLHS(coef, varObj)  →  AddLHS(constant)
AddRHS(coef, varObj)  →  AddRHS(constant)
CreateEqual / CreateLessEqual / CreateGreatEqual / CreateRange (name)
```

每次 `Create*` 呼叫後 Pool 自動清空。LHS 與 RHS 的常數項由框架合併處理，開發者不需要手動移項。

---

## 3. Solver Layer

每個 Solver 實作一個繼承 `EngineBase` 的 `OptEngine` class，並提供對應的 `ISolverConfig` 實作。

### CPLEX — OptimFoundation.Cplex

- `OptEngine : EngineBase<Cplex, INumVar, ILinearNumExpr, IRange>`
- Solver config: `CplexConfig`（`epGap`、`timeLimit`、`workThreads`、cuts、emphasis 等純 CPLEX 旋鈕）
- Project config: `ProjectConfig`（`ProjectName`、`RetentionDays`、`EnableSolverLog`、`ExportLP/MPS/Sol`、輸出身分）
- `AddVariables` override：`Model.NumVarArray(n, lbs, ubs, types, names)`，一次 interop 建立 N 個變數
- `Solve()` 完整流程：輸出 LP/MPS → 求解 → 判斷狀態 → 輸出 Sol → Infeasible 時計算 IIS

### Gurobi — OptimFoundation.Gurobi

- `OptEngine : EngineBase<GRBModel, GRBVar, GRBLinExpr, GRBConstr>`
- `AddVariables` override：`Model.AddVars(lbs, ubs, objs, types, names)` + `Model.Update()`

---

## 4. Project Layer

每個最佳化問題一個專案，結構如下：

```
ProjectName/
├── Program.cs                   唯一組裝點：模型三階段 + project / experiment runners
├── Model/                       數學模型
├── Set/Dataload.cs              資料載入、Sets、Parameters、輸出
├── Parameter/Parameter_Xxx.cs   parameters (properties only)
├── Variable/Variable{B|I|X}_Xxx.cs 變數 (properties only)
├── Objective/ObjectiveFunction.cs   目標式
└── Constraint/Constraint_Xxx.cs     各限制式
```

### 4.1 Variable class

```csharp
public class VariableB_ShiftAssign : VariableBase
{
    public DateTime Date     { get; set; }
    public string   Employee { get; set; }
    public string   Group    { get; set; }
}
```

**規則：**
- 繼承 `VariableBase`
- 命名前綴：`VariableB_`（Binary）、`VariableI_`（Integer）、`VariableX_`（Continuous）
- Properties 宣告順序 = 變數名稱中 `@` 的順序
- 不寫任何建構子

### 4.2 Parameter class

```csharp
public class Parameter_ShiftDemand : ParameterBase
{
    public DateTime Date  { get; set; }
    public string   Group { get; set; }
    public double   QTY   { get; set; }
}
```

**規則：同 Variable class，繼承 `ParameterBase`，不寫建構子。**

### 4.3 Program.cs 的變數階段

```csharp
var model = new OptModel("baseline-model")
    .AddVariables(e =>
    {
        e.BuildBVs<VariableB_ShiftAssign>(data.Date, data.Employee, data.Group);
        e.BuildCVs<VariableX_BelowAVG>(data.Employee);
    });
```

### 4.4 Constraint class

Constraint class 只收本條公式需要的積木，不注入整包 data：

```csharp
public sealed class Constraint_OneGroup
{
    private readonly List<DateTime> _dates;
    private readonly List<string> _employees;
    private readonly List<string> _groups;
    private readonly OptEngine _engine;

    public Constraint_OneGroup(
        List<DateTime> dates,
        List<string> employees,
        List<string> groups,
        OptEngine engine)
    {
        _dates = dates;
        _employees = employees;
        _groups = groups;
        _engine = engine;
    }

    public void Build()
    {
        _dates.ForEach(d =>
        {
            _employees.ForEach(e =>
            {
                _groups.ForEach(g =>
                    _engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = g }));
                _engine.AddRHS(1);
                _engine.CreateEqual($"Constraint_OneGroup@{d:yyyy_MM_dd}@{e}");
            });
        });
    }
}
```

限制式數量由 `EngineBase` 自動統計；不要自行累加或輸出數量。呼叫 `Solve()` 時會依
`ConstraintName@識別欄位` 的名稱前綴輸出 `[限制式建立完成]`，格式為
`count=<實際建立>/<預期建立>`。

### 4.5 Program.cs 的目標式與限制式階段

```csharp
model
    .AddObjective(e => new ObjectiveFunction(
        data.Date, data.Employee, data.Penalties, e).Build())
    .AddConstraints(e =>
    {
        new Constraint_FullfillDemand(
            data.Date, data.Employee, data.Group, data.ShiftDemand, e).Build();
        new Constraint_OneGroup(data.Date, data.Employee, data.Group, e).Build();
    });
```

### 4.6 兩種 Execute 環境

```csharp
var projectConfig = new ProjectConfig
{
    ProjectName = "ProjectName",
    EnableSolverLog = true,
    ExportSol = true,
};
var baseline = new CplexConfig { epGap = 0.03, timeLimit = 100 };

using var project = new OptProject(model)
    .UseConfig(() => projectConfig)
    .UseConfig(() => baseline)
    .OnSolved(e => data.WriteToCSV(e));
bool ok = project.Execute();

var emphasis = baseline.Clone();
emphasis.Emphasis = 2;
var result = new OptExperiment("project-tuning", "baseline vs emphasis")
    .AddModel(model)
    .AddConfig("baseline", baseline)
    .AddConfig("emphasis", emphasis)
    .Run();
```

`OptModel` 是不持有 engine 的純定義，固定套用 variables → objective → constraints。`OptProject` 負責單次執行、housekeeping 與 `OnSolved`；`OptExperiment` 負責 m×n 序列實驗，預設 solver log / 匯出 OFF 且沒有 `OnSolved`。

`OptData.Load` 完成註冊與驗證後會凍結 framework-controlled mutation API；基底類別無法攔截既有 public fields 或可變 `List` 的直接寫入，建模階段仍 MUST 視 data 為唯讀。

---

## 5. 變數命名規則

### Variable key 格式

`ClassName@val1@val2@...`

- `DateTime` → `yyyy-MM-dd`
- 其他型別 → `ToString()`

範例：`VariableB_ShiftAssign@2026-01-01@E1@D`

### 約束 name 格式

自由定義，建議 `ConstraintName@key1@key2@...` 方便 debug。

---

## 6. 新增 Solver 步驟

1. 新增 project，參考 `OptimFoundation.Cplex`
2. 實作 `OptEngine : EngineBase<TModel, TVar, TExpr, TConstr>`
3. Override 所有 **Solver Contract** abstract 方法（共 11 個）
4. 選擇性 override `AddVariables` 使用 solver 原生 batch API
5. 實作 `ISolverConfig` concrete class

---

## 7. 新增最佳化問題步驟

1. 新增 project，加入 solver 參考
2. 建立 `Set/` 與 `Parameter/`；`Dataload : DataContext` 只透過 `OptData.Load` 建構
3. 建立 `Variable/` 的 `Variable{B|I|X}_Xxx`（properties only）
4. 建立 `Objective/ObjectiveFunction.cs` 與各 `Constraint/Constraint_Xxx.cs`；ctor 只收自己需要的資料
5. 在 `Program.cs` 直接註冊 `AddVariables` / `AddObjective` / `AddConstraints`
6. 建立 `ProjectConfig` 與 `CplexConfig`，不可混放專案輸出與 solver 旋鈕
7. 同一個 `OptModel` 視需求交給 `OptProject` 或 `OptExperiment`

---

## 8. 常見錯誤

| 錯誤 | 原因 | 解法 |
|------|------|------|
| `KeyNotFoundException: 找不到變數 'xxx'` | `AddLHS/AddRHS` 的 varObj `ToString()` 與建立時的 key 不符 | 確認 property 宣告順序與 `BuildBVs` 傳入 set 順序一致 |
| `ArgumentException: 期望 N 個參數，收到 M 個` | `InitClassBySets` 呼叫時 sets 數量與 properties 數量不符 | Variable/Parameter class 不要加 property 以外的東西 |
| `InvalidOperationException: 缺少可用的建構子` | 類別有非無參數建構子 | 移除所有建構子，改用 properties only |
| CPLEX Error 1424 Invalid filetype | 用 `ExportModel` 輸出 `.sol` | 改用 `WriteSolution` |
