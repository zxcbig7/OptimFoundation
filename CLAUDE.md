# OptimFoundation Workspace

<system_context>
OptimFoundation 是 C# / .NET 8 的 solver-agnostic MILP framework。Core 不引用 solver SDK；CPLEX adapter 實作 solver primitives；Generator 使用 netstandard2.0。
</system_context>

<critical_notes>

- 修改 framework 前先讀 `specs/developer-guide.md` 與相關 dated spec。
- public API 變更後同步 developer guide、CodeMap、Templates 與 sibling `AI-Modeling` 規範。
- solver DLL/license 不進 git。CPLEX 走 `CplexDir`。
- framework 內部依賴使用 `ProjectReference`。
- 資料讀寫一律以 `FolderDir`（`FolderDir.Input.GetPathFile(...)` 等）決定位置，NEVER 在 code 或文件裡 hardcode 資料夾字串；csproj 不做資料複製，輸入由使用者放進 `FolderDir.Input`。
- 不覆寫或還原 workspace 中不屬於目前任務的既有變更。
- 所有主動錯誤在 throw 前用 `Logging.ErrorOnce` 留下包含事件名、位置、值、原因、`結果=中止` 的錯誤 log。
- log 與例外訊息一律中文（求解器指標 Bound / Gap 沿用英文，見 24.2），格式、欄位名、結果值、用詞照 `specs/developer-guide.md` 第 24 章；新增 log 前先對照。
- public API boundary 記錄未預期例外後原樣 rethrow；同一 exception 只記一次。
- 命名規則（使用者定案，新增 API 前先對照）：
  - 設定類別一律 `XxxConfig`（`CplexConfig`、`ProjectConfig`），吃設定的方法一律 `LoadConfig`；NEVER 新增 `XxxOptions` / `UseXxx` / `Configuration(...)` 這類變體
  - 讀檔一律 `Read` + 名詞（`ReadModel`、`ReadSolution`），寫 solver 檔一律 `Export` + 名詞（`ExportModel`、`ExportSolution`）
  - 方法動詞在前（`ResetConstraint`、`MergeVariables`），同一組 API 的拼字與結構要一致（`CreateLessEqual` / `CreateGreaterEqual` / `CreateEqual`）
  - 不縮寫（`CreateLessEqualSoft`，不是 `CreateLeSoft`）；資料夾單數全字；縮寫詞只首字大寫（`Db`，不是 `DB`）

</critical_notes>

<file_map>

- `src/OptimFoundation.Core/`：data rows、IO、naming、EngineBase、logging、experiments。
- `src/OptimFoundation.Generators/`：`[OptSet]` / `[OptParam]` / `[OptVar]` + primitive `OptDim` source generation。
- `src/OptimFoundation.Cplex/`：CPLEX engine、model、project、experiment、config。
- `Templates/`：現行 consumer examples；`Templates/Template/` 是標準範本（積木以示範的框架功能命名，功能對照見 developer-guide 第 23 章，新專案從它複製）。
- `tests/OptimFoundation.Cplex.Tests/`：unit/integration tests。
- `specs/developer-guide.md`：唯一說明文件（概念入門、逐步教學、範本導覽、API 權威）；教學內容一律寫進這份，不另開文件。
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
7. `OptModel` 組裝；`new OptProject(name)` 是唯一入口：`.Solve(model, config, onSolved)` 正式求解（`onSolved` 接 `ISolutionSink` 輸出），`.Experiment(name)` 做 model × config 交叉比較。
8. 範本 `Program.cs` 的 CLI 是兩軸自由組合：模型來源（預設從 CSV 建構；`read-model <file>` 讀既有模型檔、不讀 CSV）× 執行方式（預設正式求解；`exp` 實驗），例 `-- read-model <file> exp`。`-- import-data <raw>` 是獨立的資料前處理（只有不規則來源才需要）。結構：`0. 設定`（`ProjectConfig` + `productionBaseline`）→ `1. 模型來源`（`OptModel.ReadModel` 或 `BuildModel(data)`）→ `2. 環境`（exp / 正式求解只拿 `model`，不管來源；read-model 的實驗名加模型名，紀錄檔名跟 canonical 分得開，正式求解不跑需要資料的解驗證）。

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
