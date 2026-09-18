# ModelInspector

吃一份**既有模型檔**（`.lp` / `.mps` / `.sav`，含各自的 `.gz` / `.bz2`），求解它，然後把 OptimFoundation 在匯入模式下能提供的資訊全部倒出來。

不綁任何題目專案——任何走 OptimFoundation 的專案匯出的模型檔都能丟進來，`RosteringProblem` 只是本文用來示範的那一個。

## 為什麼是「吃檔案」而不是引用專案

框架這一側的入口是 `OptEngine.ImportModel(fileName)` 與 `OptModel.FromFile(fileName, name)`。`ImportModel` 讀完檔會呼叫 `ReindexFromModel()`，從 active model 的 `ILPMatrix` 反向取回 `INumVar` 與 `IRange` 回填框架索引——這一步是關鍵，它讓求解、取解、IIS、metrics 這些「框架特定功能」在匯入的模型上照常運作。

代價只有一個：`VariableSets` 是空的。匯入的模型沒有 C# 變數類別可對應，所以型別化那一整套用不了。取捨如下：

| 功能 | 匯入模式 | 說明 |
| --- | --- | --- |
| `Solve()` / metrics / log / LP·MPS·SOL 匯出 / retention | 可用 | 不經過 `VariableSets` |
| `GetVariableValue(name)` / `GetSolution()` | 可用 | 走 `Variables` 索引，re-index 已填好 |
| `GetCVSolution` / `GetIVSolution` / `GetBVSolution` | 可用 | 依 solver 型別分類，不依賴 C# 類別 |
| `GetConflictConstraints()` / IIS `.ilp` | 可用 | 走 `_constraints`，匯入時無名的 row 自動補 `c0`、`c1`… |
| `CplexConfig` 全部旋鈕 / tuning | 可用 | `ImportModel` 在 `Build()` 之後執行，只換模型內容不動 solver 參數 |
| `OptProject` / `OptExperiment` | 可用 | 兩者都走 `_model.ApplyTo(engine)` 這個統一入口 |
| `GetSetVarValues<T>()` / `GetSetVarNames<T>()` / `GetSolution("TypeName")` | **不可用** | `VariableSets` 為空，一律回空字典 |
| `CsvCtrl.WriteSolution<T>()` / `OracleDBCtrl.WriteSolution<T>()` | **不可用** | 同上，沒有型別可寫 |
| `RegisteredVariableCount` | **失真** | 恆為 0；請改看 `VariableCount` |
| `ObjectiveSense` | **失真** | 恆為 `Minimize`（`EngineBase` 預設值），`ImportModel` 不依檔案內容更新它 |
| `ObjectiveTermCount` / `SoftConstraintCount` / `SoftPenaltyTermCount` | **失真** | 恆為 0；量的是框架 pool 的累積，匯入的目標式沒經過 pool |
| `VariableBuildCounts` / `ConstraintBuildCounts` | **失真** | 為空；Expected vs Actual 對帳只在 `Build*Vs` 路徑成立 |

每次執行的報告都會把上表「失真」的那幾項連同原因逐條印出來，不會讓人把 0 當成真值讀。

## 用法

```powershell
ModelInspector <model-file> [options]
```

| 參數 | 預設 | 說明 |
| --- | --- | --- |
| `<model-file>` | 必填 | `.lp` / `.mps` / `.sav`；相對路徑以**目前工作目錄**為基準（不是框架的 `Models/`） |
| `--name <label>` | 模型檔名 | 報告與輸出檔的名稱前綴。框架匯出的檔名帶的 `_SAV_<時間戳>` 那段會自動剝掉 |
| `--timelimit <sec>` | 60 | 求解時間上限 |
| `--mipgap <v>` | 0 | 相對 gap 門檻 `[0, 1)`；0 = 求到最佳 |
| `--threads <n>` | CPLEX 預設 | 執行緒數 |
| `--seed <n>` | CPLEX 預設 | 亂數種子 |
| `--max-print <n>` | 20 | Console 每區塊最多印幾筆（報告檔一律完整） |
| `--no-trajectory` | 關 | 不記錄收斂軌跡。掛 callback 會關閉 CPLEX dynamic search，**量效能時應該加這個** |
| `--quiet` | 關 | solver log 不洗 Console（仍完整寫進 `Logs/`） |

