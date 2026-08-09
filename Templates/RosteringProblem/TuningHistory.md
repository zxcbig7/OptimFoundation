# RosteringProblem — Tuning History

> Phase 3 的**永久決策紀錄**，進版控。
> `bin/Experiments/*.json` 會被 `dotnet clean` 清掉，不能當唯一憑證。
> 每輪一節，格式為「假設 → 預測 → 實測 → 裁決」四段；**預測必須在跑實驗之前寫**。

---

## 契約區塊（整期固定，變更即重跑 S1 與 S2）

**進場情境：A**（`solveStatus = Optimal`）→ **主指標 = runtime `sgm`**
**症狀**：使用者未指定敘述，由 skill 實跑 triage —— 模型正確且 `Optimal`，屬「已解完、想更快」。

### 停止契約（決定「什麼叫解完了」）

| 項目 | 生效值 | 來源 |
| --- | --- | --- |
| `MipGap` | 0.03 | `Program.cs` productionBaseline 明設 |
| `TimeLimit` | 100 s | 同上（實際求解 ~2 s，未觸及） |
| `AbsoluteMipGap` / `NodeLimit` / `IntegerSolutionLimit` | null | CPLEX 預設 |
| 容差 `OptimalityTol` / `FeasibilityTol` | 1e-6 | CPLEX 預設 |

★ `MipGap = 0.03` 而實測 `MIPGap = 2.94 %`——求解是**被 gap 容差終止**的，不是解到真最佳。`BestBound = 3.3` 與 `ObjVal = 3.4` 之間有 0.1 的空間，R0 的契約健檢探針會查這件事。

### 環境契約（S1 sizing 定版）

| 項目 | 值 | 狀態 |
| --- | --- | --- |
| `Threads` | 10（進場值） | ⏳ 待 S1 sizing 定版 |
| `ParallelMode` | **未明設**（依 CPLEX 預設） | ⏳ S1 將明設為 1（決定論），否則可重現性隨 DLL 版本浮動 |
| `MemoryLimitMb` | 2048 | 框架預設 |

機器：實體核心 12、邏輯處理器 16。sizing 候選 12 / 11 / 10（NEVER 用邏輯核心數）。

### 量測契約

| 項目 | 狀態 |
| --- | --- |
| experiment 期間 export | ✅ 全關 —— exp 分支未呼叫 `.UseConfig(() => projectConfig)`，框架預設 solver log / LP / MPS / Sol 全 OFF |
| production 期間 export | LP / MPS / Sol 全開（promotion 驗證時照此設定，時間會比 experiment 長） |
| 解正確性驗證 | ✅ `Solution/RosteringProblemSolution.cs` 的 `ValidateRules` 逐條驗 10 條限制式（17 個 throw 點），key 以 `new VariableB_*{...}.ToString()` 建構 |
| `ParallelMode` | ⏳ 待 S1 明設 1 |
| dynamic search | ✅ 已確認啟用（solver log：`MIP search method: dynamic search.`） |

### Phase 2 結果基線（§0.1.1 不變式）

```text
phase2Status    = Optimal
phase2Objective = 3.4
phase2Bound     = 3.3
phase2Gap       = 2.94 %
verifiedOn      = production
```

**情境 A 的不變式**：每個 trial 與 promotion 後的 production，objective **必須嚴格等於 3.4**。偏離即淘汰／回退。

### 總預算（§7.2 停止條件 F）

單次求解 ~2 s（production wall time 4.6 s 含 JIT / 載入 / 三種 export）。
預估：sizing 9 + R0 5 + 每輪 ≤ 20 × 3 輪 = 74 次求解 ≈ **5 分鐘**。預算充裕。

### 已知的規範引用缺口（finding，不中斷流程）

`SKILL.md` 五處引用「規範 §0.0.1」，但 `solver-tuning-guide.md` 目前沒有該節。本輪依 `SKILL.md` 內嵌的情境表執行，情境 A 判定不受影響。

---

## S1 · 環境定版 sizing — 2026-08-09

