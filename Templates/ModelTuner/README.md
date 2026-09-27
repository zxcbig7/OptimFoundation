# ModelTuner

吃**既有模型檔**（`.sav` / `.lp` / `.mps`，含各自的 `.gz` / `.bz2`）做 Phase 3 solver tuning 的專案殼。

Phase 3 的規範（`AI-Modeling/.claude/skills/tuning/solver-tuning-guide.md`，下稱 guide）預設模型是 Phase 2 用 C# 組出來的；ModelTuner 把「模型」換成 `Instances/` 裡凍結的檔案，其餘流程——R0 校準、一輪一顆、θ 門檻、hold-out、promotion 閉環、`TuningHistory.md`——照 guide 走，不另立規則。

適用：模型由別的系統產生（只拿得到模型檔）、要跨多個 instance 調同一組參數、或不想為了調參動題目專案。

## 目錄

```text
ModelTuner/
├── Program.cs ← productionBaseline（唯一）+ R<N> 區塊；tuning 期間唯一會改的程式
├── Tuning/ ← runner 基礎設施（tuning 期間唯讀）
│   ├── TunerWorkspace.cs ← instance 掃描、instances.lock 指紋凍結、archive 防呆
│   ├── TuningRound.cs ← seeds × instances 展開、warm-up、順序輪替、hold-out
│   ├── RoundFacts.cs ← TUNING-FACTS、主指標彙總與 θ、結果不變式、dynamic search 掃描
│   ├── CplexTuner.cs ← CPLEX 內建 tuning tool（TuneParam）
│   └── TunerCli.cs ← 參數解析與用法
├── Instances/
│   ├── tune/ ← 調參用模型檔
│   └── holdout/ ← hold-out 模型檔（可空；空的話 hold-out 改用 holdout seeds）
├── instances.lock ← S0 產生，模型檔 SHA-256
├── Experiments/ ← 每輪原始證據的永久 archive（runner 自動複製）
└── TuningHistory.md ← 契約區塊 + 每輪決策紀錄
```

## 快速開始

```powershell
# 0. 放模型檔（優先 .sav：.lp / .mps 是文字格式，係數經十進位截斷）
Copy-Item ..\RosteringProblem\bin\Debug\net8.0\Models\RosteringProblem_SAV_*.sav .\Instances\tune\

# 1. S0 正確性確認：用 productionBaseline 解一次，把每個 instance 的結果抄進契約區塊「參考結果基線」
dotnet run

# 2. S0 凍結：寫 instances.lock（之後每次執行都比對，模型檔被換掉就中止）
dotnet run -- lock

# 3. R0 校準（Program.cs 已內建 R0 區塊）
dotnet run -- exp 0

# 4. S2.5（選用）：CPLEX 內建 tune，總預算 600 秒
dotnet run -- cplex-tune 600

# 5. R1..RN：在 Program.cs 加 R<N> 區塊後
dotnet run -- exp 1

# 6. hold-out：champion 選定後
dotnet run -- holdout 1 r1-Emphasis=2

# 7. promotion 後 production 驗證
dotnet run
```

離開碼：`0` 成功、`1` production 有 instance 沒解、`2` 參數或定義錯誤、`3` 契約防呆擋下（lock 不符、round 已 archive）。

## 對應 guide 的 S0–S5

| guide 步驟 | ModelTuner 做法 |
| --- | --- |
| S0-1 正確性 gate | 檔案模式沒有 `dotnet build` 模型、也沒有 `ValidateRules`：進場前 MUST 在契約區塊寫明模型檔的**正確性證據**（來源專案的解驗證紀錄），再跑 `dotnet run` 確認 Status 落在 A / B / C 三態 |
| S0-3 Phase 2 結果基線 | 改叫「參考結果基線」，逐 instance 抄 `dotnet run` 印出的 `[Production]` 行 |
| S0-4 契約凍結 | 停止 / 環境 / 量測契約同 guide；另加**模型凍結契約**：`dotnet run -- lock` |
| S1 sizing | 同 guide，改 `productionBaseline.Threads` 後跑 production 比較 |
| S2 R0 | `dotnet run -- exp 0`；θ、彙總、不變式由 `facts` 自動算好 |
| S2.5 內建 tune | `dotnet run -- cplex-tune [秒]`，直接吃 `Instances/tune` 全部檔案，不必再匯出 LP 到 Interactive Optimizer |
| S3 R1..RN | Program.cs 加 `else if (roundNo == N)` 區塊 → `dotnet run -- exp N` |
| S4 hold-out | `dotnet run -- holdout N <champion label>`；有 `Instances/holdout` 就升級成 train / test instance 分離 |
| S5 promotion | champion 設定寫回 `productionBaseline` + provenance 註解 → `TuningHistory.md` → `dotnet run` 驗證 |

