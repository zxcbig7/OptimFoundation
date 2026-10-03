# OptimFoundation Coding 開發指南

本指南以 Coding 架構與目前 public API 為核心。數學建模是 Coding 的輸入，tuning 是完成正確 baseline 後的延伸；兩者不主導本文順序。

## 0. Coding runtime architecture

```text
attributes + domain rows
        │
        ▼
IDataSource ──► DataContext / OptData.Load ──► validated data
                                                    │
                                                    ▼
OptModel ── variable / objective / constraint / MIP-start recipes
                                                    │
                                                    ▼
OptProject.Solve ──► OptEngine.Build ──► CPLEX solve
       │                                      │
       │                                      ├─ solution / status / metrics
       │                                      └─ import / export / conflict
       ▼
Trial / Experiment ──► main / meta / summary / trajectory CSV
```

| 模組 | 責任 | 日常入口 |
| --- | --- | --- |
| Modeling metadata | 描述 set、parameter、variable 的維度與型別 | `[OptSet]`、`[OptParam]`、`[OptVar]`、`[OptDim<T>]` |
| Data boundary | 載入、映射、驗證資料；輸出解 | `IDataSource`、`DataContext`、`OptData.Load`、`ISolutionSink` |
| Model recipe | 保存建變數、目標式、限制式、solution file 與 warm start 步驟 | `OptModel` |
| Expression/model engine | 管理 expression pool、native model、solve 狀態與結果 | `EngineBase`、`OptEngine` |
| Application lifecycle | 建 engine、套 config、build、solve、artifact、dispose | `OptProject` |
| Experiment | 展開 model/config trials 並序列化結果 | `OptExperiment`、`Experiment`、`Trial` |
| Infrastructure | 路徑、CSV、DB、logging | `ProjectConfig`、`CsvCtrl`、`IDbCtrl` |

### 0.1 典型呼叫順序

1. `OptData.Load(() => new Dataload(source))` 建立資料。
2. `new OptModel(name)` 後依序註冊 `AddVariables`、`AddObjective`、`AddConstraints`；需要 warm start 再加 `AddMIPStart`。
3. 建立 `ProjectConfig` 與 `CplexConfig`。
4. `using var project = new OptProject(name).LoadConfig(projectConfig)`。
5. `project.Solve(model, cplexConfig, onSolved, beforeSolve)`。
6. 從 `project.Engine` 讀 solution、status、metrics，或在 callback 寫入 `ISolutionSink`。
7. 要比較多組設定時使用 `project.Experiment(name)`。

`OptProject` 是 **Recommended path**。直接建立 `OptEngine` 適合 library integration、測試或需自行控制 lifecycle 的 **Advanced API**。

### 0.2 依任務找 API

| 任務 | API |
| --- | --- |
| 載入與驗證資料 | `IDataSource.Load<T>`、`OptData.Load`、`DataValidator.Validate` |
| 宣告 domain row | `SetRowBase`、`ParameterBase`、`VariableBase`、`ConstraintBase` |
| 建立變數 | `BuildVars<TVariable>`；進階使用 `BuildCVs/BuildIVs/BuildBVs` |
| 組 expression | `AddLHS`、`AddRHS`、`PoolState`、`ClearPool` |
| 建 hard/range constraint | `CreateLessEqual`、`CreateEqual`、`CreateGreaterEqual`、`CreateRange` |
| 建 soft/special constraint | `Create*Soft`、`ISpecialConstraints<TVar,TExpr>` |
| 建 objective | `CreateMinimize`、`CreateMaximize` |
| solve | `OptProject.Solve`；低階使用 `Build`、`Solve` |
| 讀 solution | `GetSolution`、`Get*Solution`、`GetVariableValue`、`LastMetrics` |
| warm start | `OptModel.AddMIPStart`、`EngineBase.AddMIPStart` |
| 匯入／匯出 | `OptModel.ReadModel/ReadSolution`、`OptEngine.ReadModel/ReadSolution/ExportModel/ExportSolution` |
| infeasible 診斷 | `GetConflictConstraints` |
| build 數量核對 | `VariableBuildCounts`、`ConstraintBuildCounts`；未引用變數由 `Solve()` 前 `[UNREFERENCED_VARIABLES]` WARN 點名 |
| 多設定實驗 | `OptProject.Experiment`、`OptExperiment` |
| CSV／DB | `CsvDataSource`、`DbDataSource`、`IDbCtrl`、`CsvCtrl` |

### 0.3 API 使用層級

- **Recommended**：`OptData.Load`、typed `BuildVars<T>`、owner/dim constraint overload、`OptModel`、`OptProject`、`OptExperiment`。
- **Advanced**：直接操作 `OptEngine`、string builders、bounds/reset、native CPLEX special constraints、copy/merge/thread、直接 import/export。
- **Framework integration**：generator base types、registration DTO、IO/DB contracts、CSV writers、logging 與 infrastructure helpers。

這份 Guide 給第一次接手 OptimFoundation 專案的人。

目標是把一份已確認的 `Model.md`，實作成可以 build、solve、驗證與重現的 C# 專案。

本文使用一個小型生產規劃案例貫穿所有步驟。

API 使用細節另見 AI-Modeling 的 [OptimFoundation API Guide](../../../AI-Modeling/.claude/skills/coding/optimfoundation-api-guide.md)。

交付前逐項核對 [Coding Checklist](../../../AI-Modeling/.claude/skills/coding/checklist.md)。

模型設計規則見 [Model Design Guide](../../../AI-Modeling/.claude/skills/modeling/model-design-guide.md)。

需要調參時，再讀 [Solver Tuning Guide](../../../AI-Modeling/.claude/skills/tuning/solver-tuning-guide.md) 與 [Tuning Checklist](../../../AI-Modeling/.claude/skills/tuning/checklist.md)。

---

## 1. 先理解三個 phase

OptimFoundation 的工作分成三個 phase。

### Phase 1：Modeling

輸入是需求、資料定義與商業規則。

輸出是已確認的 `Model/<Project>_Model.md`。

Modeling 決定：

- Set 是什麼。
- Parameter 的索引與單位是什麼。
- Variable 的 domain、型別與上下界是什麼。
- Objective 要最小化或最大化什麼。
- 每條 Constraint 的量詞、左右式與比較符號是什麼。
- 哪些規則要在解出後重新驗證。

Model.md 固定八段：

1. 問題摘要
2. Assumptions
3. Sets
4. Parameters
5. Decision Variables
6. Objective
7. Constraints
8. Validation Rules

這八段是 Coding 的契約。

Coding 不得自行移項、改號、補規則或改 domain。

### Phase 2：Coding

輸入是已確認的 Model.md。

輸出是：

- 可 build 的專案。
- 可載入且通過驗收的資料。
- 可求解的 canonical model。
- 可讀取並驗證的 solution。
- 可重現的 production baseline。

本 Guide 的主要內容就是 Phase 2。

### Phase 3：Tuning（選用）

只有 Phase 2 正確性 gate 全部通過後，才可進入 Tuning。

進入條件至少包括：

- 資料驗收通過。
- 模型可以 build。
- solver 回傳可接受狀態。
- 解值代回 Model.md 規則後全部成立。
- production baseline 的 Seed、Threads、ParallelMode 等環境已固定。

Tuning 改的是 solver config 與其證據。

Tuning 不應偷偷改 Model.md、資料語意或 constraint。

---

## 2. 本文案例：MiniProduction

工廠要生產多種產品。

每種產品有需求量。

總產量受單一產能上限限制。

產品只要啟用，就至少生產一個單位。

允許缺貨，但缺貨有高額懲罰。

### 2.1 Sets

$$Product = \{A, B\}$$

### 2.2 Parameters

$$Demand_p \ge 0 \quad \forall p \in Product$$

$$Capacity \ge 0$$

$$FixedCost_p \ge 0 \quad \forall p \in Product$$

$$ShortagePenalty > 0$$

### 2.3 Decision Variables

$$Open_p \in \{0,1\}$$

$$Produce_p \in \mathbb{Z}_{\ge 0}$$

$$Shortage_p \in \mathbb{R}_{\ge 0}$$

### 2.4 Objective

$$\min \sum_p FixedCost_p Open_p + ShortagePenalty \sum_p Shortage_p$$

### 2.5 Constraints

需求平衡：

$$Produce_p + Shortage_p = Demand_p \quad \forall p$$

總產能：

$$\sum_p Produce_p \le Capacity$$

啟用後的最低產量：

$$Produce_p \ge Open_p \quad \forall p$$

為了讓 `Open` 真正表達是否啟用，正式模型通常還要有上界連結。

本文的小案例加入：

$$Produce_p \le Demand_p Open_p \quad \forall p$$

這條規則必須先寫回 Model.md 並經確認，不能只在 Coding 階段臨時加入。

### 2.6 Validation Rules

解出後重新檢查：

- 每個產品的 `Produce + Shortage = Demand`。
- 所有產品的 `Produce` 加總不超過 `Capacity`。
- `Produce >= Open`。
- `Produce <= Demand * Open`。
- Binary、Integer 與非負條件在 tolerance 內成立。

---

## 3. 建立固定專案結構

專案根目錄放 `Program.cs`、`<Project>.csproj` 與 `status.json`。

模型實作固定使用八個資料夾：

```text
Projects/MiniProduction/
├── Model/
├── Data/
├── Set/
├── Parameter/
├── Variable/
├── Objective/
├── Constraint/
├── Solution/
├── MiniProduction.csproj
├── Program.cs
└── status.json
```

八個資料夾的責任如下：

| 資料夾 | 內容 |
| --- | --- |
| `Model/` | 已確認的數學模型與 validation rules |
| `Data/` | canonical CSV 與 `Dataload.cs` |
| `Set/` | `[OptSet]` 宣告 |
| `Parameter/` | `[OptParam]` 宣告 |
| `Variable/` | `[OptVar]` 宣告 |
| `Objective/` | 目標式建構器 |
| `Constraint/` | 一個檔案一種 constraint |
| `Solution/` | 建模前資料驗收、解讀與解驗證 |

不要把數學式塞進 `Program.cs`。

不要把資料轉換塞進 Constraint。

不要讓 Objective 或 Constraint 接收整包 `Dataload`。

---

## 4. 設定 csproj

`Projects/MiniProduction/MiniProduction.csproj` 可使用以下設定：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>MiniProduction</RootNamespace>
    <AssemblyName>MiniProduction</AssemblyName>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <Compile Remove="Generated/**/*.cs" />
  </ItemGroup>

  <ItemGroup>
    <None Remove="Data\**\*.csv" />
    <None Include="Data\**\*.csv"
          CopyToOutputDirectory="PreserveNewest"
          Link="Input\%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\OptimFoundation.Core\OptimFoundation.Core.csproj" />
    <ProjectReference Include="..\..\src\OptimFoundation.Cplex\OptimFoundation.Cplex.csproj" />
    <ProjectReference Include="..\..\src\OptimFoundation.Generators\OptimFoundation.Generators.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

這裡有三個不可省略的重點。

第一，framework repo 內的 template 使用 `ProjectReference` 直接參考 Core 與 Cplex source。

第二，Generators 的 `ProjectReference` 必須設定 `OutputItemType="Analyzer"` 與 `ReferenceOutputAssembly="false"`。

第三，`Data/**/*.csv` 必須被複製到輸出目錄的 `Input/`。

AI-Modeling 的消費端專案不直接參考 framework source；它改用 repo `dlls/` 下的 runtime DLL 與 generator analyzer DLL。

`FolderDir.Input` 指向執行時的 `Input/`，不是原始碼下的 `Data/`。

`Generated/` 只供閱讀 generator 產物，不可再次當成一般 source 編譯。

---

## 5. 宣告 Set

`Set/Set_Product.cs`：

```csharp
using OptimFoundation.Modeling;

namespace MiniProduction;

[OptSet]
[OptDim<string>("Product")]
public sealed partial class Set_Product { }
```

`partial` 是 generator 產生 class body 的必要條件。

`OptDim<string>("Product")` 會產生 `Product` property。

Set 的 CSV 是 `Data/Set_Product.csv`：

```csv
Product
A
B
```

維度欄位名稱必須和 attribute 一致。

Key 不得重複。

---

## 6. 宣告 Parameter

### 6.1 逐產品參數

`Parameter/Parameter_Demand.cs`：

```csharp
using OptimFoundation.Modeling;

namespace MiniProduction;

[OptParam]
[OptDim<string>("Product")]
public sealed partial class Parameter_Demand { }
```

`Parameter/Parameter_FixedCost.cs`：

```csharp
using OptimFoundation.Modeling;

namespace MiniProduction;

[OptParam]
[OptDim<string>("Product")]
public sealed partial class Parameter_FixedCost { }
```

generator 會為 Parameter 產生維度 property 與數值 property `QTY`。

`Data/Parameter_Demand.csv`：

```csv
Product,QTY
A,6
B,4
```

`Data/Parameter_FixedCost.csv`：

```csv
Product,QTY
A,2
B,3
```

### 6.2 Scalar 參數

Scalar 仍然是 `[OptParam]`，只是沒有 `[OptDim<T>]`。

`Parameter/Parameter_Capacity.cs`：

```csharp
using OptimFoundation.Modeling;

namespace MiniProduction;

[OptParam]
public sealed partial class Parameter_Capacity { }
```

`Parameter/Parameter_ShortagePenalty.cs`：

```csharp
using OptimFoundation.Modeling;

namespace MiniProduction;

[OptParam]
public sealed partial class Parameter_ShortagePenalty { }
```

每個 scalar parameter 都有自己的 CSV，而且只能有一筆資料。

`Data/Parameter_Capacity.csv`：

```csv
QTY
8
```

`Data/Parameter_ShortagePenalty.csv`：

```csv
QTY
100
```

用 `.Single().QTY` 讀 scalar。

`.Single()` 也會讓零列與多列資料立即失敗。

---

## 7. 實作 Dataload

`Data/Dataload.cs`：

本案例的 `import-data` 原始檔每列是一個產品，格式如下：

```csv
Product,Demand,FixedCost,Capacity,ShortagePenalty
A,6,2,8,100
B,4,3,8,100
```

`Capacity` 與 `ShortagePenalty` 是整份 instance 共用的 scalar，所以每列必須填相同值。

import constructor 會檢查這項條件，再拆成一份 Set、兩份逐產品 Parameter 與兩份 scalar Parameter。

```csharp
using System.Globalization;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;

namespace MiniProduction;

public sealed partial class Dataload : DataContext
{
    public List<Set_Product> set_Product = new();
    public List<Parameter_Demand> parameter_Demand = new();
    public List<Parameter_FixedCost> parameter_FixedCost = new();
    public List<Parameter_Capacity> parameter_Capacity = new();
    public List<Parameter_ShortagePenalty> parameter_ShortagePenalty = new();

    public Dataload() : this(new CsvDataSource()) { }

    public Dataload(IDataSource source)
    {
        set_Product = source.Load<Set_Product>("Set_Product");
        parameter_Demand = source.Load<Parameter_Demand>("Parameter_Demand");
        parameter_FixedCost = source.Load<Parameter_FixedCost>("Parameter_FixedCost");
        parameter_Capacity = source.Load<Parameter_Capacity>("Parameter_Capacity");
        parameter_ShortagePenalty = source.Load<Parameter_ShortagePenalty>("Parameter_ShortagePenalty");
    }

    public Dataload(string rawFile)
    {
        var raw = new CsvDataSource().LoadData(rawFile);
        if (raw.Rows.Count == 0)
            throw new InvalidDataException("MiniProduction raw file 至少需要一列產品。");

        static double Number(object value, string column) =>
            double.TryParse(
                value?.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double result)
                ? result
                : throw new InvalidDataException($"raw 欄位 {column} 不是有效數字：'{value}'。");

        double capacity = Number(raw.Rows[0]["Capacity"], "Capacity");
        double penalty = Number(raw.Rows[0]["ShortagePenalty"], "ShortagePenalty");

        foreach (System.Data.DataRow row in raw.Rows)
        {
            string product = row["Product"]?.ToString()?.Trim() ?? string.Empty;
            if (product.Length == 0)
                throw new InvalidDataException("raw Product 不得為空白。");

            double rowCapacity = Number(row["Capacity"], "Capacity");
            double rowPenalty = Number(row["ShortagePenalty"], "ShortagePenalty");
            if (rowCapacity != capacity || rowPenalty != penalty)
                throw new InvalidDataException(
                    "raw 每列的 Capacity 與 ShortagePenalty 必須一致。");

            set_Product.Add(new Set_Product { Product = product });
            parameter_Demand.Add(new Parameter_Demand
            {
                Product = product,
                QTY = Number(row["Demand"], "Demand"),
            });
            parameter_FixedCost.Add(new Parameter_FixedCost
            {
                Product = product,
                QTY = Number(row["FixedCost"], "FixedCost"),
            });
        }

        parameter_Capacity.Add(new Parameter_Capacity { QTY = capacity });
        parameter_ShortagePenalty.Add(new Parameter_ShortagePenalty { QTY = penalty });
    }

    public void Export()
    {
        CsvCtrl.WriteRows(set_Product, "Set_Product");
        CsvCtrl.WriteRows(parameter_Demand, "Parameter_Demand");
        CsvCtrl.WriteRows(parameter_FixedCost, "Parameter_FixedCost");
        CsvCtrl.WriteRows(parameter_Capacity, "Parameter_Capacity");
        CsvCtrl.WriteRows(parameter_ShortagePenalty, "Parameter_ShortagePenalty");
    }
}
```

`Dataload` 必須是 `public sealed partial class`，並繼承 `DataContext`。

正式載入入口是：

```csharp
var data = OptData.Load(() => new Dataload());
```

不要把裸的 `new Dataload()` 當作正式載入結果。

`OptData.Load` 會執行 generator 註冊與 framework 資料檢查。

重複 key、數值 sanity 等問題會寫入 `DataIssues` 並輸出 warning。

它不會替專案檢查全格矩陣與跨表關聯。

它也不是所有資料錯誤都 fail-fast。

因此下一行必須呼叫專案自己的 `ValidateData`。

```csharp
var data = OptData.Load(() => new Dataload());
MiniProductionSolution.ValidateData(data);
```