離開碼：`0` 有解、`1` 無解但正常結束、`2` 參數錯誤、`3` 求解過程丟例外。

## 走一次完整流程（以 RosteringProblem 為例）

```powershell
# 1. 跑 RosteringProblem，它會在模型建完後存一份 .sav 到自己的 Models/
cd ..\RosteringProblem
dotnet build
.\bin\Debug\net8.0\RosteringProblem.exe

# 2. 拿那份 .sav 餵給 Inspector
cd ..\ModelInspector
dotnet build
.\bin\Debug\net8.0\ModelInspector.exe `
  ..\RosteringProblem\bin\Debug\net8.0\Models\RosteringProblem_SAV_<時間戳>.sav `
  --mipgap 0.03 --quiet
```

用 `.sav` 而不是既有的 `.lp` / `.mps`：後兩者是文字格式、係數經十進位截斷，讀回來不保證能精確重現原專案的求解結果。`.sav` 是二進位、不截斷。

（實測這一題兩種來源給出相同的解，因為它的係數都是簡單值；係數帶長小數的模型就不見得。）

## 輸出

Console 印分區摘要；完整內容寫進執行檔目錄的 `Reports/`：

| 檔案 | 內容 |
| --- | --- |
| `<label>_Inspection_<時間戳>.md` | 九節完整報告（見下） |
| `<label>_Variables_<時間戳>.csv` | 全變數解值 `Name,Type,Value`，數值以 `R` 格式寫出（round-trip 保真） |
| `<label>_Trajectory_<時間戳>.csv` | 收斂軌跡 `TimeMs,Objective,Bound,Gap`（有取樣點才產生） |
| `<label>_Conflicts_<時間戳>.txt` | IIS 衝突限制式名稱（infeasible 才產生） |

報告的九節：

1. **來源** — 路徑、格式、大小、最後寫入時間
2. **執行設定** — `ProjectConfig` 全欄 + `ConfigSnapshot`（只記真的有設的旋鈕）
3. **模型結構** — re-index 後的變數 / 限制式數，逐項標注匯入模式下是否有效；解出來後另附變數型別分布
4. **求解結果** — `Status`、目標值、`BestBound`、`MIPGap`，加 `SolveMetrics` 全欄（`RunTimeMs` / `NodeCount` / `IterationCount` / `TrajectoryPoints` / `TFeasMs` / `DeltaBound` / `TStallMs`）與 `OptProject` 的 `TotalElapsed` / `BuildModelElapsed`
5. **收斂軌跡** — 等距抽樣 30 點的表；完整序列在 CSV
6. **變數解值** — 三型別分布 + 非零變數前 100 筆
7. **Infeasible 診斷** — `RefineConflict` 找出的衝突限制式清單，另有 `.ilp` 衝突模型寫進 `IISs/`
8. **框架輸出檔** — 本次產生的 LP / MPS / SOL / IIS 檔路徑與大小
9. **匯入模式的已知限制** — 上面那張表裡「不可用 / 失真」的項目逐條說明

框架自己的產物照舊落在各自的固定資料夾：`Models/`（本次 import 後再匯出的 LP·MPS，可拿來跟來源檔對照驗 round-trip）、`Sols/`、`IISs/`、`Logs/`。

## 已驗證

| 情境 | 結果 |
| --- | --- |
| `RosteringProblem` 的 `.sav`（1.07 MB，5,008 變數 / 9,856 限制式） | `Optimal`、Obj 3.4、Bound 3.3、Gap 2.94%——與原專案直接求解**完全一致**；變數名 `VariableB_DoubleOffFlag@2026_01_01@E1` 這類完整保留 |
| 同一題的 `.lp` | 同上，數值一致 |
| 人工造的 infeasible LP（`DemandFloor: x+y>=10` 對上 `CapacityCeiling: x+y<=5`） | `Infeasible`，IIS 精準抓出這兩條、沒有誤抓無關的第三條，`.ilp` 正常寫出 |

## 注意

`--no-trajectory`：不加的話會掛 trajectory callback，而 CPLEX 掛了 callback 就關閉 dynamic search。要拿 `RunTimeMs` 跟別次執行比較時務必加上，否則比的不是同一件事。報告裡也會提醒這點。