**experiment**：`RosteringProblem-sizing-b`（`-sizing` 那批因同名 append 污染，見下方 finding）
**設定**：`ParallelMode = 1`，掃 `Threads` 12/11/10 × 3 seeds，順序輪替，warm-up 排除

| Threads | 樣本 (ms) | sgm(shift 1s) | 組內 max/min |
| --- | --- | --- | --- |
| 12 | 3620, 3082, 2491 | 3038 | 1.45 |
| 11 | 3303, 3100, 1769 | 2656 | **1.87** |
| 10 | 2615, 3072 | 2837 | 1.17 |

`s1-t10` 因停在不同解（obj 3.3）而排除，故 t10 只有 2 個樣本。

**判定：`Threads = 10`（維持現行值），並明設 `ParallelMode = 1`。**

理由：組間最大差異 14.4%（t11 vs t12），但**組內變異高達 87%**——組間差異被雜訊完全淹沒，三個候選無法區分，依規範取最低者。

### finding · 同名 experiment append（規範 §3.3 實際踩到）

`-sizing` 第一次執行時 grep pattern 寫錯（model 名是 `Canonical` 不是 `RosteringProblem`），誤判為未執行而重跑，導致同名 experiment **append 成 20 trials**，上半批含 cold-start 明顯較慢。已改用新名 `-sizing-b` 重跑取得乾淨數據。**規範這條警告是真的。**

---

## R0 校準 — 2026-08-09

**experiment**：`RosteringProblem-R0`
**環境**：`Threads=10` / `ParallelMode=1`（S1 定版）
**tuning seeds**：1–5　**holdout seeds**：6, 7, 8（全程未使用）

### 實測

| Trial | Status | obj | gap | runtime (ms) |
| --- | --- | --- | --- | --- |
| r0-s1 | Optimal | **3.3** | **0.00%** | 3547 |
| r0-s2 | Optimal | 3.4 | 2.94% | 2765 |
| r0-s3 | Optimal | 3.4 | 2.94% | 3110 |
| r0-s4 | Optimal | 3.4 | 2.94% | 2444 |
| r0-s5 | Optimal | 3.4 | 2.94% | 3636 |
| probe-mipgap0 | Optimal | **3.3** | **0.00%** | 3880 |

### 產出 A · 雜訊地板 θ

用**停在同一解（3.4）的 4 個樣本**計算（停止點不同則 runtime 不可比）：

- `sgm`（shift 1 s）= **2964 ms**
- `max / min` = 3636 / 2444 = **1.49**
- **θ = (3636 − 2444) / 2964 = 40.2 %**

### 產出 B · 瓶頸剖面 → **Dual-bound**

| 量 | 值（s2/s3/s4/s5） | 判讀 |
| --- | --- | --- |
| `t_feas` | 263–285 ms | 首個可行解極快 |
| `r_primal` | **9.3 %**（平均） | primal 側僅佔 9%，**約 90% 時間在推 bound** |
| `Δbound` | 0.28–0.30（3.00 → 3.30） | dual 側有進展 |
| `t_stall` | 佔全程 38–73% | bound 中途即停止改善 |

`max/min = 1.49 < 2` → **不是** Variability-dominated；軌跡點充足（8–18 點/trial）→ 剖面可判定。

**候選類**（規範 §2.2 Dual-bound）：`Emphasis = 3`（BESTBOUND）→ 各 cuts → `CutsFactor` / `CutPasses` → `Probe` → `Symmetry`。

### 產出 C · 契約健檢探針（finding，需使用者拍板）

`MipGap = 0` 探針：**obj = 3.3、gap = 0%、3880 ms**。

- **真最佳解是 3.3**，現行契約 `MipGap = 0.03` 讓 production 常態交付 **3.4**
- 品質損失：**2.94 %**
- 取得真最佳的代價：約 **+1.1 秒**（2765 → 3880 ms）

★ 這與 `HospitalRostering_Generator` 的發現同型。**依規範記成 finding、不自行變更契約**，是否收緊 `MipGap` 由使用者決定。

### finding · 情境 A 不變式的定義缺陷