載入完成後，把 `data` 視為唯讀。

---

## 8. 宣告三種 Variable

Variable 類別名稱的前綴決定 solver type。

| 前綴 | 類型 | 預設界限 |
| --- | --- | --- |
| `VariableB_` | Binary | 0 到 1 |
| `VariableI_` | Integer | 0 到 infinity |
| `VariableC_` | Continuous | 0 到 infinity |

這些前綴是 generator 與 `BuildVars<T>` 的契約。

它們不是單純的命名風格。

`Variable/VariableB_Open.cs`：

```csharp
using OptimFoundation.Modeling;

namespace MiniProduction;

[OptVar]
[OptDim<string>("Product")]
public sealed partial class VariableB_Open { }
```

`Variable/VariableI_Produce.cs`：

```csharp
using OptimFoundation.Modeling;

namespace MiniProduction;

[OptVar]
[OptDim<string>("Product")]
public sealed partial class VariableI_Produce { }
```

`Variable/VariableC_Shortage.cs`：

```csharp
using OptimFoundation.Modeling;

namespace MiniProduction;

[OptVar]
[OptDim<string>("Product")]
public sealed partial class VariableC_Shortage { }
```

在 model 組裝時用 `BuildVars<T>`：

```csharp
.AddVariables(engine => engine.BuildVars<VariableB_Open>(data.set_Product))
.AddVariables(engine => engine.BuildVars<VariableI_Produce>(data.set_Product))
.AddVariables(engine => engine.BuildVars<VariableC_Shortage>(data.set_Product))
```

零維 Variable 使用 `BuildVars<T>()`。

不要為了零維 Variable 建一個虛構的一元素 Set。

---

## 9. 實作 Objective

`Objective/ObjectiveFunction.cs`：

```csharp
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace MiniProduction;

public sealed class ObjectiveFunction
{
    private readonly List<Set_Product> products;
    private readonly List<Parameter_FixedCost> fixedCosts;
    private readonly double shortagePenalty;

    public ObjectiveFunction(
        List<Set_Product> products,
        List<Parameter_FixedCost> fixedCosts,
        double shortagePenalty)
    {
        this.products = products;
        this.fixedCosts = fixedCosts;
        this.shortagePenalty = shortagePenalty;
    }

    public void Build(OptEngine engine)
    {
        foreach (var product in products)
        {
            double fixedCost = fixedCosts
                .FindParameterOrLog(row => row.Product == product.Product, product.Product)?.QTY ?? 0.0;

            engine.AddLHS(fixedCost, new VariableB_Open { Product = product.Product });
            engine.AddLHS(shortagePenalty, new VariableC_Shortage { Product = product.Product });
        }

        engine.CreateMinimize();
    }
}
```

Objective 的 linear expression 用 `AddLHS` 建立。

最大化改用 `CreateMaximize()`。

建構子只接這條式子需要的資料。

---

## 10. 實作 Constraints

原始 Model.md 左側的項放 `AddLHS`。

原始 Model.md 右側的項放 `AddRHS`。

不要先代數移項再寫 code。

每完成一條式子，立刻呼叫一次 `CreateXxx`。

本文三種比較符號都各自展示。

### 10.1 Equal：需求平衡

`Constraint/Constraint_DemandBalance.cs`：

```csharp
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace MiniProduction;

public sealed class Constraint_DemandBalance : ConstraintBase
{
    private readonly List<Set_Product> products;
    private readonly List<Parameter_Demand> demands;

    public Constraint_DemandBalance(
        List<Set_Product> products,
        List<Parameter_Demand> demands)
    {
        this.products = products;
        this.demands = demands;
    }

    public void Build(OptEngine engine)
    {
        foreach (var product in products)
        {
            engine.AddLHS(1.0, new VariableI_Produce { Product = product.Product });
            engine.AddLHS(1.0, new VariableC_Shortage { Product = product.Product });

            double demand = demands
                .FindParameterOrLog(row => row.Product == product.Product, product.Product)?.QTY ?? 0.0;
            engine.AddRHS(demand);

            engine.CreateEqual(this, product.Product);
        }
    }
}
```

`CreateEqual(this, product.Product)` 建立一條單索引 constraint。

名稱會由 owner 與原始維度值產生。

不要手工拼 `Constraint_DemandBalance@A`。

### 10.2 LessEqual：總產能

`Constraint/Constraint_TotalCapacity.cs`：

```csharp
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace MiniProduction;

public sealed class Constraint_TotalCapacity : ConstraintBase
{
    private readonly List<Set_Product> products;
    private readonly double capacity;

    public Constraint_TotalCapacity(List<Set_Product> products, double capacity)
    {
        this.products = products;
        this.capacity = capacity;
    }

    public void Build(OptEngine engine)
    {
        foreach (var product in products)
            engine.AddLHS(1.0, new VariableI_Produce { Product = product.Product });

        engine.AddRHS(capacity);
        engine.CreateLessEqual(this);
    }
}
```

這條 constraint 沒有索引，因此 `CreateLessEqual` 只傳 owner。

### 10.3 GreaterEqual：最低啟用量

`Constraint/Constraint_MinimumWhenOpen.cs`：

```csharp
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace MiniProduction;

public sealed class Constraint_MinimumWhenOpen : ConstraintBase
{
    private readonly List<Set_Product> products;

    public Constraint_MinimumWhenOpen(List<Set_Product> products)
    {
        this.products = products;
    }

    public void Build(OptEngine engine)
    {
        foreach (var product in products)
        {
            engine.AddLHS(1.0, new VariableI_Produce { Product = product.Product });
            engine.AddRHS(1.0, new VariableB_Open { Product = product.Product });
            engine.CreateGreaterEqual(this, product.Product);
        }
    }
}
```

這裡保留 Model.md 的方向：`Produce` 在左，`Open` 在右。

### 10.4 LessEqual：啟用上界連結

`Constraint/Constraint_ProduceOnlyWhenOpen.cs`：

```csharp
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace MiniProduction;

public sealed class Constraint_ProduceOnlyWhenOpen : ConstraintBase
{
    private readonly List<Set_Product> products;
    private readonly List<Parameter_Demand> demands;

    public Constraint_ProduceOnlyWhenOpen(
        List<Set_Product> products,
        List<Parameter_Demand> demands)
    {
        this.products = products;
        this.demands = demands;
    }

    public void Build(OptEngine engine)
    {
        foreach (var product in products)
        {
            double demand = demands
                .FindParameterOrLog(row => row.Product == product.Product, product.Product)?.QTY ?? 0.0;

            engine.AddLHS(1.0, new VariableI_Produce { Product = product.Product });
            engine.AddRHS(demand, new VariableB_Open { Product = product.Product });
            engine.CreateLessEqual(this, product.Product);
        }
    }
}
```

三個 hard constraint 完成 API 的核心對應：

| Model.md | API |
| --- | --- |
| 左側常數或變數項 | `AddLHS` |
| 右側常數或變數項 | `AddRHS` |
| `=` | `CreateEqual` |
| `<=` | `CreateLessEqual` |
| `>=` | `CreateGreaterEqual` |

---

## 11. 實作資料驗收與解驗證

`Solution/MiniProductionSolution.cs`：

```csharp
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace MiniProduction;

public sealed class MiniProductionSolution
{
    private const double Tolerance = 1e-6;

    public static void ValidateData(Dataload data)
    {
        if (data.DataIssues.Count > 0)
            throw new InvalidOperationException($"Framework 資料檢查發現 {data.DataIssues.Count} 個問題。");

        foreach (var product in data.set_Product)
        {
            Require(
                data.parameter_Demand.Any(row => row.Product == product.Product),
                $"Parameter_Demand 缺少 {product.Product}");
            Require(
                data.parameter_FixedCost.Any(row => row.Product == product.Product),
                $"Parameter_FixedCost 缺少 {product.Product}");

            double demand = data.parameter_Demand
                .Single(row => row.Product == product.Product).QTY;
            double fixedCost = data.parameter_FixedCost
                .Single(row => row.Product == product.Product).QTY;
            Require(demand >= 0.0, $"{product.Product} 的 Demand 不得為負數");
            Require(fixedCost >= 0.0, $"{product.Product} 的 FixedCost 不得為負數");
        }

        double capacity = data.parameter_Capacity.Single().QTY;
        double penalty = data.parameter_ShortagePenalty.Single().QTY;

        Require(capacity >= 0.0, "Capacity 不得為負數");
        Require(penalty > 0.0, "ShortagePenalty 必須大於零");
    }

    public static void ReadAndValidate(OptEngine engine, Dataload data)
    {
        var open = engine.GetSetVarValues<VariableB_Open>();
        var produce = engine.GetSetVarValues<VariableI_Produce>();
        var shortage = engine.GetSetVarValues<VariableC_Shortage>();

        ValidateRules(open, produce, shortage, data);

        CsvCtrl.WriteSolution<VariableB_Open>(engine, "MiniProduction", "SYSTEM");
        CsvCtrl.WriteSolution<VariableI_Produce>(engine, "MiniProduction", "SYSTEM");
        CsvCtrl.WriteSolution<VariableC_Shortage>(engine, "MiniProduction", "SYSTEM");
    }

    private static void ValidateRules(
        Dictionary<string, double> open,
        Dictionary<string, double> produce,
        Dictionary<string, double> shortage,
        Dataload data)
    {
        double totalProduce = 0.0;

        foreach (var product in data.set_Product)
        {
            string key = product.Product;
            double openValue = ValueOf(open, $"VariableB_Open@{key}");
            double produceValue = ValueOf(produce, $"VariableI_Produce@{key}");
            double shortageValue = ValueOf(shortage, $"VariableC_Shortage@{key}");
            double demand = data.parameter_Demand
                .FindParameterOrLog(row => row.Product == key, key)?.QTY ?? 0.0;

            Require(
                Math.Abs(produceValue + shortageValue - demand) <= Tolerance,
                $"{key} 違反需求平衡");
            Require(
                produceValue + Tolerance >= openValue,
                $"{key} 違反最低啟用量");
            Require(
                produceValue <= demand * openValue + Tolerance,
                $"{key} 違反啟用上界連結");
            Require(
                Math.Abs(openValue - Math.Round(openValue)) <= Tolerance &&
                openValue >= -Tolerance && openValue <= 1.0 + Tolerance,
                $"{key} 的 Open 不是 binary");
            Require(
                Math.Abs(produceValue - Math.Round(produceValue)) <= Tolerance &&
                produceValue >= -Tolerance,
                $"{key} 的 Produce 不是非負整數");
            Require(shortageValue >= -Tolerance, $"{key} 的 Shortage 為負數");

            totalProduce += produceValue;
        }

        double capacity = data.parameter_Capacity.Single().QTY;
        Require(totalProduce <= capacity + Tolerance, "總產量超過 Capacity");
    }

    private static double ValueOf(Dictionary<string, double> values, string key) =>
        values.TryGetValue(key, out double value) ? value : 0.0;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"[Validate] {message}");
    }
}
```

資料驗收發生在建模前。

解驗證發生在 solve 成功後。

兩者都要丟例外阻止錯誤結果繼續流動。

第一次完成 `ValidateRules` 後，要故意改壞一個 key 或比較符號。

確認驗證確實會失敗，再把修改還原。

---

## 12. 組裝 OptModel

canonical model 集中在一個方法：

```csharp
private static OptModel BuildModel(Dataload data)
{
    double capacity = data.parameter_Capacity.Single().QTY;
    double shortagePenalty = data.parameter_ShortagePenalty.Single().QTY;

    return new OptModel("Canonical")
        .AddVariables(engine => engine.BuildVars<VariableB_Open>(data.set_Product))
        .AddVariables(engine => engine.BuildVars<VariableI_Produce>(data.set_Product))
        .AddVariables(engine => engine.BuildVars<VariableC_Shortage>(data.set_Product))
        .AddObjective(engine => new ObjectiveFunction(
            data.set_Product,
            data.parameter_FixedCost,
            shortagePenalty).Build(engine))
        .AddConstraints(engine => new Constraint_DemandBalance(
            data.set_Product,
            data.parameter_Demand).Build(engine))
        .AddConstraints(engine => new Constraint_TotalCapacity(
            data.set_Product,
            capacity).Build(engine))
        .AddConstraints(engine => new Constraint_MinimumWhenOpen(
            data.set_Product).Build(engine))
        .AddConstraints(engine => new Constraint_ProduceOnlyWhenOpen(
            data.set_Product,
            data.parameter_Demand).Build(engine));
}
```

順序固定為 Variables、Objective、Constraints。

每種 variable、objective 與 constraint 各占一個 fluent call。

同一份 canonical `data` 供 production 與 experiment 使用。

---

## 13. Program：三態 CLI 與兩軸流程

執行模式有三個主要入口：

| CLI | 行為 |
| --- | --- |
| `import-data <raw>` | 整理或產生 canonical CSV，完成後立即離開 |
| `exp` | 對同一模型跑 experiment，完成後立即離開 |
| 無參數 | 使用 production baseline 正式求解 |

完整 `Program.cs` 骨架如下：

```csharp
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace MiniProduction;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "import-data")
        {
            var imported = OptData.Load(() => new Dataload(args[1]));
            MiniProductionSolution.ValidateData(imported);
            imported.Export();
            return 0;
        }

        bool isExperiment = args.Any(arg =>
            string.Equals(arg, "exp", StringComparison.OrdinalIgnoreCase));
        using var project = new OptProject("MiniProduction");

        var projectConfig = new ProjectConfig
        {
            EnableSolverLog = true,
            ExportLP = true,
            ExportSol = true,
        };

        var productionBaseline = new CplexConfig
        {
            MipGap = 0.01,
            TimeLimit = 300,
            Threads = 8,
            ParallelMode = 1,
            Seed = 11,
        };

        var data = OptData.Load(() => new Dataload());
        MiniProductionSolution.ValidateData(data);
        OptModel model = BuildModel(data);

        if (isExperiment)
        {
            var experiment = project.Experiment(
                "tuning-r0",
                "R0 校準：baseline x 5 seeds");
            experiment.AddModel(model);

            foreach (int seed in new[] { 11, 22, 33, 44, 55 })
            {
                var config = productionBaseline.Clone();
                config.Seed = seed;
                experiment.AddConfig($"r0-baseline-s{seed}", config);
            }

            var result = experiment.Run();
            foreach (var trial in result.Trials)
            {
                Logging.Info(
                    $"[Experiment] {trial.Label} " +
                    $"status={trial.Metrics.Status} " +
                    $"solveTimeMs={trial.Metrics.SolveTimeMs:F0}");
            }

            return 0;
        }

        project.LoadConfig(projectConfig);
        bool solved = project.Solve(
            model,
            productionBaseline,
            onSolved: engine => MiniProductionSolution.ReadAndValidate(engine, data));
        return solved ? 0 : 1;
    }

    private static OptModel BuildModel(Dataload data)
    {
        double capacity = data.parameter_Capacity.Single().QTY;
        double shortagePenalty = data.parameter_ShortagePenalty.Single().QTY;

        return new OptModel("Canonical")
            .AddVariables(engine => engine.BuildVars<VariableB_Open>(data.set_Product))
            .AddVariables(engine => engine.BuildVars<VariableI_Produce>(data.set_Product))
            .AddVariables(engine => engine.BuildVars<VariableC_Shortage>(data.set_Product))
            .AddObjective(engine => new ObjectiveFunction(
                data.set_Product,
                data.parameter_FixedCost,
                shortagePenalty).Build(engine))
            .AddConstraints(engine => new Constraint_DemandBalance(
                data.set_Product,
                data.parameter_Demand).Build(engine))
            .AddConstraints(engine => new Constraint_TotalCapacity(
                data.set_Product,
                capacity).Build(engine))
            .AddConstraints(engine => new Constraint_MinimumWhenOpen(
                data.set_Product).Build(engine))
            .AddConstraints(engine => new Constraint_ProduceOnlyWhenOpen(
                data.set_Product,
                data.parameter_Demand).Build(engine));
    }
}
```

### 13.1 import-data 狀態

執行：

```powershell
dotnet run --project .\Projects\MiniProduction\MiniProduction.csproj -- import-data raw-source
```

此模式讀取 `Input/raw-source.csv`，依本節定義的 raw schema 拆成 canonical Set 與 Parameter CSV。

它輸出 canonical CSV 後立即 `return 0`。

不要在同一次執行接著 build model 或 solve。

### 13.2 exp 狀態

執行：

```powershell
dotnet run --project .\Projects\MiniProduction\MiniProduction.csproj -- exp
```

此模式使用同一份 data、同一顆 model 與 baseline clone。

R0 先固定其他條件，只改 Seed。

### 13.3 default 狀態

執行：

```powershell
dotnet run --project .\Projects\MiniProduction\MiniProduction.csproj
```

此模式載入 `ProjectConfig`，再用 production `CplexConfig` 正式求解。

`onSolved` 讀取並驗證解。

---

## 14. ProjectConfig 與 CplexConfig

兩種 config 管不同層級。

### ProjectConfig

`ProjectConfig` 管專案輸出行為。

本文使用：

```csharp
var projectConfig = new ProjectConfig
{
    EnableSolverLog = true,
    ExportLP = true,
    ExportSol = true,
};
```

`EnableSolverLog` 控制 solver log。

`ExportLP` 控制模型匯出。

`ExportSol` 控制 solution 匯出。

### CplexConfig

`CplexConfig` 管求解器參數。

本文 baseline 明確設定：

- `MipGap`
- `TimeLimit`
- `Threads`
- `ParallelMode`
- `Seed`

production 只保留一個 baseline/champion。

Experiment 對 baseline 呼叫 `Clone()`，再改本輪唯一的實驗因子。

不要讓 production config 與 experiment config 各自從零建立而逐漸漂移。

---

## 15. Experiment schema v10

`Experiment.Run()` 與 `OptProject.Solve()` 都把紀錄接在 `FolderDir.Experiment` 下這個專案的累積檔尾端。

檔案依功能分、不依實驗或批次分，每個專案只有四個：

- `{專案名}-trial.csv`：主表，一列一個 trial。
- `{專案名}-meta.csv`：說明檔，每批（RunId）一份。
- `{專案名}-summary.csv`：彙總，每組設定一列；正式求解不寫。
- `{專案名}-trajectory.csv`：收斂軌跡，一列一個軌跡點；有軌跡點才寫。

