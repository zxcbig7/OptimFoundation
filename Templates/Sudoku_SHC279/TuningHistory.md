# Sudoku SHC 279 — Tuning History

本檔是 production solver baseline 的永久 provenance。`Experiments/*.json` 保存完整 Trial；本檔保存 AI 的 promotion／retain 決策，即使清除 `bin/` 仍可追溯。

每次修改 `Program.cs` 的 `productionBaseline`，必須先新增一筆紀錄，至少包含：experiment name、champion Trial label、before/after config diff、Status／objective／gap、穩健效能證據、決策理由，以及 promotion 後的 production 驗證結果。

## r2 — Retain initial baseline

| 欄位 | 紀錄 |
| --- | --- |
| 日期 | 2026-08-02 |
| Experiment | `Sudoku_SHC279-tuning-r2` |
| Baseline Trial | `Canonical \| r2-s{1,2,3}-baseline` |
| Candidate Trials | `emphasis=feasibility`、`probe=aggressive`，各 3 seeds |
| 方法 | 先排除 warm-up；seeds 1–3；跨 seed 輪替 variant 順序；shifted geometric mean（shift = 1 ms） |
| Eligibility | 全部 `Optimal`、objective = 0、gap = 0 |
| Baseline | 3.8088 ms |
| Feasibility emphasis | 4.1203 ms |
| Aggressive probe | 4.0251 ms |
| 決策 | **Retain**；沒有 candidate 可靠勝過 baseline，因此不修改 config |
| Config diff | 無 |
| Production 驗證 | build 0 warning / 0 error；`Optimal`；objective = 0；gap = 0；`ValidateRules` 通過 |

補充：既有 r1 固定讓 baseline 第一個執行，對毫秒級模型造成 cold-start／順序偏差，未作 promotion 依據；r2 取代它作為本輪正式證據。
