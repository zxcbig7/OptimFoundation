# TSP_MultiDimSet — Tuning History

> Phase 3 的**永久決策紀錄**，進版控。
> `bin/Experiments/*.json` 會被 `dotnet clean` 清掉，不能當唯一憑證。
> 每輪一節，格式為「假設 → 預測 → 實測 → 裁決」四段；**預測必須在跑實驗之前寫**。

---

## 契約區塊（整期固定，變更即重跑 S1 與 S2）

**進場情境：A**（`solveStatus = Optimal`）→ **主指標 = runtime `sgm`**
**症狀（使用者）**：想求解更快

### 停止契約（決定「什麼叫解完了」）

| 項目 | 生效值 | 來源 |
| --- | --- | --- |
| `TimeLimit` | 30 s | `Program.cs` productionBaseline 明設 |
| `MipGap` | 1e-4 | baseline 未設 → CPLEX 預設（log `EpGap=0.0001`） |
| `AbsoluteMipGap` / `NodeLimit` / `IntegerSolutionLimit` | null | CPLEX 預設 |
| 容差 `OptimalityTol` / `FeasibilityTol` | 1e-6 | CPLEX 預設（log 確認） |

### 環境契約（S1 sizing 定版）

| 項目 | 值 | 狀態 |
| --- | --- | --- |
| `Threads` | **1** | ✅ **S1 定版 2026-08-09**（明顯勝者，見下） |
| `ParallelMode` | 1（決定論） | ✅ 已明設 |
| `MemoryLimitMb` | 2048 | 框架預設；注意設它時會強制 `NodeFileStrategy = 0` |

機器：實體核心 12、邏輯處理器 16。sizing 候選 12 / 11 / 10（NEVER 用邏輯核心數）。

**S1 sizing 實測**（experiment `TSP_MultiDimSet-sizing` 與 `-sizing-b`，warm-up 已排除，obj 全部 = 14）：

| Threads | s1 | s2 | s3 | sgm(shift 1ms) | max/min |
| --- | --- | --- | --- | --- | --- |
| 12 | 6 | 10 | 6 | 7.14 | 1.67 |
| 11 | 6 | 10 | 6 | 7.14 | 1.67 |
| 10 | 7 | 9 | 5 | 6.83 | 1.80 |
| **1** | **3** | **3** | **3** | **3.00** | **1.00** |

**候選集擴充的理由**：規範的候選是「實體核心數 / −1 / −2」（12/11/10），但現行 baseline 是 `Threads = 1`，比候選中最低者更低。規範「平手時取最低者以壓低 θ」的邏輯直接指向它，故補測後納入比較。

**判定**：`Threads = 1` 比最佳多執行緒候選（t10, sgm 6.83）快 **56%**，遠超 10% 門檻 → 依規範「有明顯勝者 → 取之，凍結」。且它的三個 seed 完全一致（變異 0），對後續量測最有利。

**機制**：本模型僅 24 變數 / 26 限制式，root LP 即解完；多執行緒的 ramp-up 與同步開銷是純損失，並引入執行緒時序變異。這與規範 §2.0.2「threads 高會抬高 θ」一致。

### 量測契約

| 項目 | 狀態 |
| --- | --- |
| experiment 期間 export | ✅ 全關 —— exp 分支未呼叫 `.UseConfig(() => projectConfig)`，框架預設 solver log / LP / MPS / Sol 全 OFF |
| production 期間 export | `ExportLP = true`（promotion 驗證時照此設定） |
| 解正確性驗證 | ✅ 有 `Solution/TSP_MultiDimSetSolution.cs` 的 `ValidateRules`，且已實測會 throw |
| `ParallelMode` | ✅ 明設 1，不依賴 CPLEX 預設 |
| dynamic search | ✅ 每輪確認 solver log 無停用 warning（框架只用 informational `MIPInfoCallback`） |

### Phase 2 結果基線（§0.1.1 不變式）

```text
phase2Status    = Optimal
phase2Objective = 14
phase2Bound     = 14
phase2Gap       = 0
verifiedOn      = production
```

**情境 A 的不變式**：每個 trial 與 promotion 後的 production，objective **必須嚴格等於 14**。偏離即淘汰／回退。

### 總預算（§7.2 停止條件 F）

單次求解遠低於 1 s（整個 process 含載入 < 2 s）。預估 sizing 9 + R0 5 + 每輪 ≤ 20 → 三輪合計 < 80 次求解，**總計 < 5 分鐘**。預算充裕，條件 F 預期不會觸發。

### 已知的規範引用缺口（finding，不中斷流程）