規範 §0.1.1 對情境 A 要求「objective **嚴格相等**」，但本專案 `MipGap = 0.03 > 0`，**同契約下解本來就不唯一**——`r0-s1`（決定論、可重現）停在 3.3，其餘停在 3.4。

把 3.3 當「違反不變式」而淘汰是錯的：3.3 比 3.4 **更好**（min 問題），它是解到了真最佳。

**本輪採用的實際判準**：objective **不得比基線差**（≤ 3.4），且 runtime 比較只在**同一停止點的樣本間**進行。規範的不變式定義應補上「`MipGap > 0` 時放寬為不得變差」，否則會誤淘汰更優解。

---

## S2.5 · CPLEX 內建 tuning tool 基準 — 2026-08-09

**工具**：CPLEX Interactive Optimizer 22.1.1.0（`C:\IBM\ILOG\CPLEX_Studio2211`，可用）
**輸入**：`bin/Debug/net8.0/Models/RosteringProblem_LP_2026-08-09_21-35-48.lp`（3.0 MB）
**設定**：`timelimit 100`、`mip tolerances mipgap 0.03`、`tune timelimit 240`、**`tune repeat 3`**（permutation 穩健化）

```text
Default test: Time = 8.36 sec.
Best test: 'defaults'  Time = 8.36 sec.
Fixed and tuned parameter settings:   ← 空
Tuning - Complete.  Time = 43.52 sec.
```

**結果：CPLEX 自己找不到任何比預設更好的參數組合**。測試過的 variant（含 `no_Gomory_cuts`、`no_heuristic`）皆未勝出。

→ 無建議可拆成 S3 的 variant。此結果**強烈預示 S3 也會 retain**，但依規範仍照剖面執行一輪取得自己的證據。

★ 命令是 `tools tune`（不是 `tune`）——後者在 Interactive Optimizer 中不存在，規範 §3.6 的指令範例需修正。

---

## R1 — 2026-08-09

**假設**：R0 剖面為 Dual-bound（`r_primal` 僅 9.3%、約 90% 時間在推 bound，`t_stall` 佔 38–73%）
→ bound 的推進速度決定何時觸發 `gap < 3%` 而停止。`Emphasis = 3`（BESTBOUND）是 CPLEX 官方語意中
**唯一專門加大 best bound 推進力度**的設定，應能更快讓 bound 到 3.3。

**預測**（跑實驗之前寫死）：

1. `Emphasis = 3` 的 `Δbound` 到達 3.30 的時點會早於 baseline
2. **但 runtime 的 sgm 改善不會超過 θ = 40.2%** → 預期裁決為 **retain**
3. 理由：S2.5 的 CPLEX 內建 tune 已在同契約下測過多組設定並回報 `Best test: defaults`；
   且 R0 組內變異（max/min 1.49）本身就吃掉大部分可偵測空間

**實測**：experiment `RosteringProblem-tuning-r1`（⚠️ 該名稱已被專案原有 exp 分支使用過，本次為 append，**只採本輪 11 筆**）

| seed | baseline runtime / obj | Emphasis=3 runtime / obj |
| --- | --- | --- |
| s1 | 3708 / **3.3** | 5198 / **3.3** |
| s2 | 2679 / 3.4 | 2247 / **3.3** |
| s3 | 3112 / 3.4 | 2821 / 3.4 |
| s4 | 2486 / 3.4 | 5228 / **3.3** |
| s5 | 3644 / 3.4 | 3371 / 3.4 |

**主指標比較**（只在同一停止點的樣本間，shift 1 s）：

| 停止點 | baseline sgm | Emphasis=3 sgm | 差異 |
| --- | --- | --- | --- |
| obj 3.4 | 2956（n=4） | 3087（n=2） | **慢 4.4 %** |
| obj 3.3 | 3708（n=1） | 4006（n=3） | 慢 8.0 % |

**裁決：retain** — 主指標（runtime sgm）**未改善**（實際慢 4.4%），差異遠在 θ = 40.2% 內 → 依規範 §4.4 視同平手。

**預測命中檢核**：

