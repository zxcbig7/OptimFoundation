# OptimFoundation

OptimFoundation 是以 .NET 8 建立 MILP 模型的 solver-agnostic framework，目前提供 IBM CPLEX 與 Gurobi adapter。

## Packages

| Package | Responsibility |
| --- | --- |
| `OptimFoundation.Core` | 資料列、IO、命名、變數/限制式通用邏輯、logging、experiments |
| `OptimFoundation.Generators` | Set / Parameter / Variable source generator |
| `OptimFoundation.Cplex` | IBM CPLEX adapter |
| `OptimFoundation.Gurobi` | Gurobi adapter |

## Quick example

```csharp
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Modeling;
using OptimFoundation.Cplex;

[OptSet]
[OptDim<string>("Employee")]
public sealed partial class Set_Employee { }

[OptSet]
[OptDim<DateTime>("Date")]
public sealed partial class Set_Date { }

[OptVar]
[OptDim<DateTime>("Date")]
[OptDim<string>("Employee")]
public sealed partial class VariableB_Assign { }

var data = OptData.Load(() => new Dataload());

var model = new OptModel("Canonical")
    .AddVariables(engine =>
        engine.BuildVars<VariableB_Assign>(data.set_Date, data.set_Employee))
    .AddObjective(engine => new ObjectiveFunction(/* dependencies */).Build(engine))
    .AddConstraints(engine => new Constraint_Assign(/* dependencies */).Build(engine));

using var project = new OptProject(model)
    .UseConfig(() => new ProjectConfig { ProjectName = "Example" })
    .UseConfig(() => new CplexConfig());

bool solved = project.Execute();
```

資料載入對 Set 與 Parameter 使用同一個入口：

```csharp
set_Employee = source.Load<Set_Employee>("employees.csv");
parameter_Demand = source.Load<Parameter_Demand>("demand-2026.csv");
```

所有輸入 CSV 都必須有與 generated properties 對應的表頭。

## Build and test

```powershell
dotnet build OptimFoundation.sln
dotnet test tests/OptimFoundation.Cplex.Tests/OptimFoundation.Cplex.Tests.csproj
```

Solver managed/native libraries 與 license 不在 repository 內。CPLEX 使用 `CplexDir`，Gurobi 使用 `GUROBI_HOME`；不得 commit solver DLL 或 license。

## Documentation

- [開發者指南](specs/developer-guide.md)
- [架構盤查](specs/2026-08-09-api-architecture-audit.md)
- [多維 Set 規格](specs/2026-08-08-multidim-set.md)
- [IO 規格](specs/2026-08-08-io-read-surface.md)
- [Tutorial template](Templates/Tutorial/README.md)
