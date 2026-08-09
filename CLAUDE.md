# OptimFoundation Workspace

<system_context>
OptimFoundation 是 C# / .NET 8 的 solver-agnostic MILP framework。Core 不引用 solver SDK；CPLEX/Gurobi adapter 實作 solver primitives；Generator 使用 netstandard2.0。
</system_context>

<critical_notes>

- 修改 framework 前先讀 `specs/developer-guide.md` 與相關 dated spec。
- public API 變更後同步 developer guide、CodeMap、Templates、docs 與 sibling `AI-Modeling` 規範。
- solver DLL/license 不進 git。CPLEX 走 `CplexDir`，Gurobi 走 `GUROBI_HOME`。
- framework 內部依賴使用 `ProjectReference`。
- 不覆寫或還原 workspace 中不屬於目前任務的既有變更。
- 所有主動錯誤在 throw 前留下包含 event code、context、value、reason、`result=aborted` 的 Error Log。
- public API boundary 記錄未預期例外後原樣 rethrow；同一 exception 只記一次。

</critical_notes>

<file_map>

- `src/OptimFoundation.Core/`：data rows、IO、naming、EngineBase、logging、experiments。
- `src/OptimFoundation.Generators/`：`[OptSet]` / `[OptParam]` / `[OptVar]` + primitive `OptDim` source generation。
- `src/OptimFoundation.Cplex/`：CPLEX engine、model、project、experiment、config。
- `src/OptimFoundation.Gurobi/`：Gurobi engine/config。
- `Templates/`：現行 consumer examples。
- `tests/OptimFoundation.Cplex.Tests/`：unit/integration tests。
- `specs/developer-guide.md`：現行 API 權威。
- `CodeMap.md`：source map。

</file_map>

<paved_path>

## Consumer modeling

1. Set：`[OptSet]` + 一到多個 primitive `[OptDim<T>("Name")]`。
2. Parameter：`[OptParam]` + 零到多個 primitive `OptDim`，固定生成 `QTY`。
3. Variable：`[OptVar]` + primitive `OptDim`；B/C/I 前綴決定型別。
4. Set/Parameter 都以 `IDataSource.Load<T>` 載入為 `List<T>`，CSV 都有表頭。
5. 一般變數入口 `BuildVars<T>`；型別專用 builders 僅在自訂 bounds 或維護需求使用。
6. 限制式使用 `AddLHS` / `AddRHS` + `CreateXxx(this, dims...)`。
7. `OptModel` 組裝，`OptProject.Execute()` 求解，`ISolutionSink` 輸出。

## Framework change

1. 先確認 public contract 與 adapter boundary。
2. 實作 source + tests。
3. 更新 XML comments 與所有對外文件。
4. 執行 solution build、tests、template build 與 stale API scan。

</paved_path>

<commands>

```powershell
dotnet build OptimFoundation.sln
dotnet test tests/OptimFoundation.Cplex.Tests/OptimFoundation.Cplex.Tests.csproj
```

</commands>
