# 架構整頓基準（Architecture Consolidation）

- **日期**: 2026-07-10
- **狀態**: shipped
- **對應 commits**: `d49aee4` → `dd876db`（5 個連續 commit）

## 決策與依據

| 決策 | 內容 | Why |
| --- | --- | --- |
| Db.Oracle 併入 Core | `OracleDBCtrl` 移至 `src/OptimFoundation.Core/IO/`，namespace 保留 `OptimFoundation.Db.Oracle`，Oracle.ManagedDataAccess 21.11.0 進 Core | 單檔專案無獨立存在價值；namespace 保留讓外部 `using` 不破 |
| Generators 維持獨立專案 | source generator 必須是 netstandard2.0 且以 Analyzer 形式被引用，技術上不可併入 Core | Roslyn 機制限制（編譯器載入，非執行期） |
| 刪除 OptimFoundation.Solver + Template_Solver | 依賴跨 repo `../MILP Solver/Solver.dll`（本機不存在，無法建置）；csproj / 類別名均為 Gurobi 複製殘留 | 死專案；需要時從 git 歷史（`50f05d3` 前）找回 |
| 版控歸位 | Templates 與 tests 從 workspace 根搬進 git repo，取代 repo 內過期副本；sln 補全 9 專案 | 原本「實際使用的檔案沒版控、版控裡是過期副本」 |
| TFM 全鏈 net48 | templates + tests 由 net8.0 統一為 net48 + LangVersion latest | CPLEX 2211 只支援 .NET Framework；消除 net8→net48 compat mode 的隱性 runtime 風險 |

## 目前架構（基準狀態）

```
OptimFoundation.sln（9 專案，dotnet build 全綠）
├── src/
│   ├── OptimFoundation.Core        net48（NLog、Oracle.ManagedDataAccess 21.11、System.Text.Json 8.0.5）
│   ├── OptimFoundation.Cplex       net48 → Core；ILOG 參考走 $(CplexDir)
│   ├── OptimFoundation.Gurobi      net48 → Core；$(GUROBI_HOME) 條件式（無 Gurobi 時編 stub）
│   └── OptimFoundation.Generators  netstandard2.0（Roslyn source generator，尚無消費者）
├── Templates/                      全部 net48 + ProjectReference（不再 HintPath 指 bin）
│   ├── Template_CPLEX / Template_Gurobi / Template_ThreadTest / FeatureTest
└── tests/OptimFoundation.Cplex.Tests  net48，45 tests（unit + CPLEX integration）
```

## 慣例（新專案 / 新 template 遵守）

- solver DLL 參考一律走可覆寫 property：CPLEX 用 `$(CplexDir)`（預設 `C:\IBM\ILOG\CPLEX_Studio2211`）、Gurobi 用 `$(GUROBI_HOME)` + `Exists` 條件式
- 框架內部依賴一律 `ProjectReference`，NEVER `Reference HintPath` 指 bin 輸出
- 全鏈 `net48` + `LangVersion latest`；NEVER 在消費端用 net8-only API/namespace
- NLog / Oracle 由 Core 傳遞，消費端不重複宣告

## 待辦（本次未做）

- Generators 接線：canonical template 以 `<ProjectReference OutputItemType="Analyzer">` 引用 + 現有 Variable/Parameter class 轉 attribute 宣告式
- Templates 對等化：CPLEX / Gurobi 兩套範本結構與註解對齊
- `ITunableConfig` 兩實作（CplexConfig / GurobiConfig）對映方式統一
- `Logging` 全域可變靜態狀態的多執行緒安全（ThreadTest 場景）
