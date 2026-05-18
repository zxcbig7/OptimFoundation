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
| Constraint class | `ConstraintBase` | properties only + `override void Build()` |

**零建構子原則（Zero-Constructor）：**  
所有 Variable、Parameter、Constraint class 只需宣告 properties，不寫任何建構子。  
呼叫端一律使用 object initializer：

```csharp
// Variable / Parameter
new VariableB_ShiftAssign { Date = d, Employee = e, Group = g }
new Parameter_ShiftDemand { Date = d, Group = "D", QTY = 5 }

// Constraint（屬性由 project base class 注入）
new Constraint_OneGroup { Engine = engine, Data = data }
```

---

### 2.2 VariableBuilder

**職責：** 從多個 Set 的笛卡爾積產生所有變數名稱，供 EngineBase.BatchBuild 使用。

**核心機制：**
- `GetCtor(Type)` — 以 `Expression.Lambda` 編譯建構子 delegate，快取於 `ConcurrentDictionary<Type, Func<string[], object>>`
- 優先使用無參數建構子（零建構子設計）；向下相容 `object[]`、`string[]` 建構子
- `GenVarParts(List<string>[])` — 直接 yield `string[]`，避免字串拼接再 Split 的 overhead
- `GetVarNames<T>(object[] sets)` — 主要對外方法，回傳所有變數名稱

**支援的 Set 型別：**  
`List<DateTime>`、`List<int>`、`List<double>`、`List<string>`

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
| `BuildCVs/BuildIVs/BuildBVs` | 呼叫 `BatchBuild<T>` | 通常不需 override |
| `CreateLeSoft/CreateGeSoft/CreateEqSoft` | `throw NotImplementedException` | 實作軟性限制式（penalty 法） |

#### 2.3.3 VariableSet 管理

```
Variables          Dictionary<string, TVar>                      所有變數的平坦查找表
VariableSets       Dictionary<string, Dictionary<string, TVar>>  依型別名稱分組
```

建立變數後透過 `BuildBVs<T> / BuildIVs<T> / BuildCVs<T>` 自動分組。  
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
- Config: `CplexConfig`（`epGap`、`timeLimit`、`workThreads`、`enableLog`、`exportLP`、`exportMPS`、`exportSol`、`projectName`）
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
├── Data/
│   ├── Dataload.cs              資料載入、Sets、Parameters 初始化
│   └── Parameter_Xxx.cs         parameters (properties only)
├── VariablesClass/
│   ├── VariableCreate.cs        呼叫 BuildBVs/BuildIVs/BuildCVs
│   └── Variable{B|I|X}_Xxx.cs  變數 (properties only)
├── Constraints/
│   ├── XxxConstraintBase.cs     project 中介 base（注入 Engine + Data）
│   ├── BuildModel.cs            依序呼叫所有限制式的 Build()
│   ├── ObjectiveFunction.cs     目標式
│   └── Constraint_Xxx.cs        各限制式
└── ProblemName.cs               Execute() 入口
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

### 4.3 VariableCreate

```csharp
public void Build()
{
    optEngine.BuildBVs<VariableB_ShiftAssign>(dataload.Date, dataload.Employee, dataload.Group);
    optEngine.BuildCVs<VariableX_BelowAVG>(dataload.Employee);
}
```

### 4.4 Constraint class

**Step 1 — 定義 project 中介 base（每個 project 一個）：**

```csharp
public abstract class RosterConstraintBase : ConstraintBase
{
    protected OptEngine Engine { get; set; }
    protected Dataload   Data   { get; set; }
    public abstract void Build();
}
```

**Step 2 — Constraint class 只剩邏輯：**

```csharp
public class Constraint_OneGroup : RosterConstraintBase
{
    public override void Build()
    {
        Data.Date.ForEach(d =>
        {
            Data.Employee.ForEach(e =>
            {
                Data.Group.ForEach(g =>
                    Engine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = g }));
                Engine.AddRHS(1);
                Engine.CreateEqual($"{ConstraintName}@{d:yyyy_MM_dd}@{e}");
                ConstraintCount++;
            });
        });
        Logging.Info($"{ConstraintName} 共：{ConstraintCount} 條");
    }
}
```

### 4.5 BuildModel

```csharp
var constraints = new List<RosterConstraintBase>
{
    new Constraint_FullfillDemand { Engine = engine, Data = dataload },
    new Constraint_OneGroup       { Engine = engine, Data = dataload },
    // ...
};
constraints.ForEach(c => c.Build());
```

### 4.6 Execute 入口

```csharp
public bool Execute()
{
    var config = new CplexConfig { epGap = 0.03, timeLimit = 100, ... };
    optEngine = new OptEngine(config);
    optEngine.Build();

    new VariableCreate(dataload, optEngine).Build();
    new BuildModel(dataload, optEngine).Build();

    return optEngine.Solve();
}
```

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
2. 建立 `Data/` — `Dataload` + `Parameter_Xxx`（properties only）
3. 建立 `VariablesClass/` — `Variable{B|I|X}_Xxx`（properties only）
4. 建立 `Constraints/XxxConstraintBase`（注入 Engine + Data）
5. 每個限制式繼承中介 base，只寫 `Build()` 邏輯
6. `BuildModel.cs` 組裝所有 constraint，統一 `ForEach(c => c.Build())`
7. `ProblemName.cs` 的 `Execute()` 串接 Build → Solve → 輸出

---

## 8. 常見錯誤

| 錯誤 | 原因 | 解法 |
|------|------|------|
| `KeyNotFoundException: 找不到變數 'xxx'` | `AddLHS/AddRHS` 的 varObj `ToString()` 與建立時的 key 不符 | 確認 property 宣告順序與 `BuildBVs` 傳入 set 順序一致 |
| `ArgumentException: 期望 N 個參數，收到 M 個` | `InitClassBySets` 呼叫時 sets 數量與 properties 數量不符 | Variable/Parameter class 不要加 property 以外的東西 |
| `InvalidOperationException: 缺少可用的建構子` | 類別有非無參數建構子 | 移除所有建構子，改用 properties only |
| CPLEX Error 1424 Invalid filetype | 用 `ExportModel` 輸出 `.sol` | 改用 `WriteSolution` |
