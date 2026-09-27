# ModelTuner — TuningHistory

> Phase 3 決策紀錄（solver-tuning-guide §6），檔案模式：模型來自 `Instances/` 的凍結模型檔。
> 契約區塊整期固定 → R0 一節 → 每輪一節，只追加，NEVER 改寫歷史節。
> 每輪格式照 guide §6.2（目標 / 依據 / 假設 / 預測在跑之前寫；TUNING-FACTS 貼 `dotnet run -- facts <N>` 產出的檔，NEVER 手填數字）。

## 契約區塊（整期固定，變更即重跑 §2.3 與 §3.0）

### 模型凍結契約（檔案模式專屬，取代 Phase 2 的「model chain 唯讀」）

| 項目 | 值 | 備註 |
| --- | --- | --- |
| instances.lock | <commit 或日期> | `dotnet run -- lock` 產生；之後每次執行自動比對 SHA-256，不符即中止 |
| 模型檔來源 | <哪個專案 / 系統、何時匯出> | 優先 `.sav`；`.lp` / `.mps` 係數經十進位截斷 |
| 正確性證據 | <來源專案的解驗證紀錄 / ValidateRules 結果 / 小 instance 驗證> | 檔案模式沒有 ValidateRules，這格空著 = 不准進場（§0.0） |
| tune instances | <tune/xxx.sav> | |
| holdout instances | <holdout/xxx.sav，或「無 → 改用 holdout seeds 66,77,88」> | NEVER 用來選 config |

### 停止契約

| 項目 | 值 | 備註 |
| --- | --- | --- |
| `MipGap` | | 使用者定案日期 |
| `TimeLimit` | | |

### 環境契約（§2.3 sizing 定版）

| 項目 | 值 | 備註 |
| --- | --- | --- |
| `Threads` | | sizing 結果與理由 |
| `ParallelMode` | 1 | 決定論，換取可比性 |
| `MemoryLimitMb` / `NodeFileStrategy` | 未設 | |

### 量測契約

| 項目 | 值 |
| --- | --- |
| 進場情境（§0.0.1） | A `Optimal` / B `Feasible` / C `TimeLimit` |
| 主指標（§3.0 產出 A） | runtime `sgm` / endGap 平均 / 找到解的 seed 數 + `t_feas` |
| θ | <值 + 單位，抄自 `facts 0` 的 θ 行> |
| seeds | tuning 11,22,33,44,55；holdout 66,77,88 |
| warm-up / 順序輪替 | runner 自動（`TuningRound`） |
| export（experiment 期間） | 全關 |
| 解正確性驗證 | 無 ValidateRules → 每輪看 `facts` 的結果不變式表（bound 越線 / Optimal 分散） |
| dynamic search | 每輪看 `facts` 的 log 掃描結果 |

### 參考結果基線（§0.1.1；S0 時 `dotnet run` 的 production 結果，逐 instance）

| instance | status | objective | bound | gap | sense | verifiedOn |
| --- | --- | --- | --- | --- | --- | --- |
| | | | | | | |

---

## R0 — YYYY-MM-DD（校準，不計入輪次）

- experiment：`ModelTuner-tuning-r0`；archive：`Experiments/ModelTuner-tuning-r0.{csv,-meta.csv,json,-trajectory.csv}`
- TUNING-FACTS：貼 `bin/.../Experiments/ModelTuner-tuning-r0-facts.md` 的 BEGIN…END 區塊
- 主指標與 θ：
- 瓶頸剖面（§2.1）：
- 契約健檢探針（§3.0 產出 C）：
- R0 出口 gate：

## S2.5 — CPLEX 內建 tune

- 指令：`dotnet run -- cplex-tune <總秒數>`；archive：`Experiments/ModelTuner-cplex-tune-<時間戳>.prm`
- 建議參數（每顆拆成獨立 variant，NEVER 直接 promote）：
