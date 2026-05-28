# OptimFoundation

A .NET framework for building mixed-integer programming (MIP) optimization models with a clean, solver-agnostic API.

## Packages

| Package | Description |
| --- | --- |
| `OptimFoundation.Core` | Abstract base classes, engine contract, variable builder |
| `OptimFoundation.Cplex` | IBM CPLEX solver implementation |
| `OptimFoundation.Gurobi` | Gurobi solver implementation |
| `OptimFoundation.Db` | Database utilities |
| `OptimFoundation.Db.Oracle` | Oracle connector |

## Quick Example

```csharp
// 1. Define a variable class (properties only, no constructor)
public class VariableB_Assign : VariableBase
{
    public DateTime Date     { get; set; }
    public string   Employee { get; set; }
    public string   Shift    { get; set; }
}

// 2. Build variables
optEngine.BuildBVs<VariableB_Assign>(dates, employees, shifts);

// 3. Write a constraint
employees.ForEach(e =>
{
    shifts.ForEach(s =>
        engine.AddLHS(1, new VariableB_Assign { Date = d, Employee = e, Shift = s }));
    engine.AddRHS(1);
    engine.CreateEqual($"OneShift@{d:yyyy_MM_dd}@{e}");
});

// 4. Solve
bool solved = optEngine.Solve();
```

## Getting Started

See [specs/developer-guide.md](specs/developer-guide.md) for the full development guide.

## Requirements

- **OptimFoundation 函式庫**：.NET Framework 4.8（所有套件均 target `net48`）
- **消費端專案**：.NET Framework 4.8 或 .NET 8+（可直接 reference `net48` DLL）
- IBM CPLEX 或 Gurobi 授權（依使用的 solver 而定）

---

## DLL 參考設定

OptimFoundation 不內附 solver DLL，使用前需自行將以下 DLL 加入專案參考。

### IBM CPLEX

需要自行安裝 IBM ILOG CPLEX Optimization Studio，並 reference 以下兩個 DLL：

| DLL | 預設安裝路徑 |
| --- | --- |
| `ILOG.Concert.dll` | `C:\Program Files\IBM\ILOG\CPLEX_StudioXXXX\cplex\bin\x64_win64\` |
| `ILOG.CPLEX.dll` | 同上 |

**.csproj 設定範例：**

```xml
<ItemGroup>
  <Reference Include="ILOG.Concert">
    <HintPath>path\to\ILOG.Concert.dll</HintPath>
  </Reference>
  <Reference Include="ILOG.CPLEX">
    <HintPath>path\to\ILOG.CPLEX.dll</HintPath>
  </Reference>
  <Reference Include="OptimFoundation.Core">
    <HintPath>path\to\OptimFoundation.Core.dll</HintPath>
  </Reference>
  <Reference Include="OptimFoundation.Cplex">
    <HintPath>path\to\OptimFoundation.Cplex.dll</HintPath>
  </Reference>
</ItemGroup>
```

### Gurobi

需要自行安裝 Gurobi Optimizer，並 reference 以下 DLL：

| DLL | 預設安裝路徑 |
| --- | --- |
| `Gurobi110.NET.dll` | `C:\gurobi1100\win64\bin\`（版本號依安裝版本調整） |

**.csproj 設定範例：**

```xml
<ItemGroup>
  <Reference Include="Gurobi110.NET">
    <HintPath>path\to\Gurobi110.NET.dll</HintPath>
  </Reference>
  <Reference Include="OptimFoundation.Core">
    <HintPath>path\to\OptimFoundation.Core.dll</HintPath>
  </Reference>
  <Reference Include="OptimFoundation.Gurobi">
    <HintPath>path\to\OptimFoundation.Gurobi.dll</HintPath>
  </Reference>
</ItemGroup>
```

### OptimFoundation DLL 取得方式

從 source code build：

```powershell
# 先 build Core
cd src\OptimFoundation.Core
dotnet build -c Debug

# 再 build 所需 solver
cd ..\OptimFoundation.Cplex
# 需透過 Visual Studio MSBuild（dotnet build 無法解析 CPLEX HintPath）
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" .\OptimFoundation.Cplex.csproj /p:Configuration=Debug
```

> **注意**：`OptimFoundation.Cplex` 只能用 Visual Studio MSBuild 建置，因為 CPLEX DLL 路徑需要對應實際安裝位置。
> 建置後的 DLL 位於 `src\OptimFoundation.Cplex\bin\Debug\net48\`。