四個檔的前三欄都是 `RecordedAt`（寫入時間）、`Experiment`（實驗名；正式求解是 `solve`）、`RunId`（批次）。

同名實驗再跑一次會多一批 RunId，舊列不改不刪。

只看某一輪時，篩 `Experiment` + `RunId`。

`RunId` 是開始時間 `yyyyMMdd-HHmmss`；同一個 process 同一秒內再開一批時加 `-2`、`-3`。

檔案表頭跟這一版不同時，舊檔改名成 `{檔名}-old-<時間>.csv` 保留，另開新檔，並留 WARN。

寫不進去（例：檔案被 Excel 開著）時，這次改寫到 `{檔名}-locked-<時間>.csv`，並留 WARN。

保留期清理不清 Experiment，累積檔只會長大。

### 15.1 主表：21 欄

schema v10 主表（`-trial.csv`）欄位依序為：

1. `RecordedAt`
2. `Experiment`
3. `RunId`
4. `TrialId`
5. `Model`
6. `ModelType`
7. `TrialLabel`
8. `ConfigChanges`
9. `Seed`
10. `VsBaseline`
11. `Status`
12. `ObjectiveValue`
13. `BestBound`
14. `Gap`
15. `BuildAndSolveTimeMs`
16. `SolveTimeMs`
17. `FirstSolutionMs`
18. `LastBoundChangeMs`
19. `BoundChange`
20. `NodeCount`
21. `IterationCount`

`VsBaseline` 是 framework 對同 seed baseline 的比較結果。

不要自行重算並覆寫它。

比法依序：有沒有找到解 → 有沒有證明最佳 → 都證明最佳比 `SolveTimeMs` → 都沒證明比 `Gap`。

`Gap` 是求解結果實際達到的 gap，不是 `CplexConfig.MipGap` 那個停止門檻。

`SolveTimeMs` 只計 `Solve()`；`BuildAndSolveTimeMs` = 建模 + 求解，只有經由 `OptProject.Solve` / `OptExperiment` 才有值，自己呼叫 `Trial.Capture` 時是 null（CSV 寫 `n/a`）。

同一批每列的基準是誰，看同 `Experiment` + `RunId` 的 `-meta.csv` 的 `baseline.label`；模型結構數量（VarCount、ConstraintCount 等）同一模型每列一樣，看 `-meta.csv` 的 `model.<Model>.*`。

缺值語意由同一批 `-meta.csv` 的 legend 解釋。

### 15.2 Summary：17 欄

`-summary.csv` 欄位依序為：

1. `RecordedAt`
2. `Experiment`
3. `RunId`
4. `Model`
5. `Config`
6. `IsBaseline`
7. `Trials`
8. `Seeds`
9. `Optimal`
10. `Feasible`
11. `NoSolution`
12. `Failed`
13. `FoundSolution`
14. `Wins`
15. `Losses`
16. `Ties`
17. `NotCompared`

Summary 提供 status count 與同 seed 的 W/L/T/NotCompared count。

它沒有 sgm、PAR10、theta、gap 平均或 improvement 欄位；framework 不算這些統計，勝負直接看 `Wins` / `Losses`。

### 15.3 Meta

`-meta.csv` 欄位為 `RecordedAt`、`Experiment`、`RunId`、`Section`、`Key`、`Value`。

每批寫一份完整說明：`schema`、`legend`、`experiment.description`、`run.startedAt`、`run.trialCount`、`model.*`、`modelStats.*`、`environment.*`、`baseline.*`。

### 15.4 Trajectory 的條件

`-trajectory.csv` 欄位為 `RecordedAt`、`Experiment`、`RunId`、`TrialId`、`TrialLabel`、`PointIndex`、`ElapsedMs`、`ObjectiveValue`、`BestBound`、`Gap`；`Experiment` + `RunId` + `TrialId` 對回主表那一列。

Trajectory 只有在該 trial 啟用 trajectory 收集、且 CPLEX 實際呼叫 callback 時才有資料。

沒有 trajectory 檔案或點數為零，不等於 solver 沒有運行。

比較 `FirstSolutionMs`、`LastBoundChangeMs` 或 `BoundChange` 前，先確認該輪確實啟用且資料完整。

---

## 16. Build、Run 與驗證順序

### 16.1 Build

```powershell
dotnet build .\Projects\MiniProduction\MiniProduction.csproj
```

先處理 compiler error 與 generator diagnostic。

常見檢查：

- class 是否為 `partial`。
- attribute 是否正確。
- Variable 名稱是否有合法 B/C/I 前綴。
- 三個 framework `ProjectReference` 路徑是否正確。
- Generators reference 是否以 Analyzer 載入。
- `Generated/` 是否被排除編譯。

### 16.2 確認 generator 產物

build 後查看 `Generated/`。

確認 Set、Parameter、Variable 產生預期 property。

確認 Dataload 產生註冊碼。

執行時的資料載入摘要不應顯示 `Sets（0）` 或 `Parameters（0）`。

### 16.3 Run production

```powershell
dotnet run --project .\Projects\MiniProduction\MiniProduction.csproj
```

檢查：

- Input CSV 成功載入。
- `DataIssues` 為空。
- `ValidateData` 通過。
- model build 完成。
- solver 狀態可接受。
- `ReadAndValidate` 沒有丟例外。
- LP 與 solution 輸出符合 `ProjectConfig`。

### 16.4 Run experiment

```powershell
dotnet run --project .\Projects\MiniProduction\MiniProduction.csproj -- exp
```

檢查：

- 五個 seed 都有 trial。
- `-trial.csv` 正好是 schema v10 的 21 欄，前三欄是 RecordedAt / Experiment / RunId。
- summary 正好是 17 欄。
- metadata 的 schema 與 legend 存在。
- trajectory 只在啟用時出現。

### 16.5 反向驗證

至少做一次刻意破壞：

1. 將 `ValidateRules` 的 `<= Capacity` 暫時改成錯誤比較。
2. 執行一個已知解。
3. 確認程式丟出驗證例外。
4. 還原程式碼。

這能證明 validation 不是只會成功的裝飾。

---

## 17. 除錯路線

### 17.1 找不到 CSV

先看 build output 的 `Input/`，不要只看 source 的 `Data/`。

若缺檔，檢查 csproj 的 `None Include`、`Link` 與 `CopyToOutputDirectory`。

### 17.2 Set 或 Parameter 數量是零

檢查：

- `Dataload` 是否 `partial`。
- `Dataload` 是否繼承 `DataContext`。
- Set 是否有 `[OptSet]`。
- Parameter 是否有 `[OptParam]`。
- generator 是否以 Analyzer 掛入。

### 17.3 `OPTF001` 或 variable type 錯誤

檢查類名是否以 `VariableB_`、`VariableI_` 或 `VariableC_` 開頭。

一般建立一律先用 `BuildVars<T>`。

### 17.4 Constraint 數值不對

逐條對照 Model.md：

1. 量詞的迴圈是否完整。
2. domain 是否正確。
3. 左側是否全部使用 `AddLHS`。
4. 右側是否全部使用 `AddRHS`。
5. 比較符號是否選對 `CreateXxx`。
6. `FindParameterOrLog` 的 key 是否完整。
7. `CreateXxx(this, dims)` 的 dims 是否使用原始值。

### 17.5 解值看起來都是零

先看 solver status 與 objective。

再確認讀取 key 的格式。

多維 key 必須依 property 順序組成。

日期維度使用 `yyyy_MM_dd`。

不要直接內插整個 row object。

### 17.6 Solve 失敗

保留 solver log 與匯出的 LP。

先辨認 infeasible、unbounded、time limit 或執行期例外。

不要用放寬 constraint 的方式掩蓋未知原因。

若懷疑資料，回到 `ValidateData` 與 Input CSV。

若懷疑模型，從最小資料集逐條啟用 constraint。

---

## 18. 禁用與不建議 API

新程式碼不要使用舊縮寫名稱：

| 不使用 | 使用 |
| --- | --- |
| `CreateLe` | `CreateLessEqual` |
| `CreateGe` | `CreateGreaterEqual` |
| `CreateEq` | `CreateEqual` |
| `CreateLeSoft` | `CreateLessEqualSoft` |
| `CreateGeSoft` | `CreateGreaterEqualSoft` |
| `CreateEqSoft` | `CreateEqualSoft` |

canonical hard model 不使用 soft constraint API。

只有 Model.md 明確定義 soft constraint 與 penalty 時，才可使用 soft API。

一般 variable 建立不優先使用 `BuildBVs`、`BuildIVs`、`BuildCVs`。

新程式碼使用 `BuildVars<T>`，讓類名前綴成為單一型別來源。

不要使用裸 `new Dataload()` 取代 `OptData.Load`。

不要在 Program 手工建立 CPLEX expression。

不要手工組 constraint name。

不要手工組日期 constraint key。

不要把缺少的 Parameter 默認成零後當作正常結果。

`FindParameterOrLog(predicate, keyValues)? .QTY ?? 0.0` 的 fallback 只避免組模時空參考。

真正的缺格必須在 `ValidateData` 擋下來。

不要在 Phase 2 未過 gate 時開始 tuning。

---

## 19. 交付前檢查

### Modeling 契約

- [ ] Model.md 已確認。
- [ ] 固定八段齊全。
- [ ] 每個 Set、Parameter、Variable 都能對到 code。
- [ ] Objective 的方向與係數一致。
- [ ] 每條 Constraint 的量詞、domain、左右式與比較符號一致。
- [ ] Validation Rules 已轉成可執行檢查。

### 專案結構

- [ ] 固定八資料夾存在。
- [ ] csproj 以 `ProjectReference` 引用 Core 與 Cplex。
- [ ] Generators `ProjectReference` 以 Analyzer 載入。
- [ ] `Generated/**/*.cs` 不重複編譯。
- [ ] Data CSV 複製到 output `Input/`。

### 資料與 generator

- [ ] Set、Parameter、Variable 類別都是 `partial`。
- [ ] Dataload 是 `public sealed partial` 並繼承 `DataContext`。
- [ ] 正式入口使用 `OptData.Load`。
- [ ] `ValidateData` 緊接在 Load 後。
- [ ] 資料載入摘要的 Set/Parameter 數量正確。
- [ ] `DataIssues` 已處理。

### 模型

- [ ] Variable 使用合法 B/C/I 前綴。
- [ ] 一般建立使用 `BuildVars<T>`。
- [ ] Objective 在 Constraints 前加入。
- [ ] 左式使用 `AddLHS`。
- [ ] 右式使用 `AddRHS`。
- [ ] `=`, `<=`, `>=` 使用正確的完整 API 名稱。
- [ ] `CreateXxx` 直接接收原始維度值。

### 執行與驗證

- [ ] import-data 完成後立即結束。
- [ ] exp 與 default 共用 canonical model。
- [ ] production baseline 唯一且可重現。
- [ ] 解值逐條代回 Validation Rules。
- [ ] 已做一次刻意破壞驗證。
- [ ] build 成功。
- [ ] production run 成功。
- [ ] experiment 輸出符合 schema v10。

### Tuning gate

- [ ] Phase 2 正確性 gate 全部通過。
- [ ] R0 baseline 使用多個 seed。
- [ ] 每輪只改已聲明的實驗因子。
- [ ] 直接使用 summary 的 count 與 W/L/T。
- [ ] 額外聚合統計從主表依公式計算並記錄來源。

完成這份檢查後，才把專案交給 production 或 Phase 3 Tuning。

---

## API reference 使用規則

以下 catalog 完整保留目前 source 的 public signature，並依 source module 分組。搭配前面的架構章節閱讀：

- **Recommended**：`OptData.Load`、typed `BuildVars<T>`、owner/dim constraint overload、`OptModel`、`OptProject`、`OptExperiment`。
- **Advanced**：直接控制 `OptEngine`、string builders、bounds/reset、special constraints、import/export、conflict、copy/merge/thread。
- **Framework integration**：generator base types、registration DTO、IO/DB contracts、CSV writers、logging 與 infrastructure helpers。

方法共同前置條件：builder 需在 solve 前；solution getter 需在有 incumbent 後；檔案 API 會讀寫磁碟；DB transaction API 可能 commit/rollback；native CPLEX API 要求 objects 屬於相容且未 dispose 的 engine。DTO property 是狀態快照或設定，不會自行觸發 solve。

### 高風險 lifecycle 摘要

- `AddLHS`、`AddRHS` 共用 expression pool；每個 `Create*` 消耗 pool。用 `HasPool`、`PoolState` 檢查，`ClearPool` 放棄未完成式子。
- `BuildVars<T>` 是一般入口；`BuildCVs/IVs/BVs` 與 string overload 用於需要直接控制型別、bounds 或動態 schema 時。
- `Build()` 後、`Solve()` 前加入 MIP start；key 使用 canonical variable name。
- `EnableTrajectory()` 在 solve 前呼叫；先查 `SupportsTrajectory`。
- `VariableBuildCounts`、`ConstraintBuildCounts` 提供各群組 expected/actual；宣告了卻沒被引用的變數由 `Solve()` 前的 `[UNREFERENCED_VARIABLES]` WARN 點名。
- `ExportModel`、`ExportSolution` 的父目錄應先存在；`ReadSolution` 需要相容模型。
- `GetConflictConstraints()` 用於 infeasible 診斷。copy/merge/thread/reset APIs 都是進階 stateful 操作。
- `Experiment.Save()` 是一般輸出入口；writers 的 `Write` 供 framework integration。`ConfigSummary` 只有 status/incumbent/W-L-T counts。

## Consumer-callable interface contracts

Interface members 沒有重複寫 `public`，但仍是 consumer-callable API。以下八組契約另外納入 coverage。

### `ISolverConfig`

| Signature | 行為與使用時機 |
| --- | --- |
| `double? TimeLimit { get; set; }` | 求解秒數上限；null 保留 solver default。 |
| `double? MipGap { get; set; }` | 相對 MIP gap；建 engine/solve 前設定。 |
| `int? Threads { get; set; }` | solver thread 數；null 交由 solver 決定。 |
| `int? Seed { get; set; }` | random seed；需要重現時固定。 |
| `int? Emphasis { get; set; }` | solver-specific MIP emphasis 整數。 |
| `double? FeasibilityTol { get; set; }` | primal feasibility tolerance。 |
| `double? OptimalityTol { get; set; }` | optimality tolerance。 |
| `int? RootAlgorithm { get; set; }` | root LP algorithm 的 solver-specific 值。 |
| `int? Presolve { get; set; }` | presolve 設定；0 表示關閉。 |
| `double? HeuristicEffort { get; set; }` | heuristic effort。 |
| `double? MemoryLimitMb { get; set; }` | solver work memory 上限 MB。 |
| `int ScaleWarnThreshold { get; }` | 變數數量警告門檻；預設 10,000,000，只警告不阻止 solve。 |

這些 properties 由 solver config 實作。讀寫本身不建立模型；`LoadConfig`/`Build` 才把值套進 solver。

### `ISolverEngine`

| Signature | 行為、state 與回傳值 |
| --- | --- |
| `ISolverConfig Config { get; }` | 目前 engine 設定。 |
| `SolveStatus Status { get; }` | 最近求解狀態；尚未 solve 為 `NotSolved`。 |
| `SolveMetrics LastMetrics { get; }` | 最近一次 solve snapshot；尚未 solve 為 null。 |
| `ModelType ModelType { get; }` | 依目前 native model 判定 LP/MILP/IP/BP；build 後可讀。 |
| `void Build()` | 建立 solver model 並套 config；建立變數、讀模型或限制式前呼叫。 |
| `bool Solve()` | 求解並更新 status/metrics；只有 Optimal 或 Feasible 回傳 true。 |
| `double GetObjectiveValue()` | 有可用 solution 後回傳 objective value，否則依 implementation 拋例外。 |
| `double GetVariableValue(string name)` | 以 canonical full name 取值；要求有 solution 且名稱存在。 |
| `IReadOnlyDictionary<string, double> GetSolution(string varTypeName = null)` | 回傳全部解，或依 variable type prefix 篩選；不修改模型。 |
| `int AddMIPStart(IReadOnlyDictionary<string, double> values, string name = null)` | build 完、solve 前加入 warm start，回傳實際套用變數數；LP 會警告並回傳 0。 |
| `void Dispose()` | 釋放 solver/native resources；dispose 後不可再使用 engine。 |

### `ISpecialConstraints<TVar, TExpr>`

這是目前 source 中承擔 advanced constraint backend 的 public contract；目前沒有名為 `IAdvancedConstraintBackend` 的 public type。若其他文件使用 `IAdvancedConstraintBackend`，對應的實際 API 是這個介面。

| Signature | 行為、時機與副作用 |
| --- | --- |
| `void AddSOS1(IEnumerable<TVar> vars)` | build 後、solve 前把 native variables 加成 SOS1；最多一個非零，會修改 native model。 |
| `void AddSOS2(IEnumerable<TVar> vars)` | build 後、solve 前建立 SOS2；最多兩個相鄰項非零，variables 必須屬於目前 engine。 |
| `void AddIndicator(TVar binary, TExpr expr, ConstraintSense sense, double rhs)` | 建立 `binary = 1` 時強制 expression 成立的 indicator；binary/expr 必須來自目前 native model。 |
| `void AddLazyConstraint(TExpr expr, ConstraintSense sense, double rhs)` | 註冊候選解階段才檢查的 lazy constraint；會改變 solve callback/model 行為，solve 前呼叫。 |

`TVar`、`TExpr` 是 solver-native types。這些方法沒有 framework expression-pool 回傳值，也不消耗 `AddLHS`/`AddRHS` pool。

### `IDataSource`

| Signature | 行為與使用時機 |
| --- | --- |
| `DataTable LoadData(string sourceName)` | 直接載入表格；CSV 將名稱解為 input file、DB 將它視為 SQL、memory source 將它視為 registration name。 |
| `List<T> Load<T>(string sourceName = null) where T : ModelElementBase, new()` | default method；省略名稱時使用 `typeof(T).Name`，以 public property 名映射 rows 並回傳 typed list。 |

