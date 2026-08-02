---
title: OptimFoundation config / runner 重構 — Codex 團隊交接文件
status: draft
created: 2026-08-01
updated: 2026-08-01
modules: [handoff]
---

# Codex 交接：OptimFoundation config / runner 重構

> **先讀這份，再依 Packet 進對應 spec。**
> 執行的 agent 沒有原始討論的 context，本文件與三份 spec 是唯一權威來源。
> 任何本文件沒寫、spec 也沒寫的事 → **停下回報，NEVER 自行推測**。

## 這次要做什麼（一句話）

把 OptimFoundation 的設定拆成「專案 config」與「solver config」兩層，並讓 `OptModel` 降格為純模型定義、`OptProject` / `OptExperiment` 成為對稱的兩個執行環境；然後把文件與 8 個練習專案遷移過去。

## 環境

| 項目 | 值 |
| --- | --- |
| `$FW` | `C:/Users/zxcbi/Desktop/Projects/LLMDevFramework`（本文件與三份 spec 所在） |
| `$OPT` | `C:/Users/zxcbi/Desktop/Projects/OptimizationFramework` |
| .NET | net8.0（本機 SDK 10.0.301，可正常 build net8 目標） |
| 求解器 | IBM CPLEX 22.1.1，已驗證可實跑 |
| Gurobi | **授權過期，不在開發 scope**。只需保持編譯通過 |

### git 結構（重要）

`$OPT` **父層不是 git repo**，但兩個工作目錄各自是：

| repo | 路徑 | 分支 |
| --- | --- | --- |
| AI-Modeling | `$OPT/AI-Modeling` | `dev` |
| OptimFoundation | `$OPT/OptimFoundation/OptimFoundation` | `dev` |

`$FW`（LLMDevFramework）本身也是 git repo，分支 `dev`。

## 三份 spec

| Spec | 檔 | 範圍 |
| --- | --- | --- |
| 1 | [`2026-08-01-optimfoundation-dual-config.md`](2026-08-01-optimfoundation-dual-config.md) | `ProjectConfig` 新增、`CplexConfig` 刪 6 個非 solver 成員、`ISolverConfig` 刪 2 個、框架 solution 內 12 檔遷移 |
| 2 | [`2026-08-01-optimfoundation-runner-symmetry.md`](2026-08-01-optimfoundation-runner-symmetry.md) | `OptModel` 語意翻轉、`OptProject` / `OptExperiment`、`Clone()`、`DataContext` 凍結 |
| 3 | [`2026-08-01-optim-docs-and-projects-migration.md`](2026-08-01-optim-docs-and-projects-migration.md) | 34 份文件 + `dlls/` 更新 + 17 個 `.cs` 遷移 |

## Packet 一覽

| # | 內容 | Spec | 平行度 | 驗收 |
| --- | --- | --- | --- | --- |
| **0** | 前置：commit、開 branch、採基準值 | — | 由**人**執行 | 見下 |
| **1a** | 框架：config 分層 | 1 | ❌ 單一 agent | `dotnet build OptimFoundation.sln` 0 error + xUnit 全綠 |
| **1b** | 框架：runner 對稱化 | 2 | ❌ 單一 agent，接在 1a 後 | 同上 + 三個 `Templates` 實跑結果不變 |
| **2a** | 文件 Tier 1（15 份，AI 引導） | 3 | ✅ 2–3 agents | `rg` 查無舊 API 殘留 |
| **2b** | 文件 Tier 2（2 份，API 權威） | 3 | ✅ 1 agent | 同上 + `enableLog` 預設值修正 |
| **2c** | 文件 Tier 3（9 份，教學導覽） | 3 | ✅ 2 agents | 同上 |
| **2d** | 文件 Tier 5（8 份，加 frontmatter） | 3 | ✅ 1 agent | 每份有 `superseded_by` |
| **3** | rebuild + 覆蓋 `dlls/` + 解 pin | 3 | ❌ 單點 | **反直覺，見 spec 3** |
| **4** | 17 個 `.cs` + Tier 4 文件 | 3 | ✅ **8 agents**（一專案一個） | `dotnet run` 比對 `baseline/BASELINE.md` |

## 依賴鏈

```
Packet 0（人）
   ↓
  1a ──→ 1b ──→ 2a ──→ 3 ──→ 4
                  ├─ 2b ┐
                  ├─ 2c ├─ 可與 3、4 平行
                  └─ 2d ┘
```

**兩條不可違反的順序**：

