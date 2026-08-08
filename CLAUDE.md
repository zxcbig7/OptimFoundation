# OptimFoundation Workspace

<system_context>
Solver-agnostic MILP 建模框架（C#, .NET 8）。後端支援 IBM CPLEX 與 Gurobi，資料層走 Oracle（Oracle.ManagedDataAccess.Core）。
本檔位於 git repo 根（remote: OptimFoundation.git）。註：外層 `OptimizationFramework/OptimFoundation/` 只是資料夾外殼、非 repo，NEVER 在那層跑 git init（疊 repo 會亂）。
</system_context>

<critical_notes>
- MUST 動框架 code 前先讀 `specs/developer-guide.md` —— 13 章完整 API 手冊（Variable / Parameter / Pool / BuildModel / Config / 結果 API）—— Why: 框架有自己的建模慣例（Pool API、Variable Key 格式），憑直覺寫會偏離慣例
- 全鏈 `net8.0` + `LangVersion latest`（含 Templates 與 tests；Generators 例外：netstandard2.0，Roslyn 要求）—— Why: CPLEX/Gurobi 的 .NET assembly 是 managed wrapper，net8 直接載（native `cplex2211.dll` 要在 PATH）；NEVER 降回 net48、NEVER 用 Framework-only 套件（Oracle 一律 `Oracle.ManagedDataAccess.Core`）
- 改任何 public API 後 MUST：更新 `specs/developer-guide.md` → 同步 sibling 鏡像 `../AI-Modeling/CPLEX_API_REFERENCE.md`（檔頭標同步日期）→ 提醒消費端依 `AI-Modeling/dlls/README.md` 回填 DLL 並更新 `VERSION.txt` —— Why: stale DLL 會遮住 API drift，消費端看似 build 綠實則已編不過（2026-07-11 事故）
- solver DLL 不在 repo 內：CPLEX 走 `$(CplexDir)` property（預設 `C:\IBM\ILOG\CPLEX_Studio2211`）、Gurobi 走 `$(GUROBI_HOME)` 條件式，NEVER commit DLL 進 git、NEVER 用 HintPath 指 bin 輸出
- 框架內部依賴一律 `ProjectReference`；NLog / Oracle 由 Core 傳遞，消費端不重複宣告
- Variable class 只放 properties、不寫 constructor（框架用 reflection 組 key）
- 本專案是 class library，`$FW/.Net Web API` 只參考 C# 通則，Web API 專屬規則（controller / DI / middleware）不適用
- 架構基準與慣例全文 → `specs/2026-07-10-architecture-consolidation.md`
</critical_notes>

<file_map>
（repo 根；main / origin/main）sln 含全部 8 專案（4 src + 3 Templates + 1 tests）
OptimFoundation.sln - 主 solution（dotnet build 全綠是基準）
  src/OptimFoundation.Core/ - EngineBase、ISolverEngine、VariableBuilder、DesignBases、Experiments、DataContext/DataValidator（資料防護）、Numeric、IO（Csv + Oracle DB）/ Logging 工具
  src/OptimFoundation.Cplex/ - CPLEX 實作（OptEngine、OptModel、CplexConfig）
  src/OptimFoundation.Gurobi/ - Gurobi 實作（OptEngine、GurobiConfig；無 Gurobi 時編 stub）
  src/OptimFoundation.Generators/ - AutoSetsGenerator（source generator，netstandard2.0；消費者：三個 Templates 與 sibling AI-Modeling 的 HospitalRostering 系列，皆以 analyzer 引用）
  Templates/ - 消費端起手範本（皆已套用 DataContext 資料防護）：Tutorial（權威示範）/ FJSP_BASIC_BRICK / Template_CPLEX
  tests/ - OptimFoundation.Cplex.Tests（137 tests，unit + CPLEX integration）
  specs/ - developer-guide.md（API 手冊）、architecture-consolidation（架構基準）、framework-dev-spec、cplex-project-dev-spec
</file_map>

<paved_path>
## 建模標準流程（消費端）

1. 定義 Variable class：繼承 `VariableBase`，只放 properties
2. `optEngine.BuildBVs<T>(...)` 建變數
3. Pool API 寫限制式：`AddLHS(...)` → `AddRHS(...)` → `CreateEqual("name")` / 不等式版本
4. Objective → `BuildModel()` → `Solve()` → 結果 API
5. 新題目從 `Templates/Tutorial` 複製起手（權威示範：`Dataload : DataContext` + `OptData.Load(...)`，載入後框架自動驗資料）；排班類題目可參考 `Templates/Template_CPLEX`
6. Dataload MUST 是 `partial class ... : DataContext`，建構走 `OptData.Load(() => new Dataload(...))` —— 載入後自動跑參照完整性 / key 唯一性 / 數值 sanity 檢查，NEVER 在專案端手寫這類驗證（規格：`specs/2026-07-18-framework-data-guard.md`）

## 改框架本體

非 trivial 改動走 `/sdd` → 規格存 `specs/` → 產 `CodeMap.md` → stub → 實作
</paved_path>

<common_tasks>
- 加 solver 參數 → `CplexConfig.cs` / `GurobiConfig.cs`
- 加變數建構方式 → `Core/VariableBuilder.cs`
- 改 DB 存取 → `Core/IO/OracleDBCtrl.cs`（基底 `Core/IO/DBCtrlBase.cs` + `IDbCtrl.cs`）
- 跑測試 → `dotnet test tests/OptimFoundation.Cplex.Tests`（需本機 CPLEX）
</common_tasks>

<fatal_implications>
- NEVER commit solver DLL 或 license 檔
- NEVER 在外層 `OptimizationFramework/OptimFoundation/` 跑 git init —— repo 就是本層，疊 repo 會亂
</fatal_implications>
