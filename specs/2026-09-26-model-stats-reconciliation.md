---
title: 模型統計對帳（框架建模統計 vs solver 模型實際統計）
status: superseded
created: 2026-09-26
modules: [core, cplex, experiments, templates]
related: 2026-09-18-model-source-duality-and-profile.md
---

# 模型統計對帳

> 2026-10-03 已撤除：三方對帳與 `ModelStats.cs` 刪掉，只留 `Solve()` 前的 `[UNREFERENCED_VARIABLES]` WARN；模型數量與目標式方向一律直接讀 CPLEX（`SolveMetrics`、`-meta.csv` model 區段，schema v11）。

## Summary

框架在建模時記下「親手建了什麼」（建模記帳），solver 模型手上有「實際收了什麼」。兩者過去各自印 log、從不比對。本次讓 `EngineBase` 在每次 `Solve()` 前逐項對帳，不一致就寫 `[MODEL_STATS_MISMATCH]` warn（不阻擋求解），並把結果帶進 `SolveMetrics`，讓建模階段（log）與 tuning 階段（Trial JSON、實驗說明檔、ModelTuner facts）都看得到。

## 三個來源

| 來源 | 內容 | 取值 |
| --- | --- | --- |
| 建模記帳 | Build*Vs / Create* / 軟性限制式 / 目標式 / 匯入 re-index / OptEngine 直接建模入口登記的數量與型別 | `EngineBase` 私有記帳；`VariableBuildCounts` / `ConstraintBuildCounts` 的 Actual 合計 + 型別分布 + 匯入部分 |
| 框架索引 | `Variables` 與限制式清單 | `VariableCount` / `ConstraintCount` |
| solver 模型 | CPLEX 實際持有的內容 | 新 primitive `ReadSolverModelCounts()`：`Ncols` / `Nrows` / `NbinVars` / `NintVars` / `NSOSs` / `NQCs` / `Nindicators` / `NsemiContVars` / `NsemiIntVars` / `NLCs` / `NUCs` / `GetObjective().Sense` |

## 對帳項目

| 項目 | 比對 | 落差時的原因判讀 |
| --- | --- | --- |
| Variables | 記帳 / 索引 / solver | 記帳≠索引：沒經過建模入口就進出索引；索引>solver：列出沒被任何限制式或目標式引用的變數（CPLEX 不收）與分組、sample，差額對不上再提示模型重建後索引沒清；索引<solver：有程式繞過框架直接加進 solver |
| Binary / Integer / Continuous | 記帳 / solver | Variables 同時有落差 → 同因；否則是 solver 端型別被改動 |
| Constraints | 記帳 / 索引 / solver | 同 Variables 的三種方向 |
| SpecialElements | 框架恆 0 / solver 合計 | 模型含框架不建立也不索引的元素，框架統計、IIS、取解都不涵蓋 |
| Objective | 記帳方向 / solver 方向 | 方向不一致、solver 有但框架沒建、框架建了但 solver 沒有 |

## 可見位置

| 階段 | 位置 |
| --- | --- |
| 建模 | `Solve()` 前兩行 `[模型統計對帳]` 對帳表 + `[MODEL_STATS_MATCH]`（info）或逐項 `[MODEL_STATS_MISMATCH]`（warn）；`engine.ModelStats`；`engine.ReconcileModelStats()` 隨時手動呼叫、不寫 log |
| tuning | `SolveMetrics.ModelStats` 進 Trial JSON；`-meta.csv` 新增 `modelStats` 區段（schema v2）；`OptExperiment.Run()` 結尾一行整批總結；ModelTuner facts 的「模型統計對帳」節與 production 行的 `ModelStats=` 欄位 |

主表 `.csv` 刻意不加欄：tuning guide 以 22 欄為抽取契約。

## 順帶修正

- 匯入模式的 `ObjectiveSense` 改依檔案內容同步（原本恆為 `Minimize`，也會讓疊加的軟性限制式 penalty 反號）——即 related spec 的 AC3。
- OptEngine 的 `CreateVar` / `AddLE` / `AddGE` / `AddEQ` / `Minimize` / `Maximize` 登記建模記帳，並同步 `ObjectiveSense`。
- 匯入會換掉整個 solver 模型，re-index 時把先前 Build*Vs 的變數記帳歸零。

## 實測發現（不在本次修正範圍）

- CPLEX `Ncols` 只算被限制式或目標式引用的變數；對這種變數呼叫 `AddMIPStart` 會丟 `object is unknown to IloCplex`。
- 已收進模型的欄在限制式被移除後仍留著（`ResetConstraint` 後部分重建，`Ncols` 不降），所以 Benders 式重置不會造成誤報。`ResetConstraint` 仍會清空引用紀錄，給不保留欄的 engine 用。
- `.mps` 沒有目標式方向欄位，CPLEX 把 maximize 寫成係數取負的 minimize，讀回來目標值反號。
- `Templates/FJSP_BASIC_BRICK` 有 189 個 `VariableB_Precede` 以全笛卡兒積宣告卻未被引用（含自身配對）。

## 測試

| 情境 | 測試 |
| --- | --- |
| 1 兩種統計完美符合 | `Unit/ModelStatsReconciliationTests.PerfectMatch_*`；`Integration/ModelStatsIntegrationTests.Authored_PerfectMatch`、`Imported_PerfectMatch_AndObjectiveSenseSynced`（.lp / .mps / .sav）、`ResetConstraint_PartialRebuild_StaysConsistent` |
| 2 有落差且 log 看得到 | 未引用變數、solver 端多出限制式、繞過 pool 的 primitive、特殊元素、目標式方向（Unit，Mock 注入）；未引用變數、繞過框架直接動 CPLEX、再次 Configuration 後索引殘留、匯入含 SOS 的檔（Integration，真 CPLEX） |
| tuning 階段 | `Experiment_RecordsModelStats`：Trial JSON 讀回、meta CSV `modelStats` 區段、實驗 log 總結 |

`SolverParamCoverageTests` / `SolverParamValueMatrixTests` 併入 `[Collection("Logging")]`：每次 `Solve()` 都寫 log，與讀 log 斷言的測試平行跑會互相污染。

## 未同步

- `AI-Modeling/dlls/` 的框架 DLL 尚未重建，AI-Modeling 專案要重建 DLL 後才有此功能。
- `AI-Modeling` 的 coding / tuning guide 尚未提到 `modelStats` 區段與 `[MODEL_STATS_MISMATCH]`。