1. `2a → 4` —— Tier 1 是 **AI 引導文件**。沒改就派 agent 做 Packet 4，它會讀著舊的 `claudemdTemplate` / `Prompts` 生出舊架構的 code
2. `3 → 4` 且**背靠背** —— Packet 3 覆蓋 `dlls/` 之後、Packet 4 完成之前，`AI-Modeling` 處於編不過的狀態。這段期間 **MUST NOT** 插入其他工作

## Packet 0（人做，尚未完成）

| # | 項目 | 狀態 |
| --- | --- | --- |
| ① | 釐清並提交 `OptimFoundation` 的 **79 個未提交檔案**（來源不明，非本次工作產出） | ⏸ **未完成，阻塞全部** |
| ② | 提交 `AI-Modeling` 的 24 個未提交檔案（Template 重構，已驗證） | ⏸ 未完成 |
| ③ | 採基準值 | ✅ **已完成** → `$OPT/AI-Modeling/baseline/BASELINE.md` |
| ④ | 兩個 repo 各開 branch | ⏸ 未完成 |

> ① 為何阻塞：agent 改壞東西的標準救援動作是 `git reset --hard`。在 79 個未提交檔案上這樣做會把別人做到一半的工作一併清掉，且無任何提示。

建議 branch：`feat/dual-config`（1a）→ `feat/runner-symmetry`（1b）→ `chore/docs-migration`（2x）→ `chore/projects-migration`（3、4）。

## 所有 agent 通用的硬規則

1. **NEVER 改任何模型的數學內容**。本次全部工作只做 config / runner / 文件遷移。目標值變了 = bug，不是預期
2. **NEVER 為了讓驗收通過而調整容差、改基準值、或修改 `BASELINE.md`**
3. 檔案內容與 spec 的 before pattern 不符 → **跳過該檔並回報**，NEVER 自行推測意圖
4. `dotnet build` 修 **5 次**仍失敗 → 停下回報，NEVER 硬修
5. 只准動派工單明列的檔案。需要動清單外的檔案 → 停下回報
6. **NEVER 改 `HISTORY/` 底下任何東西**
7. `Projects/WoodworkingShop/` **無 `.csproj`**，是空殼資料夾，不在遷移範圍
8. 寫檔規則：NEVER 欄位對齊（`=` / `:` / 註解一律單一空格）；NEVER 產生 UTF-8 BOM

## 派工單範本

```
【任務】<一句話>

【唯一權威來源】
  $FW/specs/<spec 檔名>.md  §<章節>
  （本文件沒寫、spec 也沒寫的事 → 停下回報）

【檔案清單】（只准動這些）
  <逐行列出絕對路徑>

【before / after】
  <逐字貼 spec 的 pattern>

【前置條件】
  <依賴哪個 Packet 已完成>

【驗收】（可直接執行）
  <指令>
  預期：<可機器判定的結果>

【停下條件】
  - 檔案內容與 before pattern 不符 → 跳過該檔並回報
  - build 修 5 次仍失敗 → 停下回報
  - 需要改模型數學內容 → 一律停下
  - 需要動清單外的檔案 → 停下回報
```

## 已知的三個陷阱（每個 agent 都該知道）

### ① `enableLog` 的預設值，文件是錯的

`CPLEX_API_REFERENCE.md` §4 寫 `enableLog = false`，**原始碼是 `true`**（`CplexConfig.cs:18`）。照文件遷移會讓所有專案安靜地失去 Console solver log。**一律以原始碼為準**，並在 Packet 2b 修正該文件。

### ② 舊的 `new OptModel("X")` 那個字串是**專案名**不是模型名

改後 `OptModel` 是模型定義、名字用於實驗 label；專案名要搬去 `ProjectConfig.ProjectName`。單一模型的專案兩者可同名，但語意不同，別搞混。

### ③ 舊的 `BuildModel.Build()` 要拆成兩半

原方法內**第一行**是 `new ObjectiveFunction(…).Build()` → 歸 `AddObjective`；其餘 `new Constraint_*(…).Build()` → 歸 `AddConstraints`。順序由框架保證，但拆錯會讓目標式跑進 constraints 階段。

## 完成的定義

全部 Packet 完成後，下列全部成立：

- `dotnet build OptimFoundation.sln` 0 error、xUnit 全綠
- 8 個練習專案 build + run，Status 與目標值與 `baseline/BASELINE.md` 一致
- `HospitalRostering_Generator` 與 `_Manual` 目標值仍相同
- `AI-Modeling/Template` 目標值 158、限制式 93/155/5/125/305/5/5
- `rg "VariableCreate|BuildModel\.cs|ExperimentRunner|enableLog|exportLP"` 在 Tier 1–3 文件查無殘留
- `/harness-eval milp-model` 不退步