兩者都可能讀檔或查 DB；schema/型別不相容會在 mapping 階段失敗。

### `ISolutionSink`

| Signature | 行為與使用時機 |
| --- | --- |
| `void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null)` | 有 solution 後輸出指定 variable class；CSV 寫檔，Oracle 寫 DB。 |
| `ISolutionBatch BeginBatch(string dataId = null, string userId = null)` | 建立多 variable class 的 batch；回傳物件需要 `Commit`/`Dispose`。 |

### `ISolutionBatch`

| Signature | 行為與使用時機 |
| --- | --- |
| `void Write<TVariableClass>(ISolverEngine engine)` | 將指定 class 的 solution 加入 batch；Oracle 延後到 commit，CSV implementation 可立即寫檔。 |
| `void Commit()` | 完成 batch；Oracle 在同一 transaction 寫入且失敗 rollback，CSV 已寫內容不回復。 |
| `void Dispose()` | 釋放 batch；Oracle 未 commit 的 pending writes 會捨棄。 |

### `IDbCtrl`

| Signature | 行為、回傳與交易副作用 |
| --- | --- |
| `void Open()` | 開啟連線；provider 可採每次操作自行取 pool connection。 |
| `void Close()` | 關閉 controller 連線；`Dispose` 預設會呼叫。 |
| `DataTable Query(string sql, params (string name, object value)[] parameters)` | 執行 parameterized SELECT 並回傳結果集。 |
| `void NonQuery(string sql, params (string name, object value)[] parameters)` | 執行不需要 row count 的 DDL/DML。 |
| `int Execute(string sql, params (string name, object value)[] parameters)` | 執行 INSERT/UPDATE/DELETE 並回傳受影響列數。 |
| `TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters)` | 執行 scalar query 並轉成 TResult。 |
| `void ExecuteInTransaction(Action<IDbCtrl> work)` | 同一 transaction 執行 callback；成功 commit，例外 rollback 並重拋。 |
| `void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows)` | 同一 SQL 批次套用多列參數；transaction 內須沿用現有 connection/transaction。 |
| `void Dispose()` | 釋放 controller，通常關閉連線。 |

### `ITrajectorySource`

| Signature | 行為與必要 state |
| --- | --- |
| `bool SupportsTrajectory { get; }` | 表示 implementation 能否記錄收斂軌跡。 |
| `void EnableTrajectory()` | solve 前開啟記錄；不支援的 engine 不做事。 |
| `IReadOnlyList<ConvergencePoint> Trajectory { get; }` | 最近一次 solve 的軌跡；未開啟、不支援或未 solve 時為空。 |

舊 inventory 將最後一項記成 `GetTrajectory`；目前 source 的 consumer-callable signature 是 `Trajectory` property，沒有 `GetTrajectory()` method。

### Source-generated attributes

| Signature | 用途與時機 |
| --- | --- |
| `sealed class OptSetAttribute : Attribute` | 編譯期標記 set declaration，讓 generator 建 registration code。 |
| `sealed class OptParamAttribute : Attribute` | 編譯期標記 parameter declaration。 |
| `sealed class OptVarAttribute : Attribute` | 編譯期標記 variable declaration。 |
| `sealed class OptDimAttribute<T> : Attribute` | 宣告一個型別化 dimension。 |
| `string OptDimAttribute<T>.Name { get; }` | dimension 的明確名稱，由 constructor 保存。 |
| `OptDimAttribute<T>(string name)` | 建 attribute 並保存 dimension name；只影響 generated metadata，runtime 不建立變數。 |

<!-- PUBLIC API REFERENCE GENERATED FROM INVENTORY -->

## 完整 public API catalog

`Guide` means only that the current guide contains the symbol text; it does not prove full behavioral or overload coverage. Public classes with no explicit constructor have the normal implicit public parameterless constructor unless static/abstract; the source line is the class declaration.

### `OptimFoundation.Core/DataContext.cs`

- `public enum DataIssueKind` | `OptimFoundation.Core/DataContext.cs:13`
- `public sealed class DataIssue` | `OptimFoundation.Core/DataContext.cs:15`
- `public DataIssueKind Kind` | `OptimFoundation.Core/DataContext.cs:17`
- `public string Parameter` | `OptimFoundation.Core/DataContext.cs:18`
- `public string Detail` | `OptimFoundation.Core/DataContext.cs:19`
- `public DataIssue(DataIssueKind kind, string parameter, string detail)` | `OptimFoundation.Core/DataContext.cs:20` | 建立含 Kind、Parameter、Detail 的驗證問題物件；只保存資料，不做 IO。
- `public static class DataValidator` | `OptimFoundation.Core/DataContext.cs:25`
- `public const double MaxMagnitude = 1e15;` | `OptimFoundation.Core/DataContext.cs:27`
- `public static IReadOnlyList<DataIssue> Validate( IReadOnlyList<SetRegistration> sets, IReadOnlyList<ParamRegistration> parameters)` | `OptimFoundation.Core/DataContext.cs:30` | 用途：檢查資料或模型輸入；build/solve 前呼叫，不應改變 domain data。
- `public static IReadOnlyList<DataIssue> Validate(IReadOnlyList<ParamRegistration> parameters)` | `OptimFoundation.Core/DataContext.cs:46` | 用途：檢查資料或模型輸入；build/solve 前呼叫，不應改變 domain data。
- `public static class OptData` | `OptimFoundation.Core/DataContext.cs:114`
- `public static T Load<T>(System.Func<T> factory)` | `OptimFoundation.Core/DataContext.cs:116` | 從目前 source/factory 載入並回傳 typed data；呼叫前來源需可用，驗證或映射失敗會拋例外或留下 DataIssues。
- `public sealed class ParamRow` | `OptimFoundation.Core/DataContext.cs:144`
- `public object[] Index` | `OptimFoundation.Core/DataContext.cs:146`
- `public (string Name, double Value)[] Numbers` | `OptimFoundation.Core/DataContext.cs:147`
- `public ParamRow(object[] index, (string Name, double Value)[] numbers)` | `OptimFoundation.Core/DataContext.cs:148` | 建立對應資料 registration DTO，保存索引、欄位與 rows；供 DataContext/generator 整合，不觸發求解。
- `public sealed class SetRegistration` | `OptimFoundation.Core/DataContext.cs:156`
- `public string Name` | `OptimFoundation.Core/DataContext.cs:159`
- `public string[] IndexFields` | `OptimFoundation.Core/DataContext.cs:161`
- `public IReadOnlyList<object[]> Rows` | `OptimFoundation.Core/DataContext.cs:163`
- `public int RowCount` | `OptimFoundation.Core/DataContext.cs:165`
- `public SetRegistration(string name, string[] indexFields, IReadOnlyList<object[]> rows)` | `OptimFoundation.Core/DataContext.cs:168` | 建立對應資料 registration DTO，保存索引、欄位與 rows；供 DataContext/generator 整合，不觸發求解。
- `public sealed class ParamRegistration` | `OptimFoundation.Core/DataContext.cs:176`
- `public string Name` | `OptimFoundation.Core/DataContext.cs:178`
- `public string[] IndexFields` | `OptimFoundation.Core/DataContext.cs:179`
- `public IReadOnlyList<ParamRow> Rows` | `OptimFoundation.Core/DataContext.cs:180`
- `public int RowCount` | `OptimFoundation.Core/DataContext.cs:181`
- `public ParamRegistration(string name, string[] indexFields, IReadOnlyList<ParamRow> rows)` | `OptimFoundation.Core/DataContext.cs:182` | 建立對應資料 registration DTO，保存索引、欄位與 rows；供 DataContext/generator 整合，不觸發求解。
- `public abstract class DataContext` | `OptimFoundation.Core/DataContext.cs:191`
- `public IReadOnlyList<DataIssue> DataIssues` | `OptimFoundation.Core/DataContext.cs:198`
- `public static class ParameterLookupExtensions` | `OptimFoundation.Core/DataContext.cs:266`
- `public static T FindParameterOrLog<T>( this IEnumerable<T> rows, Func<T, bool> predicate, params object[] keyValues) where T : ParameterBase` | `OptimFoundation.Core/DataContext.cs:272` | 以 predicate 找 parameter row；找不到時以 keyValues 留下診斷並回傳空結果，組模查參數時使用。

### `OptimFoundation.Core/ModelElementBase.cs`

- `public abstract class ModelElementBase` | `OptimFoundation.Core/ModelElementBase.cs:10`
- `public void InitClassBySets(params object[] values)` | `OptimFoundation.Core/ModelElementBase.cs:20` | 依 public writable properties 順序轉型並填入維度值；數量或型別不合會拋例外。
- `public override string ToString()` | `OptimFoundation.Core/ModelElementBase.cs:77` | 回傳由型別名與維度 token 組成的 canonical name，供 variable/constraint lookup 與輸出使用。
- `public abstract class SetRowBase : ModelElementBase` | `OptimFoundation.Core/ModelElementBase.cs:82`
- `public override string ToString()` | `OptimFoundation.Core/ModelElementBase.cs:87` | 回傳由型別名與維度 token 組成的 canonical name，供 variable/constraint lookup 與輸出使用。
- `public abstract class ParameterBase : ModelElementBase` | `OptimFoundation.Core/ModelElementBase.cs:91`
- `public abstract class VariableBase : ModelElementBase` | `OptimFoundation.Core/ModelElementBase.cs:93`
- `public abstract class ConstraintBase : ModelElementBase` | `OptimFoundation.Core/ModelElementBase.cs:94`

### `OptimFoundation.Core/EngineBase.cs`