## 可寫白名單（檔案模式）

tuning 期間只准動下列四處，其餘（`Tuning/`、csproj、`Instances/`、`instances.lock`）一律唯讀：

| # | 可寫區域 | 限制 |
| --- | --- | --- |
| 1 | `Program.cs` 的 `productionBaseline` 與其上方 provenance 註解 | promotion 唯一寫回點 |
| 2 | `Program.cs` exp 區段的 `R<N>` 區塊 | 一律 `productionBaseline.Clone()` 起手；已執行的區塊跑完後 materialize 成字面值 initializer，NEVER 改寫或刪除 |
| 3 | `TuningHistory.md` | 只追加 |
| 4 | `Experiments/` | 只由 runner 自動複製（`exp` / `holdout` / `cplex-tune`），NEVER 手改 |

**換模型檔 = 契約變更**：`lock` 會拒絕覆寫，要人手動刪 `instances.lock` 再 lock，並從 S1 / R0 重來。

交付 diff 檢查：`git diff --name-only` 只應出現 `Program.cs`、`TuningHistory.md`、`Experiments/<Project>-tuning-r<N>*`、`Experiments/<Project>-cplex-tune-*.prm`。

## runner 自動做的事

| 項目 | 行為 | guide |
| --- | --- | --- |
| seed 展開 | 每顆 config × tuning seeds（11,22,33,44,55）；label 自動加 `-s<seed>`，seed 是共同因子不是 variant | §3.4 |
| warm-up | 正式 cell 前先跑一次 baseline（第一個 instance、第一個 seed），不進 experiment | §3.4 |
| 順序輪替 | 第 k 個 seed 的 variant 順序旋轉 k 格，不固定讓 baseline 承擔 cold-start | §3.4 |
| 同名防呆 | archive 已有 `r<N>` → 拒跑（archive 不可變）；bin 殘留同名檔 → 刪除後再跑，確保 archive 的是本輪產物 | §3.3 |
| archive | 跑完立刻把 `.csv` / `-meta.csv`（+ `-trajectory.csv`）複製到 `Experiments/` | §0.1 |
| facts | 跑完自動產 `bin/.../Experiments/<name>-facts.md`；隨時可用 `dotnet run -- facts <N>` 從 archive 重產 | §6.2.2 |
| label 驗證 | 必須 `r<N>-` 前綴、第一顆必須是 `r<N>-baseline`、不得重複、不得自帶 seed 後綴 | §3.3 |

## facts 內容

`<name>-facts.md` 只算不判——用哪個主指標、誰是 champion，仍依契約與 guide §4 由分析者決定。

1. **TUNING-FACTS block**：§6.2.2 固定格式（archive 路徑清單 + 一列一 trial 的 markdown 表），數值逐字取自 archive 主表 `.csv`；`configDiffFromBaseline` 由 `-meta.csv` 的基準設定疊上主表 `DiffKnobs` 還原後比對，不含 Seed；直接貼進 `TuningHistory.md`。
2. **彙總**：每顆 config 三種主指標一次算好——runtime `sgm`（非 Optimal 以 PAR10 計）、endGap 平均（無解記 100%）、found 數與 `t_feas` `sgm`（無解以 PAR10 計）；Δ 以「正值 = 比 baseline 好」表示並對照 θ。
   - shift 由 R0 baseline runtime 中位數決定（< 60s → 1s，否則 10s），整期共用。
   - θ 取自 R0 baseline：多 instance 時逐 seed 先跨 instance 彙總，再量 seed 之間的離散；單一 instance 即 §3.0 原始定義。
