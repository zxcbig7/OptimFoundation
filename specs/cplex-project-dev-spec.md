# OptimFoundation CPLEX Project 開發說明書

> API 細節請參閱 [developer-guide.md](developer-guide.md)。本文件定義新題目的 paved path：`OptModel` 模型定義、`OptProject` 單次 runner、`OptExperiment` 實驗 runner，以及 project / solver config 分層。

## 1. 標準目錄

```text
ProjectName/
├── ProjectName.csproj
├── Program.cs                     唯一組裝點
├── Model/ProjectName_Model.md     可審閱的數學模型
├── Set/Dataload.cs                Sets、Parameters、輸出
├── Parameter/Parameter_Xxx.cs     一個 parameter 一個檔案
├── Variable/VariableB_Xxx.cs      Binary 變數
├── Variable/VariableI_Xxx.cs      Integer 變數（若有）
├── Variable/VariableX_Xxx.cs      Continuous 變數（若有）
├── Objective/ObjectiveFunction.cs 目標式
└── Constraint/Constraint_Xxx.cs   一條限制式一個檔案
```

Program.cs 直接呼叫 `.AddVariables(...)`、`.AddObjective(...)`、`.AddConstraints(...)`。不要新增只負責轉呼叫的中介組裝檔；模型只定義一次，兩種 runner 共用。

## 2. csproj 依賴規則

### OptimFoundation repo 內部 Templates

`Templates/Tutorial` 與 `Templates/FJSP_BASIC_BRICK` 已改為直接驗證工作樹 API：

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\OptimFoundation.Core\OptimFoundation.Core.csproj" />
  <ProjectReference Include="..\..\src\OptimFoundation.Cplex\OptimFoundation.Cplex.csproj" />
</ItemGroup>
<ItemGroup>
  <Reference Include="ILOG.Concert"><HintPath>..\dlls\ILOG.Concert.dll</HintPath></Reference>
  <Reference Include="ILOG.CPLEX"><HintPath>..\dlls\ILOG.CPLEX.dll</HintPath></Reference>
  <Analyzer Include="..\dlls\OptimFoundation.Generators.dll" />
</ItemGroup>
```

Core / Cplex 使用 `ProjectReference`；ILOG 仍是 binary reference，generator 仍以 analyzer DLL 掛入。`Templates/Template_CPLEX` 與 `Templates/Sudoku_SHC279` 同樣以 project reference 驗證框架 source。

### AI-Modeling 消費端

AI-Modeling 是發布 DLL 的消費端，必須保留 repo-local `dlls/` 的 `<Reference>` + `HintPath`，generator 用 `<Analyzer Include="...OptimFoundation.Generators.dll" />`；不要跨 repo 改成 project reference。兩套規則用途不同，不可混用。

## 3. 命名與資料類別

| 類別 | 前綴 | 建立方法 | 數值欄位 |
|---|---|---|---|
| Binary variable | `VariableB_` | `BuildBVs<T>()` | 無 |
| Integer variable | `VariableI_` | `BuildIVs<T>()` | 無 |
| Continuous variable | `VariableX_` | `BuildCVs<T>()` | 無 |
| Parameter | `Parameter_` | source / `OptData.Load` | `QTY`（最後一個） |
| Constraint | `Constraint_` | Pool API | 無 |

Variable / Parameter 採 properties-only，property 宣告順序就是 key 的索引順序。推薦以 `[OptVar]`、`[OptParam]` 與 `[OptDim<TSet>]` 交給 source generator 生成 class body；需要手寫時繼承 `VariableBase` / `ParameterBase` 且保留無參數建構子。

Variable key 格式：`VariableB_ShiftAssign@2026-01-01@E1@D`；`DateTime` 固定 `yyyy-MM-dd`。

## 4. Dataload 與資料防護

```csharp
public partial class Dataload : DataContext
{
    public List<DateTime> Date = new();
    public List<string> Employee = new();
    public List<string> Group = new();
    public List<Parameter_ShiftDemand> ShiftDemand = new();

    public Dataload(IDataSource source)
    {
        Date = Set.Load<DateTime, Parameter_ShiftDemand>(source);
        Employee = Set.Load<string, Parameter_Employee>(source);
        Group = Set.Load<string, Parameter_ShiftDemand>(source);
        ShiftDemand = source.LoadParam<Parameter_ShiftDemand>();
    }

    public void WriteToCSV(OptEngine engine)
    {
        FolderDir.Solution.CreateFolder();
        CSVCtrl.SaveToCSV<VariableB_ShiftAssign>(
            engine.GetSetVarSol<VariableB_ShiftAssign>(), DATA_ID: "V1", USER_ID: "USER");
    }
}