- `public interface ISolverConfig` | `OptimFoundation.Core/EngineBase.cs:13`
- `public interface ISolverEngine : IDisposable` | `OptimFoundation.Core/EngineBase.cs:54`
- `public interface ISpecialConstraints<TVar, TExpr>` | `OptimFoundation.Core/EngineBase.cs:92`
- `public enum SolveStatus` | `OptimFoundation.Core/EngineBase.cs:111`
- `public enum VarType` | `OptimFoundation.Core/EngineBase.cs:136`
- `public enum ConstraintSense` | `OptimFoundation.Core/EngineBase.cs:149`
- `public enum ObjectiveSense` | `OptimFoundation.Core/EngineBase.cs:162`
- `public enum ModelType` | `OptimFoundation.Core/EngineBase.cs:172`
- `public static class OptBounds` | `OptimFoundation.Core/EngineBase.cs:190`
- `public const double Infinity = 1E20;` | `OptimFoundation.Core/EngineBase.cs:193`
- `public abstract class EngineBase<TModel, TVar, TExpr, TConstr> : ISolverEngine, ITrajectorySource` | `OptimFoundation.Core/EngineBase.cs:203`
- `public int VariableCount` | `OptimFoundation.Core/EngineBase.cs:215`
- `public ModelType ModelType` | `OptimFoundation.Core/EngineBase.cs:229`
- `public ISolverConfig Config` | `OptimFoundation.Core/EngineBase.cs:232`
- `public SolveStatus Status` | `OptimFoundation.Core/EngineBase.cs:235`
- `public double BestObjValue` | `OptimFoundation.Core/EngineBase.cs:238`
- `public double MIPGap` | `OptimFoundation.Core/EngineBase.cs:241`
- `public SolveMetrics LastMetrics` | `OptimFoundation.Core/EngineBase.cs:244`
- `public virtual int ConstraintCount` | `OptimFoundation.Core/EngineBase.cs:247`
- `public IReadOnlyDictionary<string, (int Expected, int Actual)> VariableBuildCounts` | `OptimFoundation.Core/EngineBase.cs:284`
- `public IReadOnlyDictionary<string, (int Expected, int Actual)> ConstraintBuildCounts` | `OptimFoundation.Core/EngineBase.cs:288`
- `public virtual bool SupportsTrajectory` | `OptimFoundation.Core/EngineBase.cs:314`
- `public virtual void EnableTrajectory()` | `OptimFoundation.Core/EngineBase.cs:317` | 用途：啟用或擷取 solve/trajectory；enable 在 solve 前，capture/get 在 engine lifecycle 內。
- `public virtual IReadOnlyList<ConvergencePoint> Trajectory` | `OptimFoundation.Core/EngineBase.cs:320`
- `public (int LhsTerms, double LhsConst, int RhsTerms, double RhsConst) PoolState` | `OptimFoundation.Core/EngineBase.cs:343`
- `public ObjectiveSense ObjectiveSense` | `OptimFoundation.Core/EngineBase.cs:347`
- `public int ObjectiveTermCount` | `OptimFoundation.Core/EngineBase.cs:350`
- `public double ObjectiveConstant` | `OptimFoundation.Core/EngineBase.cs:353`
- `public int SoftConstraintCount` | `OptimFoundation.Core/EngineBase.cs:356`
- `public int SoftPenaltyTermCount` | `OptimFoundation.Core/EngineBase.cs:359`
- `public abstract void LoadConfig(ISolverConfig config);` | `OptimFoundation.Core/EngineBase.cs:394` | 用途：套用設定；build、solve 或 experiment run 前呼叫，設定型別必須相容。
- `public void Build()` | `OptimFoundation.Core/EngineBase.cs:451` | 用途：完成 engine build lifecycle；所有 recipe 已註冊後、solve 前呼叫。
- `public bool Solve()` | `OptimFoundation.Core/EngineBase.cs:469` | 用途：執行求解；模型與設定必須完成，會更新 status、metrics 與 solution state。
- `public abstract double GetObjectiveValue();` | `OptimFoundation.Core/EngineBase.cs:516` | 用途：讀取解；solve 成功或 solution 已載入後呼叫，不會修改模型。
- `public abstract double GetVariableValue(string name);` | `OptimFoundation.Core/EngineBase.cs:519` | 用途：讀取解；solve 成功或 solution 已載入後呼叫，不會修改模型。
- `public abstract void Dispose();` | `OptimFoundation.Core/EngineBase.cs:522` | 用途：釋放 native/IO resources；scope 結束時呼叫，之後不可再使用 instance。
- `public virtual void BuildCVs<TVariable>(params object[] sets)` | `OptimFoundation.Core/EngineBase.cs:644` | 用途：建立決策變數；在 objective/constraint/solve 前呼叫，dimensions 與 bounds 必須合法。
- `public virtual void BuildCVs<TVariable>(double lb, double ub, params object[] sets)` | `OptimFoundation.Core/EngineBase.cs:651` | 用途：建立決策變數；在 objective/constraint/solve 前呼叫，dimensions 與 bounds 必須合法。
- `public virtual void BuildIVs<TVariable>(params object[] sets)` | `OptimFoundation.Core/EngineBase.cs:658` | 用途：建立決策變數；在 objective/constraint/solve 前呼叫，dimensions 與 bounds 必須合法。
- `public virtual void BuildIVs<TVariable>(double lb, double ub, params object[] sets)` | `OptimFoundation.Core/EngineBase.cs:665` | 用途：建立決策變數；在 objective/constraint/solve 前呼叫，dimensions 與 bounds 必須合法。
- `public virtual void BuildBVs<TVariable>(params object[] sets)` | `OptimFoundation.Core/EngineBase.cs:672` | 用途：建立決策變數；在 objective/constraint/solve 前呼叫，dimensions 與 bounds 必須合法。
- `public virtual void BuildVars<TVariable>(params object[] sets)` | `OptimFoundation.Core/EngineBase.cs:683` | 用途：建立決策變數；在 objective/constraint/solve 前呼叫，dimensions 與 bounds 必須合法。
- `public virtual void BuildCVs(string setName, params object[] sets)` | `OptimFoundation.Core/EngineBase.cs:723` | 用途：建立決策變數；在 objective/constraint/solve 前呼叫，dimensions 與 bounds 必須合法。
- `public virtual void BuildCVs(string setName, double lb, double ub, params object[] sets)` | `OptimFoundation.Core/EngineBase.cs:727` | 用途：建立決策變數；在 objective/constraint/solve 前呼叫，dimensions 與 bounds 必須合法。
- `public virtual void BuildIVs(string setName, params object[] sets)` | `OptimFoundation.Core/EngineBase.cs:731` | 用途：建立決策變數；在 objective/constraint/solve 前呼叫，dimensions 與 bounds 必須合法。
- `public virtual void BuildIVs(string setName, double lb, double ub, params object[] sets)` | `OptimFoundation.Core/EngineBase.cs:735` | 用途：建立決策變數；在 objective/constraint/solve 前呼叫，dimensions 與 bounds 必須合法。
- `public virtual void BuildBVs(string setName, params object[] sets)` | `OptimFoundation.Core/EngineBase.cs:739` | 用途：建立決策變數；在 objective/constraint/solve 前呼叫，dimensions 與 bounds 必須合法。
- `public virtual void BuildVars(string setName, VarType type, params object[] sets)` | `OptimFoundation.Core/EngineBase.cs:746` | 用途：建立決策變數；在 objective/constraint/solve 前呼叫，dimensions 與 bounds 必須合法。
- `public string[] GetAllVarNames()` | `OptimFoundation.Core/EngineBase.cs:825` | 用途：列出變數池全部變數名（含軟性限制式彈性變數與匯入變數）；variables 建立或模型匯入後呼叫。
- `public string[] GetSetVarNames<TVariable>()` | `OptimFoundation.Core/EngineBase.cs:837` | 用途：查詢 canonical variable names；variables 建立或模型匯入後呼叫。
- `public string[] GetSetVarNames(string setName)` | `OptimFoundation.Core/EngineBase.cs:841` | 用途：查詢 canonical variable names；variables 建立或模型匯入後呼叫。
- `public Dictionary<string, double> GetSetVarValues<TVariable>()` | `OptimFoundation.Core/EngineBase.cs:849` | 用途：讀取解；solve 成功或 solution 已載入後呼叫，不會修改模型。
- `public Dictionary<string, double> GetSetVarValues(string setName)` | `OptimFoundation.Core/EngineBase.cs:853` | 用途：讀取解；solve 成功或 solution 已載入後呼叫，不會修改模型。
- `public virtual IReadOnlyDictionary<string, double> GetSolution(string varTypeName = null)` | `OptimFoundation.Core/EngineBase.cs:870` | 用途：讀取解；solve 成功或 solution 已載入後呼叫，不會修改模型。
- `public int AddMIPStart(IReadOnlyDictionary<string, double> values, string name = null)` | `OptimFoundation.Core/EngineBase.cs:911` | 用途：加入 warm start；solve 前呼叫，key 必須是目前模型的 canonical variable name。
- `public bool HasPool` | `OptimFoundation.Core/EngineBase.cs:1348`
- `public void ClearPool()` | `OptimFoundation.Core/EngineBase.cs:1354` | 用途：清除指定 framework/engine state；進階重用或錯誤復原時呼叫，既有 references 可能失效。
- `public bool AddLHS(double coeff, object varSpec)` | `OptimFoundation.Core/EngineBase.cs:1397` | 用途：加入 expression term/constant；建式時呼叫，會修改共用 pool，稍後由 Create* 消耗。
- `public bool AddLHS(double constant)` | `OptimFoundation.Core/EngineBase.cs:1422` | 用途：加入 expression term/constant；建式時呼叫，會修改共用 pool，稍後由 Create* 消耗。
- `public bool AddRHS(double coeff, object varSpec)` | `OptimFoundation.Core/EngineBase.cs:1434` | 用途：加入 expression term/constant；建式時呼叫，會修改共用 pool，稍後由 Create* 消耗。
- `public bool AddRHS(double constant)` | `OptimFoundation.Core/EngineBase.cs:1459` | 用途：加入 expression term/constant；建式時呼叫，會修改共用 pool，稍後由 Create* 消耗。
- `public bool CreateGreaterEqual(string name)` | `OptimFoundation.Core/EngineBase.cs:1494` | 驗證明確 name，將 pool 正規化為 `(LHS terms - RHS terms) >= (RhsConst - LhsConst)` 並加入 solver model。兩側都沒有變數項時記 warning、回傳 false 且保留 pool；同名 duplicate 不新增 model object但記 warning、清 pool並回傳 true；成功也清 pool並回傳 true。
- `public bool CreateGreaterEqual(ConstraintBase owner, params object[] dims)` | `OptimFoundation.Core/EngineBase.cs:1498` | 由 owner type 與 dims 組 canonical name，再把 LHS/RHS pool 建成 `>=` constraint並加入 model。空 variable pool 回傳 false且不清；duplicate 或成功都記 build count、清 pool並回傳 true。
- `public bool CreateGreaterEqual(double rhs, string name)` | `OptimFoundation.Core/EngineBase.cs:1506` | 驗證 name；LHS 沒有變數項時 warning、回傳 false並保留 pool。否則用 `rhs` 覆寫先前 `AddRHS(constant)` 的 RHS constant，既有 RHS variable terms 仍移到左側，再建立 `>=` constraint；duplicate/成功後清 pool並回傳 true。
- `public bool CreateLessEqual(string name)` | `OptimFoundation.Core/EngineBase.cs:1525` | 驗證明確 name，建立 `(LHS terms - RHS terms) <= (RhsConst - LhsConst)` 並加入 solver model。兩側無變數項時回傳 false且保留 pool；duplicate 不新增但清 pool並回傳 true；成功加入 model後同樣清 pool。
- `public bool CreateLessEqual(ConstraintBase owner, params object[] dims)` | `OptimFoundation.Core/EngineBase.cs:1529` | 由 owner+dims 產生 canonical name，使用完整 LHS/RHS pool 建 `<=` constraint。空 variable pool warning並回傳 false且不清；duplicate 或成功都清 pool並回傳 true。
- `public bool CreateLessEqual(double rhs, string name)` | `OptimFoundation.Core/EngineBase.cs:1534` | 驗證 name；若 LHS 沒有變數項則回傳 false並保留 pool。否則 `rhs` 取代既有 RHS constant，RHS variable terms 保留並以負係數移到左側，建立 `<=` constraint；duplicate/成功後清 pool並回傳 true。
- `public bool CreateEqual(string name)` | `OptimFoundation.Core/EngineBase.cs:1553` | 驗證明確 name，把兩側 pool 正規化後建立 equality並加入 solver model。沒有 LHS/RHS variable terms 時 warning、回傳 false且不清 pool；duplicate 略過新增但清 pool並回傳 true；成功也清 pool。
- `public bool CreateEqual(ConstraintBase owner, params object[] dims)` | `OptimFoundation.Core/EngineBase.cs:1557` | 由 owner type 與 dims 命名，將完整 pool 建成 equality。空 variable pool 回傳 false且保留；同名 duplicate 或成功建立都記錄結果、清 pool並回傳 true。
- `public bool CreateEqual(double rhs, string name)` | `OptimFoundation.Core/EngineBase.cs:1562` | 驗證 name；LHS 沒有變數項時 warning、回傳 false且不清 pool。否則用參數 `rhs` 覆寫 RHS constant，仍納入原 RHS variable terms，建立 equality；duplicate/成功後清 pool並回傳 true。
- `public bool CreateRange(double lb, double ub, string name)` | `OptimFoundation.Core/EngineBase.cs:1579` | 只取 LHS terms 建 `lb - LhsConst <= LHS <= ub - LhsConst` 並使用明確 name；RHS terms/constant 不參與，若存在會記 `[POOL_RHS_IGNORED]` warning 後捨棄。LHS 沒有變數項時清空 pool 並回傳 false；成功或 duplicate skip 後也清空整個 pool並回傳 true。
- `public bool CreateRange(double lb, double ub, ConstraintBase owner, params object[] dims)` | `OptimFoundation.Core/EngineBase.cs:1583` | 先由 owner+dims 產生 canonical name，再只消耗 LHS 建範圍限制式；任何 RHS pool 內容都會 warning 並忽略。LHS 空時清 pool、記 build failure 並回傳 false；其他完成路徑清 pool並回傳 true。
- `public void CreateMinimize()` | `OptimFoundation.Core/EngineBase.cs:1682` | 只取 LHS terms、LHS constant 與已累積 soft penalty terms，設定 `ObjectiveSense.Minimize` 並以 `SetObjective` 取代 solver objective；RHS pool 若有內容會記 `[POOL_RHS_IGNORED]` warning 後捨棄。LHS 與 soft penalty 都空時不建立 objective，即使只有 constant 也 skip；所有正常返回路徑都清 pool。方法無回傳值，成功會更新 objective terms/constant/sense 與 model stats state。
- `public void CreateMaximize()` | `OptimFoundation.Core/EngineBase.cs:1685` | 只以 LHS terms、LHS constant 及 soft penalty terms建立 `ObjectiveSense.Maximize` objective，並取代 solver 目前 objective；任何 RHS terms/constant 都 warning 後忽略。沒有 LHS terms且沒有 soft penalty時 skip建立並清 pool；成功更新 objective tracking/state後也清 pool。方法回傳 void，建置例外會記錄後重拋。
- `public virtual bool SupportsSoftConstraints` | `OptimFoundation.Core/EngineBase.cs:1756`
- `public virtual bool CreateLessEqualSoft(double rhs, double penalty)` | `OptimFoundation.Core/EngineBase.cs:1759` | 自動命名後建立 nonnegative continuous slack `Surplus_*`，把它以 -1 加入式子形成 `LHS - slack <= rhs`；目標最小化加入 `+penalty*slack`，最大化加入負 penalty。pool 空回傳 false；成功會重設 objective、清 pool並回傳 true。
- `public virtual bool CreateLessEqualSoft(double rhs, double penalty, string name)` | `OptimFoundation.Core/EngineBase.cs:1763` | 驗證 name，以 `Surplus_{name}` 建非負 continuous slack，建立 `LHS - slack <= rhs`，並依 objective sense 加入 `+penalty` 或 `-penalty` slack term；修改 variable、constraint 與 objective state，完成後清 pool。
- `public virtual bool CreateLessEqualSoft(double rhs, double penalty, ConstraintBase owner, params object[] dims)` | `OptimFoundation.Core/EngineBase.cs:1768` | 由 owner+dims 組 canonical name，建立 surplus slack 允許 LHS 超過 rhs；slack 係數為 -1，違反量以 penalty 加入最小化 objective、從最大化 objective 扣除，成功後清 pool。
- `public virtual bool CreateGreaterEqualSoft(double rhs, double penalty)` | `OptimFoundation.Core/EngineBase.cs:1772` | 自動命名並建立 nonnegative continuous `Deficit_*`，形成 `LHS + slack >= rhs`；不足量以 penalty 懲罰，最小化加正項、最大化加負項。pool 空回傳 false，成功修改 variable/constraint/objective 後清 pool。
- `public virtual bool CreateGreaterEqualSoft(double rhs, double penalty, string name)` | `OptimFoundation.Core/EngineBase.cs:1776` | 驗證 name，以 `Deficit_{name}` 建非負 slack，建立 `LHS + slack >= rhs`，並依 objective direction 加入 penalty slack term；會以 `SetObjective` 重設目前 objective並清 pool。
- `public virtual bool CreateGreaterEqualSoft(double rhs, double penalty, ConstraintBase owner, params object[] dims)` | `OptimFoundation.Core/EngineBase.cs:1781` | 由 owner+dims 命名，建立 deficit slack 允許 LHS 低於 rhs；最小化加 `penalty*slack`、最大化扣除，成功會新增一個 variable、一條 constraint、修改 objective並清 pool。
- `public virtual bool CreateEqualSoft(double rhs, double penalty, string name)` | `OptimFoundation.Core/EngineBase.cs:1785` | 驗證 name，建立兩個 nonnegative continuous slacks `Delta_Neg_{name}`、`Delta_Pos_{name}`，形成 `LHS + negativeSlack - positiveSlack = rhs`；兩個偏差量都按 penalty 加入最小化 objective或從最大化 objective扣除，成功後清 pool。
- `public virtual bool CreateEqualSoft(double rhs, double penalty, ConstraintBase owner, params object[] dims)` | `OptimFoundation.Core/EngineBase.cs:1790` | 由 owner+dims 命名，新增正負兩個偏差 slack 與 equality constraint；兩個 slack 都套用相同 penalty，方向由 objective sense 決定。會新增兩個 variables、修改 objective並在完成後清 pool。

### `OptimFoundation.Core/Experiments/ConfigSnapshot.cs`

- `public sealed class ConfigSnapshot` | `OptimFoundation.Core/Experiments/ConfigSnapshot.cs:13`
- `public string Solver` | `OptimFoundation.Core/Experiments/ConfigSnapshot.cs:16`
- `public Dictionary<string, object> Tunable` | `OptimFoundation.Core/Experiments/ConfigSnapshot.cs:19`
- `public Dictionary<string, object> SolverSpecific` | `OptimFoundation.Core/Experiments/ConfigSnapshot.cs:22`
- `public static ConfigSnapshot From(ISolverConfig config)` | `OptimFoundation.Core/Experiments/ConfigSnapshot.cs:27` | 反射 ISolverConfig 建立 ConfigSnapshot，分成 Tunable 與 SolverSpecific；不修改原 config。

### `OptimFoundation.Core/Experiments/ExpCsvWriter.cs`

- `public sealed class CsvExperimentWriter` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:20`
- `public const string Off = "off";` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:25`
- `public const string None = "none";` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:28`
- `public const string NotAvailable = "n/a";` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:31`
- `public const string Baseline = "baseline";` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:34`
- `public const string Win = "win";` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:41`
- `public const string Lose = "lose";` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:44`
- `public const string Tie = "tie";` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:47`
- `public void Write(Experiment experiment, string path, DateTime recordedAt)` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:74` | 讀取 `experiment.Trials`、各 trial 的 config/metrics 與同 run baseline comparison，把一列一 trial 接在 `path`（`{專案}-trial.csv`）檔尾；每列前三欄 RecordedAt / Experiment / RunId；不建立 Experiment result 或執行求解。
- `public sealed class MetaCsvWriter` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:269`
- `public const int SchemaVersion = 10;` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:265`
- `public void Write(Experiment experiment, string path, DateTime recordedAt)` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:298` | 每批（RunId）把 schema、legend、描述、批次資訊、model statistics、環境與 baseline config 寫成一份 Section/Key/Value，接在 `path` 檔尾；只序列化既有資料，不修改 trials。
- `public sealed class SummaryCsvWriter` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:400`
- `public void Write(Experiment experiment, string path, DateTime recordedAt)` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:429` | 讀取每次存取時由 `experiment.Trials` 計算的 `Summaries`，把每個 run/model/config 一列的 status、incumbent 與 W/L/T 接在 `path` 檔尾；不回寫 summary 或 trial state。
- `public sealed class TrajectoryCsvWriter` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:462`
- `public void Write(Experiment experiment, string path, DateTime recordedAt)` | `OptimFoundation.Core/Experiments/ExpCsvWriter.cs:493` | 逐一讀取 `experiment.Trials[*].Metrics.Convergence`，跳過沒有軌跡的 trial，把一列一 sampling point（含 TrialId）接在 `path` 檔尾；整批沒有點就不動檔案；NaN/Infinity 寫成 `#N/A`，不補造取樣點。

### `OptimFoundation.Core/Experiments/Experiment.cs`

- `public class Experiment` | `OptimFoundation.Core/Experiments/Experiment.cs:14`
- `public string Project` | `OptimFoundation.Core/Experiments/Experiment.cs:18`
- `public string Name` | `OptimFoundation.Core/Experiments/Experiment.cs:20`
- `public string Description` | `OptimFoundation.Core/Experiments/Experiment.cs:22`
- `public bool WriteSummary` | `OptimFoundation.Core/Experiments/Experiment.cs:29`
- `public DateTime CreatedAt` | `OptimFoundation.Core/Experiments/Experiment.cs:21`
- `public List<Trial> Trials` | `OptimFoundation.Core/Experiments/Experiment.cs:23`
- `public IReadOnlyList<ConfigSummary> Summaries` | `OptimFoundation.Core/Experiments/Experiment.cs:29`
- `public Experiment(string project, string name, string description)` | `OptimFoundation.Core/Experiments/Experiment.cs:41` | 驗證 project（不可空白、不可含非法檔名字元）與 name、設定 CreatedAt 並初始化 Trials；建立容器時不求解也不寫檔。
- `public void AddTrial(Trial trial)` | `OptimFoundation.Core/Experiments/Experiment.cs:45` | 驗證 `trial` 非 null 後，只把既有 `Trial` reference 加入 `Trials`；不呼叫 `Run`、不求解、不複製 trial，也不寫檔。
- `public void Save()` | `OptimFoundation.Core/Experiments/Experiment.cs:77` | 建立 Experiment 目錄，把 trials 接在 `{Project}-trial.csv` / `-meta.csv` / `-summary.csv`（`WriteSummary` 時）/ `-trajectory.csv`（有點才寫）檔尾；表頭不同舊檔改名 `-old-<時間>`、寫不進去改寫 `-locked-<時間>`，皆留 WARN；沒有 trial 只留 WARN；失敗記 log 後重拋。
- `public interface ITrajectorySource` | `OptimFoundation.Core/Experiments/Experiment.cs:103`
- `public sealed class Trial` | `OptimFoundation.Core/Experiments/Experiment.cs:118`
- `public string Label` | `OptimFoundation.Core/Experiments/Experiment.cs:121`
- `public string ExperimentId` | `OptimFoundation.Core/Experiments/Experiment.cs:125`
- `public int TrialId` | `OptimFoundation.Core/Experiments/Experiment.cs:128`
- `public string Model` | `OptimFoundation.Core/Experiments/Experiment.cs:131`
- `public DateTime RunTime` | `OptimFoundation.Core/Experiments/Experiment.cs:134`
- `public ConfigSnapshot Config` | `OptimFoundation.Core/Experiments/Experiment.cs:137`
- `public SolveMetrics Metrics` | `OptimFoundation.Core/Experiments/Experiment.cs:140`
- `public static Trial Capture(ISolverEngine engine, string label, Func<bool> solveAction, bool captureTrajectory = true)` | `OptimFoundation.Core/Experiments/Experiment.cs:182` | 要求非 null engine 與 action；先從 `engine.Config` 建 `ConfigSnapshot`，若要求 trajectory 且 engine 實作並支援 `ITrajectorySource`，在求解前呼叫 `EnableTrajectory()`。接著實際呼叫一次 `solveAction()`；其 bool 回傳不決定是否建 Trial。action 正常返回後讀 `engine.LastMetrics`，若為 null 則以當下 `engine.Status` 建最小 metrics，再連同 label、目前時間、snapshot 組成新 Trial；trajectory 由 solve 後的 `LastMetrics.Convergence` 一併保存。action 或 capture 失敗時記 `TRIAL_CAPTURE_FAILED` 並原例外重拋，不回傳 Trial；此方法不呼叫 `Dispose()`，engine lifecycle 仍由呼叫端管理。
- `public sealed class ConfigSummary` | `OptimFoundation.Core/Experiments/Experiment.cs:214`
- `public string RunId` | `OptimFoundation.Core/Experiments/Experiment.cs:217`
- `public string Model` | `OptimFoundation.Core/Experiments/Experiment.cs:220`
- `public string Config` | `OptimFoundation.Core/Experiments/Experiment.cs:223`
- `public bool IsBaseline` | `OptimFoundation.Core/Experiments/Experiment.cs:226`
- `public int Trials` | `OptimFoundation.Core/Experiments/Experiment.cs:229`
- `public string Seeds` | `OptimFoundation.Core/Experiments/Experiment.cs:232`
- `public int Optimal` | `OptimFoundation.Core/Experiments/Experiment.cs:235`
- `public int Feasible` | `OptimFoundation.Core/Experiments/Experiment.cs:238`
- `public int NoSolution` | `OptimFoundation.Core/Experiments/Experiment.cs:264`
- `public int Failed` | `OptimFoundation.Core/Experiments/Experiment.cs:267`
- `public int FoundSolution` | `OptimFoundation.Core/Experiments/Experiment.cs:270`
- `public int? Wins` | `OptimFoundation.Core/Experiments/Experiment.cs:250`
- `public int? Losses` | `OptimFoundation.Core/Experiments/Experiment.cs:253`
- `public int? Ties` | `OptimFoundation.Core/Experiments/Experiment.cs:256`
- `public int? NotCompared` | `OptimFoundation.Core/Experiments/Experiment.cs:259`

### `OptimFoundation.Core/Experiments/SolveMetrics.cs`