`SKILL.md` 五處引用「規範 §0.0.1」（情境 A/B/C 判定、`verifiedOn` 語意、No-incumbent 剖面、θ 單位隨主指標變、停止條件 I），但 `solver-tuning-guide.md` **目前沒有 §0.0.1 這一節**。本輪依 `SKILL.md` 內嵌的情境表執行，判定結果不受影響。

---

## R0 校準 — 2026-08-09

**experiment**：`TSP_MultiDimSet-R0`（warm-up 已排除）
**環境**：`Threads=1` / `ParallelMode=1`（S1 定版）
**tuning seeds**：1–5　**holdout seeds**：6, 7, 8（全程未使用）

### 實測

| Trial | Status | obj | bound | gap | runtime (ms) |
| --- | --- | --- | --- | --- | --- |
| r0-s1 | Optimal | 14 | 14 | 0 | 3.1832 |
| r0-s2 | Optimal | 14 | 14 | 0 | 10.6966 |
| r0-s3 | Optimal | 14 | 14 | 0 | 4.0369 |
| r0-s4 | Optimal | 14 | 14 | 0 | 3.7915 |
| r0-s5 | Optimal | 14 | 14 | 0 | 6.2372 |
| probe-mipgap0 | Optimal | 14 | 14 | 0 | 4.1050 |

**§0.1.1 不變式**：6 個 trial 的 objective 全部 = 14 = `phase2Objective` ✅

### 產出 A · 雜訊地板 θ

- 主指標：runtime `sgm`（情境 A）
- `sgm`（shift 1 ms）= **5.11 ms**
- `max / min` = 10.6966 / 3.1832 = **3.36**
- **θ = (max − min) / sgm = 7.5134 / 5.11 = 147 %**

無 timeout，PAR10 不適用。

### 產出 B · 瓶頸剖面 → **Variability-dominated**

判準逐項：

| 判準 | 實測 | 判定 |
| --- | --- | --- |
| `max/min > 2` | 3.36 | ✅ 符合 |
| 無明顯瓶頸 | 6 個 trial 合計只產生 **2 個軌跡點**，`t_feas` / `r_primal` / `Δbound` / `t_stall` 四個量**皆無法計算** | ✅ 符合 |

軌跡兩點（唯一可得的觀測）：

```text
r0-s2  t=8.7691ms  obj=16  bound=14  gap=12.50%
r0-s4  t=3.2328ms  obj=18  bound=14  gap=22.22%
```

★ 兩點的 `bound` 都已經是 **14**——**LP 鬆弛在 root 就等於整數最佳解**，dual 側完全沒有工作可做；求解全程只在把 incumbent 從 16/18 拉到 14。這個實例對策略旋鈕沒有作用空間。

### 產出 C · 契約健檢探針（finding）

`MipGap = 0` 探針：obj = 14、runtime 4.105 ms，與 baseline **完全相同**。

→ **現行停止契約沒有訂錯**。`MipGap = 1e-4` 已經取得真最佳解 14，不存在「契約太鬆導致常態交付次佳解」的問題。無需使用者拍板。

### 裁決

**retain** — 保留現行 `productionBaseline`（`TimeLimit=30` / `Threads=1` / `ParallelMode=1` / `Seed=1`），未 promotion。

**停止原因：規範 §7.1 條件 A（早停 · 雜訊主導）**。R0 剖面判為 Variability-dominated，依規範不進 S3，S2.5（CPLEX 內建 tune）與後續策略輪一併不執行。

**理由**：θ = 147 % 意味著任何策略旋鈕要勝出，runtime 必須改善超過 147 %——而 baseline 本身只有 3–11 ms，改善 147 % 在數學上不可能。求解時間已逼近計時器解析度（1 ms 量化 ≈ 33 % 誤差），此規模量不出可靠改善。

**這不是失敗，是規範設計的正確行為**：對這個規模的模型，tuning 的正確結論就是「不要 tuning」。

### 已否證清單（累積，後續輪次不重試）

| 方向 | 證據 | 否證於 |
| --- | --- | --- |
| `Threads` 10 / 11 / 12 | 全部慢於 `Threads=1` 逾 100 %，且引入時序變異 | S1 |
| **策略旋鈕整體**（對本實例） | LP 鬆弛 = 整數最佳解（bound 在 root 即為 14），無 dual 側工作可調；θ=147 % 高於任何可能改善 | R0（早停 A） |

### 若要讓本專案值得 tuning

需要**放大 instance**（例如 20+ 節點的 TSP，讓求解進入秒級），那屬於改 `Data/*.csv`——**不是 tuning，是換資料**，依規範 §1 不在 Phase 3 範圍內，由使用者決定。

---