var data = OptData.Load(() => new Dataload(source));
```

`OptData.Load` 的順序是 factory → generator 註冊 → `DataValidator.Validate` → freeze → 回傳。它會檢查 dangling index、duplicate key、數值 sanity 與 opt-in `[FullGrid]`。

Freeze 已降級為保護 framework-controlled mutation API（例如框架註冊入口）。基底類別無法攔截既有 public fields 的直接指定或可變 `List.Add`，因此這些寫入不保證立即拋例外；所有模型 callback 仍 MUST 把 `data` 視為唯讀。

## 5. Variable 階段

直接在 `OptModel.AddVariables` 建立所有變數：

```csharp
.AddVariables(engine =>
{
    engine.BuildBVs<VariableB_ShiftAssign>(data.Date, data.Employee, data.Group);
    engine.BuildBVs<VariableB_SixDayWork>(data.Date, data.Employee);
    engine.BuildCVs<VariableX_BelowAVG>(data.Employee);
    // engine.BuildIVs<VariableI_WorkCount>(0, 30, data.Employee);
})
```

| 方法 | 預設 LB | 預設 UB |
|---|---:|---:|
| `BuildBVs<T>(sets…)` | 0 | 1 |
| `BuildCVs<T>(sets…)` | 0 | 1E100 |
| `BuildCVs<T>(lb, ub, sets…)` | 自訂 | 自訂 |
| `BuildIVs<T>(sets…)` | 0 | 1E100 |
| `BuildIVs<T>(lb, ub, sets…)` | 自訂 | 自訂 |

Set 傳入順序 MUST 對應 variable properties 順序。框架自行記錄每種變數與總數，不要在專案端手動累加。

## 6. Objective 與 Constraint

Objective / Constraint 的建構子只收自己用到的 Set 積木、Parameter 清單、界限值與 engine，不收整包 `Dataload`。

```csharp
public sealed class Constraint_FullfillDemand
{
    private readonly List<DateTime> _dates;
    private readonly List<string> _employees;
    private readonly List<string> _groups;
    private readonly List<Parameter_ShiftDemand> _demand;
    private readonly OptEngine _engine;

    public Constraint_FullfillDemand(
        List<DateTime> dates,
        List<string> employees,
        List<string> groups,
        List<Parameter_ShiftDemand> demand,
        OptEngine engine)
    {
        _dates = dates;
        _employees = employees;
        _groups = groups;
        _demand = demand;
        _engine = engine;
    }

    public void Build()
    {
        // AML LHS → AddLHS；AML RHS → AddRHS；最後 Create*。
    }
}
```

`Program.cs` 是唯一知道整包 data 且負責接線的地方：

```csharp
var model = new OptModel("baseline-model")
    .AddVariables(engine =>
    {
        engine.BuildBVs<VariableB_ShiftAssign>(data.Date, data.Employee, data.Group);
    })
    .AddObjective(engine => new ObjectiveFunction(
        data.Date, data.Employee, data.Penalties, engine).Build())
    .AddConstraints(engine =>
    {
        new Constraint_FullfillDemand(
            data.Date, data.Employee, data.Group, data.ShiftDemand, engine).Build();
        new Constraint_OneGroup(data.Date, data.Employee, data.Group, engine).Build();
    });
```

`OptModel` 只註冊 callback，不建立或持有 engine。框架套用時固定執行 variables → objective → constraints，同一階段內依登記順序執行。

## 7. Config 分層

```csharp
var projectConfig = new ProjectConfig
{
    ProjectName = "Rostering",
    RetentionDays = 30,
    EnableSolverLog = true,
    ExportLP = true,
    ExportMPS = false,
    ExportSol = true,
};

var solverConfig = new CplexConfig
{
    epGap = 0.01,
    timeLimit = 300,
    workThreads = 8,
    randomSeed = 42,
};
```

| Config | 只放什麼 | 關鍵預設 |
|---|---|---|
| `ProjectConfig` | 專案名、保留期、solver log 呈現、LP/MPS/Sol 匯出、輸出身分 | log ON，三種匯出 OFF |
| `CplexConfig` | 真正改變 CPLEX 求解行為的旋鈕 | 未指定欄位依各欄位 / CPLEX 預設 |

`ConfigSnapshot.From(cplexConfig)` 只會記 solver 設定，不再混入專案輸出策略。兩個 config 都支援 `Clone()`，使用 `MemberwiseClone` 保留所有目前欄位。

## 8. 單次求解：OptProject

```csharp
using var project = new OptProject(model)
    .UseConfig(() => projectConfig)
    .UseConfig(() => solverConfig)
    .OnSolved(engine => data.WriteToCSV(engine));