- `public sealed class SolveMetrics` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:9`
- `public SolveStatus Status` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:12`
- `public double ObjectiveValue` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:15`
- `public double BestBound` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:18`
- `public double Gap` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:21` | 求解結束時實際達到的相對 gap（不是 `CplexConfig.MipGap` 那個停止門檻）；無解時為 NaN。
- `public double SolveTimeMs` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:24` | 純求解耗時，只計 `Solve()`、不含建模；用 CPLEX 時鐘。
- `public double? BuildAndSolveTimeMs` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:30` | 建模 + 求解 = 把 OptModel 套進 CPLEX 的時間（讀模型檔時含讀檔與建立查找索引）+ `SolveTimeMs`，兩段都用 CPLEX 時鐘；不含 beforeSolve、匯出模型 / 解檔、IIS 分析；只有經由 `OptProject.Solve` / `OptExperiment` 才有值，自己呼叫 `Trial.Capture` 時是 null（CSV 寫 `n/a`）。
- `public long? NodeCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:27`
- `public long? IterationCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:30`
- `public int? Seed` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:33`
- `public bool TrajectoryEnabled` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:36`
- `public ModelType? ModelType` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:41`
- `public int VarCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:44`
- `public int? BinaryVarCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:47`
- `public int? IntegerVarCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:50`
- `public int? ContinuousVarCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:53`
- `public int? SemiContinuousVarCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:56`
- `public int? SemiIntegerVarCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:59`
- `public int ConstraintCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:62`
- `public int? QuadraticConstraintCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:65`
- `public int? IndicatorConstraintCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:68`
- `public int? SosCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:71`
- `public int? LazyConstraintCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:74`
- `public int? UserCutCount` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:77`
- `public List<ConvergencePoint> Convergence` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:83`
- `public int TrajectoryPoints` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:89`
- `public double? FirstSolutionMs` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:101` | 第一次找到可行解的時間。
- `public double? BoundChange` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:114` | 界從頭到尾總共變了多少。
- `public double? LastBoundChangeMs` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:125` | 界最後一次變動的時間。
- `public sealed class ConvergencePoint` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:140`
- `public double ElapsedMs` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:143`
- `public double ObjectiveValue` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:146`
- `public double BestBound` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:149`
- `public double Gap` | `OptimFoundation.Core/Experiments/SolveMetrics.cs:152`

### `OptimFoundation.Core/Infrastructure/ClassInfo.cs`

- `public static class ReflectionHelper` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:11`
- `public static string[] GetMemberNames(Type type)` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:44` | 反射 type 並回傳可映射成員名稱，順序供 CSV/DB schema 使用；不修改 type。
- `public static Type[] GetMemberTypes(Type type)` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:53` | 反射 type 並回傳與 member names 同順序的型別陣列；不做 IO。
- `public static string GenerateSQLCols(Type type)` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:67` | 把 type 成員轉成 SQL 欄位定義 fragment 並回傳；不執行 SQL。
- `public class ClassInfo` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:95`
- `public Type Type` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:98`
- `public string TypeName` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:101`
- `public string[] SetNames` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:104`
- `public Type[] PropertyTypes` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:107`
- `public string ColNames` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:110`
- `public string ParamPlaceholders` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:113`
- `public string SQLColsDefinition` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:116`
- `public ClassInfo(Type type)` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:119` | 快取指定 type 的成員與 SQL metadata，供 DB schema/command 產生器使用。
- `public string VarInsertCmd(string tableName)` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:122` | 依快取 schema 與 tableName 回傳 parameterized INSERT SQL；不連線、不執行命令。
- `public string ParamInsertCmd(string tableName)` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:126` | 依快取 schema 與 tableName 回傳 parameterized INSERT SQL；不連線、不執行命令。
- `public string VarTableCreateCmd(string tableName)` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:130` | 依快取 schema 與 tableName 回傳 CREATE TABLE SQL；不直接建立資料表。
- `public string ParamTableCreateCmd(string tableName)` | `OptimFoundation.Core/Infrastructure/ClassInfo.cs:134` | 依快取 schema 與 tableName 回傳 CREATE TABLE SQL；不直接建立資料表。

### `OptimFoundation.Core/Infrastructure/FolderDir.cs`

- `public class FolderDir` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:11`
- `public static ProjFolder Input = new ProjFolder("Input");` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:14` | 保存相對 AppDomain BaseDirectory 的 folderName；建構時不建立目錄。
- `public static ProjFolder Output = new ProjFolder("Output");` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:17` | 保存相對 AppDomain BaseDirectory 的 folderName；建構時不建立目錄。
- `public static ProjFolder Log = new ProjFolder("Log");` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:20` | 保存相對 AppDomain BaseDirectory 的 folderName；建構時不建立目錄。
- `public static ProjFolder Model = new ProjFolder("Model");` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:23` | 保存相對 AppDomain BaseDirectory 的 folderName；建構時不建立目錄。
- `public static ProjFolder IIS = new ProjFolder("IIS");` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:26` | 保存相對 AppDomain BaseDirectory 的 folderName；建構時不建立目錄。
- `public static ProjFolder Solution = new ProjFolder("Solution");` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:29` | 保存相對 AppDomain BaseDirectory 的 folderName；建構時不建立目錄。
- `public static ProjFolder Experiment = new ProjFolder("Experiment");` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:32` | 保存相對 AppDomain BaseDirectory 的 folderName；建構時不建立目錄。
- `public static void CreateAll()` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:44` | 建立七個 framework 目錄；已存在時保留原內容。
- `public static int PurgeAllOutputs(int retentionDays)` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:50` | retentionDays > 0 時清 Log/Model/Solution/IIS/Output 逾期檔並回傳刪除總數；不清 Input/Experiment，非正值回傳 0。
- `public class ProjFolder` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:59`
- `public static string ProjectPath` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:62`
- `public ProjFolder(string folderName)` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:67` | 保存相對 AppDomain BaseDirectory 的 folderName；建構時不建立目錄。
- `public string GetPath()` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:73` | 回傳 ProjectPath 與 folderName 組成的完整目錄路徑；不檢查存在。
- `public string GetPathFile(string fileName)` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:76` | 回傳目錄與 fileName 合成路徑；不建立目錄或檔案。
- `public void CreateFolder()` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:81` | 以 Directory.CreateDirectory 建目錄；已存在時不清內容。
- `public bool TryCreateFile(string fileName)` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:88` | 確保目錄後建立空檔；已存在回傳 false，新建成功回傳 true。
- `public int PurgeOlderThan(int retentionDays)` | `OptimFoundation.Core/Infrastructure/FolderDir.cs:98` | 刪除本目錄逾期檔並回傳數量；目錄不存在回 0，IO/權限錯誤會警告並跳過。

### `OptimFoundation.Core/Infrastructure/Logging.cs`

- `public static class Logging` | `OptimFoundation.Core/Infrastructure/Logging.cs:13`
- `public static void Info(string message)` | `OptimFoundation.Core/Infrastructure/Logging.cs:58` | 以 Info level 寫 framework log；訊息可能輸出至 console 與已設定 log file。
- `public static void Debug(string message)` | `OptimFoundation.Core/Infrastructure/Logging.cs:61` | 以 Debug level 寫 framework log；訊息可能輸出至 console 與已設定 log file。
- `public static void Warn(string message)` | `OptimFoundation.Core/Infrastructure/Logging.cs:64` | 以 Warn level 寫 framework log；訊息可能輸出至 console 與已設定 log file。
- `public static void Error(string message)` | `OptimFoundation.Core/Infrastructure/Logging.cs:67` | 以 Error level 寫 framework log；訊息可能輸出至 console 與已設定 log file。
- `public static TException ErrorOnce<TException>(TException exception, string eventCode, string description, string context, object value, string reason, string details = null) where TException : Exception` | `OptimFoundation.Core/Infrastructure/Logging.cs:73` | 同一 Exception instance 僅記一次結構化錯誤，在 exception.Data 標記後回傳原例外供原樣重拋。
- `public static void Info(string message, Stopwatch sw)` | `OptimFoundation.Core/Infrastructure/Logging.cs:125` | 以 Info level 寫 framework log；訊息可能輸出至 console 與已設定 log file。
- `public static void SetLogFileName(string name)` | `OptimFoundation.Core/Infrastructure/Logging.cs:138` | 設定後續 file logging 的檔名；應在首次寫檔前呼叫。
- `public static void WriteToFile(string message)` | `OptimFoundation.Core/Infrastructure/Logging.cs:156` | 把訊息附加到目前 log file，會建立並寫入 Log 目錄。
- `public static void ClearLogs()` | `OptimFoundation.Core/Infrastructure/Logging.cs:166` | 清除目前 log 輸出與狀態；新 run 或測試初始化時使用，具檔案副作用。

### `OptimFoundation.Core/Infrastructure/ProjectConfig.cs`

- `public sealed class ProjectConfig` | `OptimFoundation.Core/Infrastructure/ProjectConfig.cs:10`
- `public ProjectConfig Clone()` | `OptimFoundation.Core/Infrastructure/ProjectConfig.cs:13` | 回傳目前 config 的獨立副本，供 project/trial 修改且不改原 instance。
- `public static ProjectConfig Quiet()` | `OptimFoundation.Core/Infrastructure/ProjectConfig.cs:16` | 回傳降低一般輸出的 ProjectConfig preset，供測試或批次 run 使用。
- `public bool EnableSolverLog` | `OptimFoundation.Core/Infrastructure/ProjectConfig.cs:19`
- `public bool ExportLP` | `OptimFoundation.Core/Infrastructure/ProjectConfig.cs:22`
- `public bool ExportMPS` | `OptimFoundation.Core/Infrastructure/ProjectConfig.cs:25`
- `public bool ExportSol` | `OptimFoundation.Core/Infrastructure/ProjectConfig.cs:28`
- `public bool ExportIIS` | `OptimFoundation.Core/Infrastructure/ProjectConfig.cs:31`

### `OptimFoundation.Core/IO/csv/CsvCtrl.cs`

- `public static class CsvCtrl` | `OptimFoundation.Core/IO/csv/CsvCtrl.cs:13`
- `public static IEnumerable<string[]> ParseCsv(TextReader reader)` | `OptimFoundation.Core/IO/csv/CsvCtrl.cs:25` | 逐列解析 TextReader 的 CSV quoting 並 yield string[]；reader lifetime 由呼叫端管理。
- `public static void WriteSolution<TVariable>(ISolverEngine engine, string dataId, string userId)` | `OptimFoundation.Core/IO/csv/CsvCtrl.cs:120` | 從已有 solution 的 engine 讀 variable rows 並寫入 CSV/DB sink；會產生外部寫入。
- `public static void WriteRows<T>(IReadOnlyList<T> rows, string fileName = null) where T : ModelElementBase` | `OptimFoundation.Core/IO/csv/CsvCtrl.cs:163` | 把 ModelElementBase rows 序列化到 CSV；會建立或覆寫 Output 檔案。

### `OptimFoundation.Core/IO/csv/CsvDataSource.cs`

- `public sealed class CsvDataSource : IDataSource` | `OptimFoundation.Core/IO/csv/CsvDataSource.cs:13`
- `public CsvDataSource()` | `OptimFoundation.Core/IO/csv/CsvDataSource.cs:20` | 建立無狀態 CSV source；LoadData 時才開檔。
- `public DataTable LoadData(string fileName)` | `OptimFoundation.Core/IO/csv/CsvDataSource.cs:36` | 讀取指定 CSV、DB query 或 memory dataset 並回傳 DataTable；來源必須存在且 schema 可解析。
- `public sealed class CsvSolutionSink : ISolutionSink` | `OptimFoundation.Core/IO/csv/CsvDataSource.cs:63`
- `public void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null)` | `OptimFoundation.Core/IO/csv/CsvDataSource.cs:66` | 從已有 solution 的 engine 讀 variable rows 並寫入 CSV/DB sink；會產生外部寫入。
- `public ISolutionBatch BeginBatch(string dataId = null, string userId = null)` | `OptimFoundation.Core/IO/csv/CsvDataSource.cs:73` | 建立暫存多組 solution 的 batch；直到 Commit 才完成寫入。

### `OptimFoundation.Core/IO/db/DbCtrlBase.cs`

- `public abstract class DbCtrlBase : IDbCtrl` | `OptimFoundation.Core/IO/db/DbCtrlBase.cs:12`
- `public abstract void Open();` | `OptimFoundation.Core/IO/db/DbCtrlBase.cs:32` | 用途：控制資料庫或 batch lifecycle；需要有效連線，可能 commit、rollback 或寫入外部資料。
- `public abstract void Close();` | `OptimFoundation.Core/IO/db/DbCtrlBase.cs:35` | 用途：控制資料庫或 batch lifecycle；需要有效連線，可能 commit、rollback 或寫入外部資料。
- `public abstract DataTable Query(string sql, params (string name, object value)[] parameters);` | `OptimFoundation.Core/IO/db/DbCtrlBase.cs:38` | 執行參數化查詢並回傳 DataTable；需要有效 DB connection。
- `public abstract int Execute(string sql, params (string name, object value)[] parameters);` | `OptimFoundation.Core/IO/db/DbCtrlBase.cs:41` | 用途：控制資料庫或 batch lifecycle；需要有效連線，可能 commit、rollback 或寫入外部資料。
- `public abstract TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters);` | `OptimFoundation.Core/IO/db/DbCtrlBase.cs:44` | 執行參數化 scalar query 並轉成 TResult；需要有效 DB connection。
- `public abstract void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows);` | `OptimFoundation.Core/IO/db/DbCtrlBase.cs:47` | 用途：控制資料庫或 batch lifecycle；需要有效連線，可能 commit、rollback 或寫入外部資料。
- `public void ExecuteInTransaction(Action<IDbCtrl> work)` | `OptimFoundation.Core/IO/db/DbCtrlBase.cs:58` | 用途：控制資料庫或 batch lifecycle；需要有效連線，可能 commit、rollback 或寫入外部資料。
- `public virtual void NonQuery(string sql, params (string name, object value)[] parameters)` | `OptimFoundation.Core/IO/db/DbCtrlBase.cs:152` | 委派 Execute 執行無結果 SQL；需要 open connection，可能修改 DB。
- `public virtual void Dispose()` | `OptimFoundation.Core/IO/db/DbCtrlBase.cs:156` | 用途：釋放 native/IO resources；scope 結束時呼叫，之後不可再使用 instance。

### `OptimFoundation.Core/IO/db/DbDataSource.cs`

- `public sealed class DbDataSource : IDataSource` | `OptimFoundation.Core/IO/db/DbDataSource.cs:14`
- `public DbDataSource(IDbCtrl db)` | `OptimFoundation.Core/IO/db/DbDataSource.cs:19` | 保存非 null IDbCtrl 供後續查詢與 row mapping；建構時不開連線。
- `public List<T> Load<T>(string sql, params (string name, object value)[] parameters) where T : ModelElementBase, new()` | `OptimFoundation.Core/IO/db/DbDataSource.cs:28` | 從目前 source/factory 載入並回傳 typed data；呼叫前來源需可用，驗證或映射失敗會拋例外或留下 DataIssues。
- `public DataTable LoadData(string sql)` | `OptimFoundation.Core/IO/db/DbDataSource.cs:39` | 讀取指定 CSV、DB query 或 memory dataset 並回傳 DataTable；來源必須存在且 schema 可解析。
- `public DataTable LoadData(string sql, params (string name, object value)[] parameters)` | `OptimFoundation.Core/IO/db/DbDataSource.cs:43` | 讀取指定 CSV、DB query 或 memory dataset 並回傳 DataTable；來源必須存在且 schema 可解析。

### `OptimFoundation.Core/IO/db/IDbCtrl.cs`

- `public interface IDbCtrl : IDisposable` | `OptimFoundation.Core/IO/db/IDbCtrl.cs:13`

### `OptimFoundation.Core/IO/db/OracleDbCtrl.cs`

- `public sealed class OracleDbCtrl : DbCtrlBase` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:16`
- `public OracleDbCtrl(string connectionString) : base(connectionString)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:19` | 以 connection string 建 Oracle controller；Open 前不建立連線。
- `public override void Open()` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:26` | 用途：控制資料庫或 batch lifecycle；需要有效連線，可能 commit、rollback 或寫入外部資料。
- `public override void Close()` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:29` | 用途：控制資料庫或 batch lifecycle；需要有效連線，可能 commit、rollback 或寫入外部資料。
- `public override DataTable Query(string sql, params (string name, object value)[] parameters)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:32` | 執行參數化查詢並回傳 DataTable；需要有效 DB connection。
- `public override int Execute(string sql, params (string name, object value)[] parameters)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:53` | 用途：控制資料庫或 batch lifecycle；需要有效連線，可能 commit、rollback 或寫入外部資料。
- `public override TResult QueryScalar<TResult>(string sql, params (string name, object value)[] parameters)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:73` | 執行參數化 scalar query 並轉成 TResult；需要有效 DB connection。
- `public static string BuildConnectionString( string host, string port, string sid = "", string serviceName = "", string userId = "", string password = "")` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:165` | 由 host/port 與 SID 或 service name 組 Oracle connection string；只回傳字串。
- `public bool CheckHasTable(string tableName)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:183` | 查 Oracle metadata 並回傳 tableName 是否存在；需要有效連線。
- `public void CreateParamTable<TParameter>(string tableName)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:194` | 依 TParameter reflection schema 執行 CREATE TABLE；會修改 DB schema。
- `public void CreateResultTable<TVariable>(string tableName)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:210` | 依 TVariable reflection schema 執行 CREATE TABLE；會修改 DB schema。
- `public void DropTable(string tableName)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:226` | 執行 DROP TABLE 並移除資料與 schema；屬 destructive DB 操作。
- `public void DeleteTable(string tableName, params string[] conditions)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:244` | 依 conditions 刪除 rows；會修改 DB，呼叫端需確認條件不為空或過寬。
- `public void TruncateTable(string tableName)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:258` | 執行 TRUNCATE TABLE 清空資料；不回傳 rows。
- `public void SaveToDB<TVariable>(ISolverEngine engine, string dataId, string tableName, string userId)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:274` | 從已有 solution 的 engine 讀 TVariable 並寫入指定 Oracle table；需要相容 schema。
- `public override void ExecuteBatch(string sql, IReadOnlyList<(string name, object value)[]> rows)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:325` | 用途：控制資料庫或 batch lifecycle；需要有效連線，可能 commit、rollback 或寫入外部資料。
- `public sealed class OracleSolutionSink : ISolutionSink` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:413`
- `public OracleSolutionSink(IDbCtrl db, string tableName)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:420` | 綁定 IDbCtrl 與 tableName；建構時不寫 DB，Write/Commit 才寫入。
- `public void WriteSolution<TVariableClass>(ISolverEngine engine, string dataId = null, string userId = null)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:434` | 從已有 solution 的 engine 讀 variable rows 並寫入 CSV/DB sink；會產生外部寫入。
- `public ISolutionBatch BeginBatch(string dataId = null, string userId = null)` | `OptimFoundation.Core/IO/db/OracleDbCtrl.cs:449` | 建立暫存多組 solution 的 batch；直到 Commit 才完成寫入。