| 預測 | 結果 |
| --- | --- |
| ① Δbound 到 3.30 更早 | 部分成立（見下方非預期發現） |
| ② runtime 改善不超過 θ → retain | ✅ **命中**（實際為慢 4.4%） |
| ③ CPLEX 內建 tune 已測過、θ 吃掉可偵測空間 | ✅ 印證 |

**非預期發現（不影響本輪裁決，但有價值）**：`Emphasis = 3` 有 **3/5** 個 seed 解到真最佳 3.3，baseline 只有 **1/5**。這符合 BESTBOUND 的官方語意（加大 bound 推進力度 → 更常證到最佳），但**它換來的是解品質而非速度**——在情境 A（主指標 runtime）下不構成勝出。

★ 若使用者採納 R0 的契約 finding、把 `MipGap` 收到 0，主指標會變成「證到最佳的 runtime」，屆時 `Emphasis = 3` **應重新評估**——它在那個契約下可能真的有價值。本輪不做這個假設。

### 已否證清單（累積）

| 方向 | 證據 | 否證於 |
| --- | --- | --- |
| `Threads` 12 / 11 | 組間差異 14.4% 被組內變異 87% 淹沒，無法區分 | S1 |
| **CPLEX 內建 tune 的整個搜尋空間** | `Best test: defaults`，含 `no_Gomory_cuts` / `no_heuristic` 皆未勝出 | S2.5 |
| `Emphasis = 3`（對 runtime） | 同停止點下慢 4.4%，遠在 θ 內 | R1 |

---

## R2 — 2026-08-09

**假設**：剖面仍為 Dual-bound。`Emphasis = 3` 已否證（它改變了停止點分布而非加速），
下一個候選類是**切割平面**——規範 §2.2 Dual-bound 的第二順位。bound 從 3.00 爬到 3.30 是求解主體，
加強 cuts 應能讓 root bound 更緊、更早觸發 `gap < 3%`。選 `GomoryCuts = 2`（積極）為本輪唯一變動。

**預測**（跑實驗之前寫死）：

1. `GomoryCuts = 2` 的 root bound 會高於 baseline 的 3.00 起點
2. **但 runtime sgm 改善不會超過 θ = 40.2%** → 預期 **retain**
3. 理由：S2.5 的 CPLEX tune 測過 `no_Gomory_cuts`（關掉）沒有變好，代表 Gomory 在此模型上**已在預設強度下發揮作用**；
   加強它通常只是把時間從 B&B 移到 cut 生成，淨效益接近零

**實測**：experiment `RosteringProblem-tuning-r2`（乾淨，無 append）

| seed | baseline runtime / obj | GomoryCuts=2 runtime / obj |
| --- | --- | --- |
| s1 | 3641 / **3.3** | 2590 / 3.4 |
| s2 | 2795 / 3.4 | 4082 / **3.3** |
| s3 | 3102 / 3.4 | 2835 / 3.4 |
| s4 | 2428 / 3.4 | 2199 / 3.4 |
| s5 | 3562 / 3.4 | 3582 / 3.4 |

**主指標比較**（同停止點 obj 3.4，各 n=4，shift 1 s）：baseline `sgm = 2950` vs GomoryCuts=2 `sgm = 2769` → **改善 6.1 %**

**裁決：retain** — 改善 6.1 % 遠低於 θ = 40.2 %，依規範 §4.4 視同平手。

**預測命中檢核**：② runtime 改善不超過 θ ✅ **命中**；③ CPLEX tune 對 Gomory 的判讀 ✅ 印證。

---

## R3 — 2026-08-09

**假設**：剖面仍為 Dual-bound。cuts 與 emphasis 兩個方向已否證。本模型有 **16 名員工**，
除 `CrossGroup` 規則外彼此高度同質——這是**對稱性**的典型來源：交換兩名主責班別相同的員工會得到
等價解，B&B 因此要探索大量等價分支。規範附錄 A 明載 `Symmetry` 對「排班、指派這類同質資源的題目值得試」。
選 `Symmetry = 3`（中等強度）為本輪唯一變動。

**預測**（跑實驗之前寫死）：