bool ok = project.Execute();
```

`OptProject.Execute()` 解析有效專案名 / 保留期，設定 log、做 housekeeping、建立 engine、套用模型、求解，再於成功時執行所有 `OnSolved` callback。`OptProject` 持有 native engine，MUST `using` / `Dispose`。

## 9. 實驗：OptExperiment

```csharp
var baseline = solverConfig.Clone();
var emphasis = baseline.Clone();
emphasis.Emphasis = 2;
var threads = baseline.Clone();
threads.Threads = 2;

var result = new OptExperiment("rostering-tuning", "baseline × solver variants")
    .AddModel(model)
    .AddConfig("baseline", baseline)
    .AddConfig("emphasis", emphasis)
    .AddConfig("threads-2", threads)
    .Run();
```

`.AddModel` × `.AddConfig` 會展開完整笛卡兒積；label 格式為 `模型名 | 設定名`。也可用 `.AddTrial(model, label, config)` 明列單一 cell。runner 每個 cell 建立 fresh engine、序列執行並自動儲存 `Experiment`。

實驗預設 solver log OFF、LP/MPS/Sol 匯出 OFF、不做 housekeeping，且刻意沒有 `OnSolved`。需要共同專案設定時可 `.UseConfig(() => projectConfig)`，但輸出策略不應污染 solver config。

## 10. 完整 Program.cs 骨架

```csharp
using OptimFoundation.Core;
using OptimFoundation.Cplex;

var data = OptData.Load(() => new Dataload(new CsvDataSource()));
var projectConfig = new ProjectConfig { ProjectName = "ProjectName", ExportSol = true };
var baseline = new CplexConfig { epGap = 0.01, timeLimit = 300, workThreads = 8 };

var model = new OptModel("baseline-model")
    .AddVariables(e => CreateVariables(data, e))
    .AddObjective(e => BuildObjective(data, e))
    .AddConstraints(e => BuildConstraints(data, e));

if (args.Contains("experiment"))
{
    var emphasis = baseline.Clone();
    emphasis.Emphasis = 2;
    new OptExperiment("project-tuning", "baseline vs emphasis")
        .AddModel(model)
        .AddConfig("baseline", baseline)
        .AddConfig("emphasis", emphasis)
        .Run();
    return;
}

using var project = new OptProject(model)
    .UseConfig(() => projectConfig)
    .UseConfig(() => baseline)
    .OnSolved(e => data.WriteToCSV(e));
bool ok = project.Execute();
```

## 11. Checklist

```text
□  1. 選對依賴模式：框架 repo template 用 project reference；AI-Modeling 用 repo-local DLL HintPath
□  2. 完成 Model.md 並通過人工 gate
□  3. 建立 Parameter / Set / Variable / Objective / Constraint
□  4. Dataload 繼承 DataContext，只以 OptData.Load 取得實例
□  5. Objective / Constraint ctor 只收實際需要的資料
□  6. Program.cs 直接註冊 variables / objective / constraints
□  7. 專案輸出行為放 ProjectConfig，solver 旋鈕放 CplexConfig
□  8. 單次求解用 OptProject；多設定比較用 OptExperiment + Clone
□  9. dotnet build：0 errors
□ 10. dotnet run：Status / objective / constraint counts 符合 baseline
□ 11. 開啟輸出的 LP 對照 Model.md；再驗證解的可行性與單位
```

## 12. 常見錯誤

| 錯誤 | 原因 | 解法 |
|---|---|---|
| `KeyNotFoundException: 找不到變數` | Pool 使用的 key 與 `Build*Vs` 的 set 順序不同 | 對齊 variable property 與 set 順序 |
| 實驗 snapshot 出現輸出策略 | 兩層 config 混用 | 專案輸出欄位移到 `ProjectConfig` |
| 目標式在 soft constraint 後才建立 | callback 接錯階段 | 目標式只放 `AddObjective`；框架會先於 constraints 執行 |
| 同一份 data 在實驗間變動 | callback 寫入 public field / `List` | callback 視 data 為唯讀；結果寫到獨立 result 物件 |
| AI-Modeling 拿到 source 工作樹版本 | 消費端誤用跨 repo project reference | 還原 repo-local DLL / analyzer `HintPath` |
| OptimFoundation template 看不到新 API | 內部 template 還指 stale Core/Cplex DLL | Core / Cplex 改用 `ProjectReference` |
| Infeasible / 解不到 | LHS/RHS 移項、改號或方向錯 | 逐式對照 Model.md；使用 IIS 定位 |