### `OptimFoundation.Core/IO/IDataSource.cs`

- `public interface IDataSource` | `OptimFoundation.Core/IO/IDataSource.cs:11`
- `public interface ISolutionSink` | `OptimFoundation.Core/IO/IDataSource.cs:40`
- `public interface ISolutionBatch : IDisposable` | `OptimFoundation.Core/IO/IDataSource.cs:50`

### `OptimFoundation.Core/IO/InMemoryDataSource.cs`

- `public sealed class InMemoryDataSource : IDataSource` | `OptimFoundation.Core/IO/InMemoryDataSource.cs:12`
- `public InMemoryDataSource AddRows<T>(IEnumerable<T> rows) where T : ModelElementBase, new()` | `OptimFoundation.Core/IO/InMemoryDataSource.cs:17` | 把 typed 或 named rows 登錄到 in-memory source 並回傳 this；供測試或程式內資料載入。
- `public InMemoryDataSource AddRows(string name, IEnumerable<string[]> rows)` | `OptimFoundation.Core/IO/InMemoryDataSource.cs:39` | 把 typed 或 named rows 登錄到 in-memory source 並回傳 this；供測試或程式內資料載入。
- `public DataTable LoadData(string name)` | `OptimFoundation.Core/IO/InMemoryDataSource.cs:69` | 讀取指定 CSV、DB query 或 memory dataset 並回傳 DataTable；來源必須存在且 schema 可解析。

### `OptimFoundation.Core/VariableManager.cs`

- `public static class VariableManager` | `OptimFoundation.Core/VariableManager.cs:14`
- `public static List<string>[] ConvertSetsToTokens(params object[] sets)` | `OptimFoundation.Core/VariableManager.cs:142` | 把 DateTime/int/long/double/decimal/string/enum enumerable 轉成 invariant tokens；null、裸 string 或不支援型別會拋例外。
- `public static IEnumerable<string> ComposeNames<TVariable>(object[] sets)` | `OptimFoundation.Core/VariableManager.cs:204` | 將 sets 轉成 canonical names；typed overload 驗證 arity，string overload 以 typeName 命名且不驗證 variable properties。
- `public static IEnumerable<string> ComposeNames(string typeName, object[] sets)` | `OptimFoundation.Core/VariableManager.cs:218` | 將 sets 轉成 canonical names；typed overload 驗證 arity，string overload 以 typeName 命名且不驗證 variable properties。

### `OptimFoundation.Cplex/CplexConfig.cs`

- `public sealed class CplexConfig : ISolverConfig` | `OptimFoundation.Cplex/CplexConfig.cs:47`
- `public CplexConfig Clone()` | `OptimFoundation.Cplex/CplexConfig.cs:50` | 回傳目前 config 的獨立副本，供 project/trial 修改且不改原 instance。
- `public double? AbsoluteMipGap` | `OptimFoundation.Cplex/CplexConfig.cs:62`
- `public double? BarrierConvergeTol` | `OptimFoundation.Cplex/CplexConfig.cs:69`
- `public long? BarrierIterationLimit` | `OptimFoundation.Cplex/CplexConfig.cs:76`
- `public double? BarrierQcpConvergeTol` | `OptimFoundation.Cplex/CplexConfig.cs:83`
- `public double? DeterministicTimeLimit` | `OptimFoundation.Cplex/CplexConfig.cs:89`
- `public double? FeasOptRelaxTolerance` | `OptimFoundation.Cplex/CplexConfig.cs:96`
- `public double? FeasibilityTol` | `OptimFoundation.Cplex/CplexConfig.cs:102`
- `public long? IntegerSolutionLimit` | `OptimFoundation.Cplex/CplexConfig.cs:110`
- `public double? IntegralityTolerance` | `OptimFoundation.Cplex/CplexConfig.cs:116`
- `public double? LinearizationTolerance` | `OptimFoundation.Cplex/CplexConfig.cs:122`
- `public double? LowerCutoff` | `OptimFoundation.Cplex/CplexConfig.cs:128`
- `public double? LowerObjectiveStop` | `OptimFoundation.Cplex/CplexConfig.cs:134`
- `public double? MipGap` | `OptimFoundation.Cplex/CplexConfig.cs:141`
- `public double? NetworkFeasibilityTol` | `OptimFoundation.Cplex/CplexConfig.cs:148`
- `public long? NetworkIterationLimit` | `OptimFoundation.Cplex/CplexConfig.cs:154`
- `public double? NetworkOptimalityTol` | `OptimFoundation.Cplex/CplexConfig.cs:161`
- `public long? NodeLimit` | `OptimFoundation.Cplex/CplexConfig.cs:168`
- `public double? ObjectiveDifference` | `OptimFoundation.Cplex/CplexConfig.cs:175`
- `public double? OptimalityTol` | `OptimFoundation.Cplex/CplexConfig.cs:182`
- `public double? RelativeObjectiveDifference` | `OptimFoundation.Cplex/CplexConfig.cs:189`
- `public long? SiftingIterationLimit` | `OptimFoundation.Cplex/CplexConfig.cs:196`
- `public long? SimplexIterationLimit` | `OptimFoundation.Cplex/CplexConfig.cs:203`
- `public double? SimplexLowerObjectiveLimit` | `OptimFoundation.Cplex/CplexConfig.cs:210`
- `public double? SimplexUpperObjectiveLimit` | `OptimFoundation.Cplex/CplexConfig.cs:217`
- `public double? TimeLimit` | `OptimFoundation.Cplex/CplexConfig.cs:224`
- `public double? UpperCutoff` | `OptimFoundation.Cplex/CplexConfig.cs:230`
- `public double? UpperObjectiveStop` | `OptimFoundation.Cplex/CplexConfig.cs:236`
- `public int? AuxiliaryRootThreads` | `OptimFoundation.Cplex/CplexConfig.cs:250`
- `public string CpuMask` | `OptimFoundation.Cplex/CplexConfig.cs:258`
- `public bool? MemoryEmphasis` | `OptimFoundation.Cplex/CplexConfig.cs:265`
- `public double? MemoryLimitMb` | `OptimFoundation.Cplex/CplexConfig.cs:271`
- `public int? NodeFileStrategy` | `OptimFoundation.Cplex/CplexConfig.cs:277`
- `public int? ParallelMode` | `OptimFoundation.Cplex/CplexConfig.cs:284`
- `public int? Threads` | `OptimFoundation.Cplex/CplexConfig.cs:291`
- `public double? TreeMemoryLimitMb` | `OptimFoundation.Cplex/CplexConfig.cs:298`
- `public string WorkDir` | `OptimFoundation.Cplex/CplexConfig.cs:305`
- `public int? ClockType` | `OptimFoundation.Cplex/CplexConfig.cs:318`
- `public int? Seed` | `OptimFoundation.Cplex/CplexConfig.cs:326`
- `public int? AdvancedStart` | `OptimFoundation.Cplex/CplexConfig.cs:340`
- `public double? BacktrackTolerance` | `OptimFoundation.Cplex/CplexConfig.cs:347`
- `public long? BestBoundInterval` | `OptimFoundation.Cplex/CplexConfig.cs:354`
- `public int? BranchDirection` | `OptimFoundation.Cplex/CplexConfig.cs:360`
- `public int? DiveType` | `OptimFoundation.Cplex/CplexConfig.cs:367`
- `public int? Emphasis` | `OptimFoundation.Cplex/CplexConfig.cs:374`
- `public int? KappaStatistics` | `OptimFoundation.Cplex/CplexConfig.cs:381`
- `public int? MipSearch` | `OptimFoundation.Cplex/CplexConfig.cs:388`
- `public int? NodeSelect` | `OptimFoundation.Cplex/CplexConfig.cs:395`
- `public bool? NumericalEmphasis` | `OptimFoundation.Cplex/CplexConfig.cs:402`
- `public int? OptimalityTarget` | `OptimFoundation.Cplex/CplexConfig.cs:410`
- `public int? PriorityOrderType` | `OptimFoundation.Cplex/CplexConfig.cs:417`
- `public int? Probe` | `OptimFoundation.Cplex/CplexConfig.cs:424`
- `public int? SolutionType` | `OptimFoundation.Cplex/CplexConfig.cs:431`
- `public int? StrongBranchingCandidateLimit` | `OptimFoundation.Cplex/CplexConfig.cs:439`
- `public long? StrongBranchingIterationLimit` | `OptimFoundation.Cplex/CplexConfig.cs:446`
- `public bool? UsePriorityOrder` | `OptimFoundation.Cplex/CplexConfig.cs:453`
- `public int? VariableSelect` | `OptimFoundation.Cplex/CplexConfig.cs:460`
- `public int? CardinalityLocalSearch` | `OptimFoundation.Cplex/CplexConfig.cs:474`
- `public int? FeasibilityPumpHeuristic` | `OptimFoundation.Cplex/CplexConfig.cs:481`
- `public double? HeuristicEffort` | `OptimFoundation.Cplex/CplexConfig.cs:488`
- `public long? HeuristicFrequency` | `OptimFoundation.Cplex/CplexConfig.cs:495`
- `public bool? LocalBranchingHeuristic` | `OptimFoundation.Cplex/CplexConfig.cs:502`
- `public double? PolishAfterAbsoluteMipGap` | `OptimFoundation.Cplex/CplexConfig.cs:509`
- `public double? PolishAfterDetTime` | `OptimFoundation.Cplex/CplexConfig.cs:516`
- `public double? PolishAfterMipGap` | `OptimFoundation.Cplex/CplexConfig.cs:523`
- `public long? PolishAfterNodes` | `OptimFoundation.Cplex/CplexConfig.cs:530`
- `public long? PolishAfterSolutions` | `OptimFoundation.Cplex/CplexConfig.cs:538`
- `public double? PolishAfterTime` | `OptimFoundation.Cplex/CplexConfig.cs:545`
- `public long? RepairTries` | `OptimFoundation.Cplex/CplexConfig.cs:552`
- `public long? RinsHeuristicFrequency` | `OptimFoundation.Cplex/CplexConfig.cs:559`
- `public int? AggregationLimitForCut` | `OptimFoundation.Cplex/CplexConfig.cs:573`
- `public int? BqpCuts` | `OptimFoundation.Cplex/CplexConfig.cs:579`
- `public int? CliqueCuts` | `OptimFoundation.Cplex/CplexConfig.cs:585`
- `public int? CoverCuts` | `OptimFoundation.Cplex/CplexConfig.cs:592`
- `public long? CutPasses` | `OptimFoundation.Cplex/CplexConfig.cs:599`
- `public double? CutsFactor` | `OptimFoundation.Cplex/CplexConfig.cs:605`
- `public int? DisjunctiveCuts` | `OptimFoundation.Cplex/CplexConfig.cs:612`
- `public int? EachCutLimit` | `OptimFoundation.Cplex/CplexConfig.cs:619`
- `public int? FlowCoverCuts` | `OptimFoundation.Cplex/CplexConfig.cs:626`
- `public int? FlowPathCuts` | `OptimFoundation.Cplex/CplexConfig.cs:633`
- `public int? GomoryCandidateLimit` | `OptimFoundation.Cplex/CplexConfig.cs:641`
- `public int? GomoryCuts` | `OptimFoundation.Cplex/CplexConfig.cs:648`
- `public long? GomoryPassLimit` | `OptimFoundation.Cplex/CplexConfig.cs:655`
- `public int? GubCoverCuts` | `OptimFoundation.Cplex/CplexConfig.cs:662`
- `public int? ImpliedBoundCuts` | `OptimFoundation.Cplex/CplexConfig.cs:669`
- `public int? LiftAndProjectCuts` | `OptimFoundation.Cplex/CplexConfig.cs:676`
- `public int? LocalImpliedBoundCuts` | `OptimFoundation.Cplex/CplexConfig.cs:683`
- `public int? McfCuts` | `OptimFoundation.Cplex/CplexConfig.cs:690`
- `public int? MirCuts` | `OptimFoundation.Cplex/CplexConfig.cs:696`
- `public int? NodeCuts` | `OptimFoundation.Cplex/CplexConfig.cs:703`
- `public int? RltCuts` | `OptimFoundation.Cplex/CplexConfig.cs:709`
- `public int? ZeroHalfCuts` | `OptimFoundation.Cplex/CplexConfig.cs:716`
- `public long? AggregatorFill` | `OptimFoundation.Cplex/CplexConfig.cs:729`
- `public int? AggregatorLimit` | `OptimFoundation.Cplex/CplexConfig.cs:736`
- `public int? BoundStrengthening` | `OptimFoundation.Cplex/CplexConfig.cs:742`
- `public int? CoefficientReduction` | `OptimFoundation.Cplex/CplexConfig.cs:748`
- `public int? DependencyCheck` | `OptimFoundation.Cplex/CplexConfig.cs:755`
- `public int? LpFolding` | `OptimFoundation.Cplex/CplexConfig.cs:762`
- `public int? NodePresolve` | `OptimFoundation.Cplex/CplexConfig.cs:768`
- `public bool? PreIndicator` | `OptimFoundation.Cplex/CplexConfig.cs:775`
- `public int? PresolveDual` | `OptimFoundation.Cplex/CplexConfig.cs:782`
- `public int? PresolvePasses` | `OptimFoundation.Cplex/CplexConfig.cs:789`
- `public int? PresolveReduce` | `OptimFoundation.Cplex/CplexConfig.cs:796`
- `public int? PresolveReformulations` | `OptimFoundation.Cplex/CplexConfig.cs:803`
- `public int? RelaxedLpPresolve` | `OptimFoundation.Cplex/CplexConfig.cs:809`
- `public int? RepeatPresolve` | `OptimFoundation.Cplex/CplexConfig.cs:816`
- `public int? Scaling` | `OptimFoundation.Cplex/CplexConfig.cs:823`
- `public int? Symmetry` | `OptimFoundation.Cplex/CplexConfig.cs:830`
- `public int? BarrierAlgorithm` | `OptimFoundation.Cplex/CplexConfig.cs:842`
- `public int? BarrierColumnNonzeros` | `OptimFoundation.Cplex/CplexConfig.cs:848`
- `public long? BarrierCorrectionLimit` | `OptimFoundation.Cplex/CplexConfig.cs:855`
- `public int? BarrierCrossover` | `OptimFoundation.Cplex/CplexConfig.cs:861`
- `public double? BarrierGrowthLimit` | `OptimFoundation.Cplex/CplexConfig.cs:868`
- `public double? BarrierObjectiveRange` | `OptimFoundation.Cplex/CplexConfig.cs:875`
- `public int? BarrierOrdering` | `OptimFoundation.Cplex/CplexConfig.cs:882`
- `public int? BarrierStartAlgorithm` | `OptimFoundation.Cplex/CplexConfig.cs:889`
- `public int? DualSimplexPricing` | `OptimFoundation.Cplex/CplexConfig.cs:896`
- `public double? MarkowitzTolerance` | `OptimFoundation.Cplex/CplexConfig.cs:903`
- `public int? NetworkExtractionLevel` | `OptimFoundation.Cplex/CplexConfig.cs:911`
- `public int? NetworkPricing` | `OptimFoundation.Cplex/CplexConfig.cs:918`
- `public int? NodeAlgorithm` | `OptimFoundation.Cplex/CplexConfig.cs:925`
- `public int? PrimalSimplexPricing` | `OptimFoundation.Cplex/CplexConfig.cs:932`
- `public int? RootAlgorithm` | `OptimFoundation.Cplex/CplexConfig.cs:939`
- `public int? SiftingAlgorithm` | `OptimFoundation.Cplex/CplexConfig.cs:946`
- `public bool? SiftingFromSimplex` | `OptimFoundation.Cplex/CplexConfig.cs:953`
- `public int? SimplexCrash` | `OptimFoundation.Cplex/CplexConfig.cs:960`
- `public int? SimplexDynamicRows` | `OptimFoundation.Cplex/CplexConfig.cs:967`
- `public double? SimplexPerturbationConstant` | `OptimFoundation.Cplex/CplexConfig.cs:974`
- `public bool? SimplexPerturbationIndicator` | `OptimFoundation.Cplex/CplexConfig.cs:981`
- `public int? SimplexPerturbationLimit` | `OptimFoundation.Cplex/CplexConfig.cs:988`
- `public int? SimplexPricingCandidateList` | `OptimFoundation.Cplex/CplexConfig.cs:995`
- `public int? SimplexRefactorFrequency` | `OptimFoundation.Cplex/CplexConfig.cs:1002`
- `public int? SimplexSingularityLimit` | `OptimFoundation.Cplex/CplexConfig.cs:1009`
- `public double? BendersFeasibilityCutTol` | `OptimFoundation.Cplex/CplexConfig.cs:1022`
- `public double? BendersOptimalityCutTol` | `OptimFoundation.Cplex/CplexConfig.cs:1028`
- `public int? BendersStrategy` | `OptimFoundation.Cplex/CplexConfig.cs:1036`
- `public int? BendersWorkerAlgorithm` | `OptimFoundation.Cplex/CplexConfig.cs:1043`
- `public int? CalculateQcpDuals` | `OptimFoundation.Cplex/CplexConfig.cs:1050`
- `public int? FeasOptMode` | `OptimFoundation.Cplex/CplexConfig.cs:1057`
- `public int? MiqcpStrategy` | `OptimFoundation.Cplex/CplexConfig.cs:1064`
- `public bool? QpMakePsd` | `OptimFoundation.Cplex/CplexConfig.cs:1071`
- `public int? QpToLinear` | `OptimFoundation.Cplex/CplexConfig.cs:1078`
- `public int? Sos1Reformulation` | `OptimFoundation.Cplex/CplexConfig.cs:1085`
- `public int? Sos2Reformulation` | `OptimFoundation.Cplex/CplexConfig.cs:1092`
- `public int? SubMipNodeAlgorithm` | `OptimFoundation.Cplex/CplexConfig.cs:1099`
- `public long? SubMipNodeLimit` | `OptimFoundation.Cplex/CplexConfig.cs:1106`
- `public int? SubMipRootAlgorithm` | `OptimFoundation.Cplex/CplexConfig.cs:1113`
- `public int? SubMipScaling` | `OptimFoundation.Cplex/CplexConfig.cs:1120`
- `public int? BarrierDisplay` | `OptimFoundation.Cplex/CplexConfig.cs:1133`
- `public bool? CloneLog` | `OptimFoundation.Cplex/CplexConfig.cs:1139`
- `public int? ColumnRead` | `OptimFoundation.Cplex/CplexConfig.cs:1148`
- `public int? ConflictAlgorithm` | `OptimFoundation.Cplex/CplexConfig.cs:1155`
- `public int? ConflictDisplay` | `OptimFoundation.Cplex/CplexConfig.cs:1162`
- `public int? DataCheck` | `OptimFoundation.Cplex/CplexConfig.cs:1169`
- `public string FileEncoding` | `OptimFoundation.Cplex/CplexConfig.cs:1176`
- `public string IntSolFilePrefix` | `OptimFoundation.Cplex/CplexConfig.cs:1183`
- `public int? MipDisplay` | `OptimFoundation.Cplex/CplexConfig.cs:1190`
- `public long? MipInterval` | `OptimFoundation.Cplex/CplexConfig.cs:1197`
- `public bool? MpsLongNumerics` | `OptimFoundation.Cplex/CplexConfig.cs:1203`
- `public int? MultiObjectiveDisplay` | `OptimFoundation.Cplex/CplexConfig.cs:1209`
- `public int? NetworkDisplay` | `OptimFoundation.Cplex/CplexConfig.cs:1215`
- `public long? NonzeroRead` | `OptimFoundation.Cplex/CplexConfig.cs:1224`
- `public bool? ParamDisplay` | `OptimFoundation.Cplex/CplexConfig.cs:1230`
- `public int? PopulateLimit` | `OptimFoundation.Cplex/CplexConfig.cs:1238`
- `public double? ProbeDetTimeLimit` | `OptimFoundation.Cplex/CplexConfig.cs:1245`
- `public double? ProbeTimeLimit` | `OptimFoundation.Cplex/CplexConfig.cs:1252`
- `public long? QpNonzeroRead` | `OptimFoundation.Cplex/CplexConfig.cs:1261`
- `public long? ReadWarningLimit` | `OptimFoundation.Cplex/CplexConfig.cs:1268`
- `public bool? Record` | `OptimFoundation.Cplex/CplexConfig.cs:1275`
- `public int? RowRead` | `OptimFoundation.Cplex/CplexConfig.cs:1284`
- `public int? SiftingDisplay` | `OptimFoundation.Cplex/CplexConfig.cs:1291`
- `public int? SimplexDisplay` | `OptimFoundation.Cplex/CplexConfig.cs:1298`
- `public double? SolutionPoolAbsGap` | `OptimFoundation.Cplex/CplexConfig.cs:1305`
- `public int? SolutionPoolCapacity` | `OptimFoundation.Cplex/CplexConfig.cs:1312`
- `public int? SolutionPoolIntensity` | `OptimFoundation.Cplex/CplexConfig.cs:1319`
- `public double? SolutionPoolRelGap` | `OptimFoundation.Cplex/CplexConfig.cs:1326`
- `public int? SolutionPoolReplace` | `OptimFoundation.Cplex/CplexConfig.cs:1333`
- `public double? TuningDetTimeLimit` | `OptimFoundation.Cplex/CplexConfig.cs:1339`
- `public int? TuningDisplay` | `OptimFoundation.Cplex/CplexConfig.cs:1346`
- `public int? TuningMeasure` | `OptimFoundation.Cplex/CplexConfig.cs:1354`
- `public int? TuningRepeat` | `OptimFoundation.Cplex/CplexConfig.cs:1362`
- `public double? TuningTimeLimit` | `OptimFoundation.Cplex/CplexConfig.cs:1368`
- `public int? WriteLevel` | `OptimFoundation.Cplex/CplexConfig.cs:1375`
- `public int? Presolve` | `OptimFoundation.Cplex/CplexConfig.cs:1385`

