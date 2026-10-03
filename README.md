# OptimFoundation

.NET 8 的 solver-agnostic MILP framework，目前提供 IBM CPLEX adapter。

所有說明（概念入門、逐步教學、範本導覽、public API reference）都在同一份文件：[開發指南](specs/developer-guide.md)。

```powershell
dotnet build OptimFoundation.sln
dotnet test tests/OptimFoundation.Cplex.Tests/OptimFoundation.Cplex.Tests.csproj
```

CPLEX 的 DLL 與 license 不在 repo 內，build 時由 `CplexDir` 指定；不得 commit。