1. `Symmetry = 3` 會減少等價分支的探索，bound 推進應更有效率
2. **這是三輪中最有機會的一顆**——但仍預期 runtime sgm 改善**不超過 θ = 40.2%** → 預期 **retain**
3. 理由：S2.5 的 CPLEX 內建 tune 已在同契約下搜尋過參數空間並回報 `Best test: defaults`；
   若 `Symmetry` 真有數量級效益，內建 tune 應該會找到它

**實測**：experiment `RosteringProblem-tuning-r3`（乾淨，無 append）

| seed | baseline runtime / obj | Symmetry=3 runtime / obj |
| --- | --- | --- |
| s1 | 3506 / **3.3** | 3414 / **3.3** |
| s2 | 2654 / 3.4 | 2680 / 3.4 |
| s3 | 3051 / 3.4 | 3099 / 3.4 |
| s4 | 2526 / 3.4 | 2487 / 3.4 |
| s5 | 4047 / 3.4 | 3959 / 3.4 |

**主指標比較**（同停止點 obj 3.4，各 n=4，shift 1 s）：baseline `sgm = 3029` vs Symmetry=3 `sgm = 3019` → **改善 0.33 %**

（s1 兩者同停在 3.3：3506 → 3414，改善 2.6 %，同樣遠低於 θ。）

**裁決：retain** — 改善 0.33 %，兩組數據幾乎逐點重疊。

**預測命中檢核**：② 改善不超過 θ ✅ **命中**；③ CPLEX 內建 tune 未找到 `Symmetry` ✅ 印證。對稱性消除在此模型上**沒有可量測效益**——可能因為 `CrossGroup` / `PreAssign` 已經打破了員工間的對稱性。

---

## 本輪 tuning 收尾 — 2026-08-09

## 停止原因：規範 §7.1 **條件 D（連續 3 輪無實質改善）**

| 輪次 | 候選（一輪一顆） | 主指標改善 | 裁決 |
| --- | --- | --- | --- |
| R1 | `Emphasis = 3`（BESTBOUND） | **−4.4 %**（變慢） | retain |
| R2 | `GomoryCuts = 2` | +6.1 % | retain |
| R3 | `Symmetry = 3` | +0.33 % | retain |

三輪皆遠低於 θ = 40.2 %。

## 最終裁決：**retain**

`productionBaseline` **一個欄位都沒有變更**：

```csharp
MipGap = 0.03
TimeLimit = 100
Threads = 10
```

未執行 S4（無 champion）、未執行 S5 promotion。`promotionVerified` 維持 false。

## 已否證清單（累積，後續輪次不重試）

| 方向 | 證據 | 否證於 |
| --- | --- | --- |
| `Threads` 12 / 11 | 組間差異 14.4 % 被組內變異 87 % 淹沒 | S1 |
| **CPLEX 內建 tune 的整個搜尋空間** | `Best test: defaults`（含 `no_Gomory_cuts` / `no_heuristic`） | S2.5 |
| `Emphasis = 3` | 同停止點慢 4.4 % | R1 |
| `GomoryCuts = 2` | 改善 6.1 %，遠低於 θ | R2 |
| `Symmetry = 3` | 改善 0.33 %，逐點重疊 | R3 |

## 交還使用者的兩個決定

### ① 契約：`MipGap` 要不要收到 0？（R0 產出 C）

- 真最佳解 **3.3**，現行契約常態交付 **3.4**，品質損失 **2.94 %**
- 取得真最佳的代價：約 **+1.1 秒**（2765 → 3880 ms）
- 若收緊，主指標會從「達到 3 % gap 的時間」變成「證到最佳的時間」，**θ 與剖面都要重跑**，且 `Emphasis = 3` 值得重新評估（R1 觀察到它 3/5 個 seed 解到 3.3，baseline 只有 1/5）

### ② 效能已達旋鈕極限

三輪 + CPLEX 內建 tune 都找不到 > θ 的改善。要再快只剩**模型層手段**（規範 §2.5：變數型態、限制式聚合、收緊界限、reformulation），那是新一輪 Phase 1，**由使用者決定要不要走**，本階段 NEVER 自行升級。