### `OptimFoundation.Cplex/OptEngine.Configuration.cs`

- `public partial class OptEngine` | `OptimFoundation.Cplex/OptEngine.Configuration.cs:17`
- `public override void LoadConfig(ISolverConfig cfg)` | `OptimFoundation.Cplex/OptEngine.Configuration.cs:26` | 用途：套用設定；build、solve 或 experiment run 前呼叫，設定型別必須相容。

### `OptimFoundation.Cplex/OptEngine.cs`

- `public partial class OptEngine : EngineBase<ILOG.CPLEX.Cplex, INumVar, ILinearNumExpr, IRange>` | `OptimFoundation.Cplex/OptEngine.cs:20`
- `public OptEngine(CplexConfig config) : this(config, new ProjectConfig())` | `OptimFoundation.Cplex/OptEngine.cs:53` | 只保存 solver 與預設 project config；尚未建立 CPLEX model，必須先呼叫 `Build()`。
- `public OptEngine(CplexConfig config, ProjectConfig projectConfig) : base(config)` | `OptimFoundation.Cplex/OptEngine.cs:56` | 保存設定；`projectConfig` 為 null 時改用預設值。native CPLEX resources 到 `Build()` 才建立。
- `public OptEngine() : this(new CplexConfig(), new ProjectConfig())` | `OptimFoundation.Cplex/OptEngine.cs:62` | 保存兩份預設設定；不建立 native model，後續先呼叫 `Build()`。
- `public MIPStartEffort MipStartEffort` | `OptimFoundation.Cplex/OptEngine.cs:68`
- `public override int ConstraintCount` | `OptimFoundation.Cplex/OptEngine.cs:106`
- `public string ModelName` | `OptimFoundation.Cplex/OptEngine.cs:109`
- `public string StartTime` | `OptimFoundation.Cplex/OptEngine.cs:112`
- `public int ThreadConstraintCount` | `OptimFoundation.Cplex/OptEngine.cs:115`
- `public override bool SupportsTrajectory` | `OptimFoundation.Cplex/OptEngine.cs:120`
- `public override void EnableTrajectory()` | `OptimFoundation.Cplex/OptEngine.cs:126` | 用途：啟用或擷取 solve/trajectory；enable 在 solve 前，capture/get 在 engine lifecycle 內。
- `public void SetModelName(string name)` | `OptimFoundation.Cplex/OptEngine.cs:180` | 設定 native model name，影響後續 export 與診斷。
- `public void ExportModel(string fileName)` | `OptimFoundation.Cplex/OptEngine.cs:204` | 用途：匯出目前模型或解；在相應 state 完成後呼叫，會寫檔且父目錄須存在。
- `public (int VarCount, int ConstraintCount) ReadModel(string fileName)` | `OptimFoundation.Cplex/OptEngine.cs:264` | 從 CPLEX 支援的 model file 重建目前 native model 與 variable registry，回傳實際變數及限制式數量；檔案須存在，既有模型 state 會被取代。
- `public int ReadSolution(string fileName)` | `OptimFoundation.Cplex/OptEngine.cs:409` | 載入與目前模型相容的 solution file並更新 solution state；engine overload 回傳讀入值數。
- `public string ExportSolution(string fileName)` | `OptimFoundation.Cplex/OptEngine.cs:468` | 用途：匯出目前模型或解；在相應 state 完成後呼叫，會寫檔且父目錄須存在。
- `public override double GetObjectiveValue()` | `OptimFoundation.Cplex/OptEngine.cs:866` | 用途：讀取解；solve 成功或 solution 已載入後呼叫，不會修改模型。
- `public override double GetVariableValue(string name)` | `OptimFoundation.Cplex/OptEngine.cs:870` | 用途：讀取解；solve 成功或 solution 已載入後呼叫，不會修改模型。
- `public override void Dispose()` | `OptimFoundation.Cplex/OptEngine.cs:888` | 用途：釋放 native/IO resources；scope 結束時呼叫，之後不可再使用 instance。
- `public void ResetConstraint()` | `OptimFoundation.Cplex/OptEngine.cs:963` | 用途：清除指定 framework/engine state；進階重用或錯誤復原時呼叫，既有 references 可能失效。
- `public bool CreateGreaterEqualThread(double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine)` | `OptimFoundation.Cplex/OptEngine.cs:1019` | `this` 保存 duplicate key、命名計數與產生的 thread constraint；方法讀取 `targetEngine` 的 LHS pool，以 `targetEngine.Model` 建 expression，再以 `sourceEngine.Model.Le(value, expr)` 建 `value <= Σlhs` constraint object。若 target pool 空則回傳 false；否則完成或略過重複項後清空 target pool、把新 object 留在 `this` 的 thread list 並回傳 true，尚未加入任何 model。
- `public bool CreateLessEqualThread(double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine)` | `OptimFoundation.Cplex/OptEngine.cs:1024` | `this` 管理 duplicate/name/thread list；讀取 `targetEngine` LHS pool，以 `targetEngine.Model` 組 expression，再以 `sourceEngine.Model.Ge(value, expr)` 建 `Σlhs <= value` constraint object。target pool 空時回傳 false；其餘路徑會清空 target pool，新 constraint 只存於 `this`，尚未加入 model。
- `public bool CreateEqualThread(double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine)` | `OptimFoundation.Cplex/OptEngine.cs:1029` | `this` 管理 duplicate/name/thread list；讀取 `targetEngine` LHS pool，以 `targetEngine.Model` 組 expression，再以 `sourceEngine.Model.Eq(value, expr)` 建 `Σlhs = value` constraint object。target pool 空時回傳 false；其餘路徑會清空 target pool，新 constraint 只存於 `this`，尚未加入 model。
- `public void ResetThreadConstraint(OptEngine threadEngine1, OptEngine threadEngine2)` | `OptimFoundation.Cplex/OptEngine.cs:1037` | 用途：清除指定 framework/engine state；進階重用或錯誤復原時呼叫，既有 references 可能失效。
- `public OptEngine CopyModel(OptEngine sourceEngine)` | `OptimFoundation.Cplex/OptEngine.cs:1061` | 用途：跨 engine 複製、合併或建立 thread constraint；進階 API，所有 native objects 必須相容且未 dispose。
- `public OptEngine MergeModel(OptEngine sourceEngine, OptEngine targetEngine)` | `OptimFoundation.Cplex/OptEngine.cs:1092` | 用途：跨 engine 複製、合併或建立 thread constraint；進階 API，所有 native objects 必須相容且未 dispose。
- `public OptEngine MergeVariables(OptEngine targetEngine, HashSet<INumVar> variables)` | `OptimFoundation.Cplex/OptEngine.cs:1104` | 用途：跨 engine 複製、合併或建立 thread constraint；進階 API，所有 native objects 必須相容且未 dispose。
- `public List<string> GetConflictConstraints()` | `OptimFoundation.Cplex/OptEngine.cs:1152` | 用途：取得 infeasibility 診斷；通常在 infeasible solve 後呼叫。
- `public IReadOnlyDictionary<string, double> GetCVSolution()` | `OptimFoundation.Cplex/OptEngine.cs:1176` | 用途：讀取解；solve 成功或 solution 已載入後呼叫，不會修改模型。
- `public IReadOnlyDictionary<string, double> GetIVSolution()` | `OptimFoundation.Cplex/OptEngine.cs:1179` | 用途：讀取解；solve 成功或 solution 已載入後呼叫，不會修改模型。
- `public IReadOnlyDictionary<string, double> GetBVSolution()` | `OptimFoundation.Cplex/OptEngine.cs:1182` | 用途：讀取解；solve 成功或 solution 已載入後呼叫，不會修改模型。

### `OptimFoundation.Cplex/OptExperiment.cs`

- `public sealed class OptExperiment` | `OptimFoundation.Cplex/OptExperiment.cs:13`
- `public string Name` | `OptimFoundation.Cplex/OptExperiment.cs:44`
- `public string FullName` | `OptimFoundation.Cplex/OptExperiment.cs:47`
- `public OptExperiment LoadConfig(ProjectConfig config)` | `OptimFoundation.Cplex/OptExperiment.cs:52` | 用途：套用設定；build、solve 或 experiment run 前呼叫，設定型別必須相容。
- `public OptExperiment CaptureTrajectory(bool enabled)` | `OptimFoundation.Cplex/OptExperiment.cs:64` | 用途：啟用或擷取 solve/trajectory；enable 在 solve 前，capture/get 在 engine lifecycle 內。
- `public OptExperiment AddModel(OptModel model)` | `OptimFoundation.Cplex/OptExperiment.cs:71` | 用途：組裝並執行 experiment；Run 前加入 model/config/trial，Run 會求解並產生輸出。
- `public OptExperiment AddConfig(string label, CplexConfig config)` | `OptimFoundation.Cplex/OptExperiment.cs:82` | 用途：組裝並執行 experiment；Run 前加入 model/config/trial，Run 會求解並產生輸出。
- `public OptExperiment AddTrial(OptModel model, string label, CplexConfig config)` | `OptimFoundation.Cplex/OptExperiment.cs:98` | 用途：組裝並執行 experiment；Run 前加入 model/config/trial，Run 會求解並產生輸出。
- `public Experiment Run()` | `OptimFoundation.Cplex/OptExperiment.cs:114` | 用途：組裝並執行 experiment；Run 前加入 model/config/trial，Run 會求解並產生輸出。

### `OptimFoundation.Cplex/OptModel.cs`

- `public sealed class OptModel` | `OptimFoundation.Cplex/OptModel.cs:12`
- `public string Name` | `OptimFoundation.Cplex/OptModel.cs:15`
- `public OptModel(string name = "Model")` | `OptimFoundation.Cplex/OptModel.cs:26` | 建立命名 recipe 並初始化 build steps；不建立 engine、不求解。
- `public OptModel AddVariables(Action<OptEngine> build)` | `OptimFoundation.Cplex/OptModel.cs:32` | 註冊 variable callback 並回傳同一 OptModel；project build 時先執行。
- `public OptModel AddObjective(Action<OptEngine> build)` | `OptimFoundation.Cplex/OptModel.cs:41` | 註冊 objective callback 並回傳同一 OptModel；variables 後執行。
- `public OptModel AddConstraints(Action<OptEngine> build)` | `OptimFoundation.Cplex/OptModel.cs:50` | 註冊 constraint callback 並回傳同一 OptModel；objective 後執行。
- `public string SourceFile` | `OptimFoundation.Cplex/OptModel.cs:63`
- `public static OptModel ReadModel(string fileName, string name = null)` | `OptimFoundation.Cplex/OptModel.cs:76` | 驗證檔名並回傳保存 `SourceFile` 的 deferred recipe；此時不讀檔、不建立 native state，`ApplyTo` 才呼叫 engine `ReadModel`。
- `public OptModel ReadSolution(string fileName)` | `OptimFoundation.Cplex/OptModel.cs:102` | 驗證檔名並把讀 solution 的 callback 加入 deferred start steps，回傳同一 model；套用模型且 variables 已存在後才真正讀檔。
- `public OptModel AddMIPStart(Func<IReadOnlyDictionary<string, double>> values, string name = null)` | `OptimFoundation.Cplex/OptModel.cs:119` | 用途：加入 warm start；solve 前呼叫，key 必須是目前模型的 canonical variable name。

### `OptimFoundation.Cplex/OptProject.cs`

- `public sealed class OptProject : IDisposable` | `OptimFoundation.Cplex/OptProject.cs:14`
- `public OptProject(string name, int retentionDays = 30)` | `OptimFoundation.Cplex/OptProject.cs:19` | 驗證 name、保存 retentionDays、建立 framework 目錄並清除逾期 outputs；尚未 solve。
- `public string Name` | `OptimFoundation.Cplex/OptProject.cs:46`
- `public int RetentionDays` | `OptimFoundation.Cplex/OptProject.cs:49`
- `public OptProject LoadConfig(ProjectConfig config)` | `OptimFoundation.Cplex/OptProject.cs:65` | 用途：套用設定；build、solve 或 experiment run 前呼叫，設定型別必須相容。
- `public OptEngine Engine` | `OptimFoundation.Cplex/OptProject.cs:74`
- `public bool IsSuccess` | `OptimFoundation.Cplex/OptProject.cs:77`
- `public Trial Trial` | `OptimFoundation.Cplex/OptProject.cs:80`
- `public TimeSpan TotalElapsed` | `OptimFoundation.Cplex/OptProject.cs:83`
- `public TimeSpan BuildModelElapsed` | `OptimFoundation.Cplex/OptProject.cs:86`
- `public bool Solve(OptModel model, CplexConfig config, Action<OptEngine> onSolved = null, Action<OptEngine> beforeSolve = null)` | `OptimFoundation.Cplex/OptProject.cs:96` | 用途：執行求解；模型與設定必須完成，會更新 status、metrics 與 solution state。
- `public void Dispose()` | `OptimFoundation.Cplex/OptProject.cs:152` | 用途：釋放 native/IO resources；scope 結束時呼叫，之後不可再使用 instance。
- `public const string SolveExperimentName = "solve";` | `OptimFoundation.Cplex/OptProject.cs:154` | 正式求解紀錄在累積檔 Experiment 欄的值。
- `public OptExperiment Experiment(string name, string description = null)` | `OptimFoundation.Cplex/OptProject.cs:162` | 只建立並回傳綁定目前 project、name、description 的 `OptExperiment` builder；不建立 `Experiment` result、`CreatedAt` 或 `Trials`，也不執行求解。