3. **模型統計對帳**：每個 trial 在 `Solve()` 前由框架比對「建模記帳 / 框架索引 / solver 模型」三方——變數總數與 binary / integer / continuous 分布、限制式、SOS 等框架不索引的特殊元素、目標式方向。逐 instance 列一致與否、不一致的 trial 數與原因。匯入模型最常見的落差是 SpecialElements（SOS、二次限制式、indicator）：框架統計、IIS 與本檔的彙總都看不到那部分。
4. **結果不變式**：跨全部已 archive round，逐 instance 檢查 bound 越線（數學上不可能 → 容差被放寬或模型被換）與 Optimal objective 分散是否超過 MipGap 容許值。sense 取模型統計對帳記下的 solver 目標式方向（舊 archive 才退回推論）。檔案模式沒有 `ValidateRules`，這張表就是唯一的自動越界防線。
5. **dynamic search**：掃本輪 log 的 `MIP search method` 行，出現 `traditional branch-and-cut` 即 FAIL（§2.3.1）。

同一份對帳也在：production 的 `[Production] … ModelStats=MATCH|MISMATCH:<項目>` 行、log 的 `[模型統計對帳]` / `[MODEL_STATS_MISMATCH]`、archive `-meta.csv` 的 `modelStats` 區段（每個 instance 一份）。主表 `.csv` 維持 22 欄不變。

## cplex-tune

`CplexTuner` 繼承 `OptEngine` 取得 protected `Model` 呼叫 `TuneParam`（框架尚未封裝，guide 附錄 B），所以 `productionBaseline` 的全部旋鈕照樣經框架套進 CPLEX：

- baseline 已設的旋鈕全部進 fixed set（tune 不准動），另把停止契約的 `MipGap` / `AbsoluteMipGap` 明確釘進去——值等於 CPLEX 預設時 `GetParameterSet()` 不會列出它。
- CPLEX tune 的 `TimeLimit` 是**整個 tune 的總預算**，每次試跑的時限是 `Tune.TimeLimit`；所以契約的 `TimeLimit` 搬到 `Tune.TimeLimit`，總預算由指令參數給。
- 單一 instance 走 `TuneParam(fixedSet)` 並設 `Tune.Repeat`（預設 3，`--repeat n` 可改）；多 instance 走 `TuneParam(files, fixedSet)`。
- 輸出 = tuned 參數檔中 fixed set 以外的差異，以 `CPXPARAM_*` 名稱列出；對應的 `CplexConfig` 欄位查 `cplex-parameter-reference.md`，每顆拆成獨立 variant 走 §4。

## 已知限制

| 項目 | 說明 |
| --- | --- |
| `.mps` 的目標式方向 | MPS 沒有方向欄位：CPLEX 把 maximize 模型寫成**係數取負的 minimize**，讀回來 sense = Minimize、目標值反號。抄參考結果基線時要注意，或改用 `.sav` / `.lp` |
| `.lp` / `.mps` | 文字格式、係數截斷，讀回不保證精確重現原專案結果；範圍限制式在文字格式會多一個 `Rg*` 輔助變數。要調 production 的真實行為用 `.sav` |
| `ValidateRules` / 型別化取解 | 匯入模型沒有 C# 變數類別，不可用；正確性只能靠契約的來源證據 + facts 的結果不變式 |
| 情境判定 | runner 不替你選 A / B / C；彙總三種主指標都算，依契約區塊取用 |
| 多 instance 的 θ | 逐 seed 跨 instance 彙總後量離散，是本殼的延伸定義；guide 只定義單一 instance，寫進契約區塊時註明 |

## 建置

```powershell
dotnet build Templates/ModelTuner/ModelTuner.csproj
```

CPLEX 路徑預設 `C:\IBM\ILOG\CPLEX_Studio2211`，不同時用 `-p:CplexDir=<路徑>` 覆寫。
