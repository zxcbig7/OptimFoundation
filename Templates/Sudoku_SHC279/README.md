# Sudoku SHC 279 — OptimFoundation CPLEX Template

這個 template 以 Binary MILP 求解 9×9 Sudoku。題盤來自 Smart Hobbies 的 SHC 279，完整集合、參數、變數、限制式與目標式定義見 [`Model/Sudoku_SHC279_Model.md`](Model/Sudoku_SHC279_Model.md)。

## 架構

專案採 canonical 積木結構與單一 `Sudoku_SHC279` namespace：

- `Set/`：ROW、COLUMN、DIGIT、BLOCK，一個 generator 型別一檔。
- `Parameter/`：Given、BlockCell、ExactlyOne、ObjCoef。
- `Variable/`：`VariableB_CellDigit{Row,Column,Digit}`。
- `Objective/`：逐項套用 `ObjCoef`；目前全為零，因此是純可行性問題。
- `Constraint/`：CellValue、RowDigit、ColumnDigit、BlockDigit、Given 五組限制式。
- `Data/`：canonical CSV 與唯一資料入口 `Dataload`；`raw/` 保留原始矩陣供 import。
- `Solution/`：讀解後逐條驗證 givens、列、欄與宮規則。
- `Program.cs`：唯一組裝點，依序建立材料、`OptModel("Canonical")` 與執行環境。

宮的結構不寫死在限制式或解驗證中。`Parameter_BlockCell.csv` 明確列出每個 BLOCK 所含的 `(Row,Column)`；`Parameter_ExactlyOne.csv` 提供所有等式右側；`Parameter_ObjCoef.csv` 提供目標係數。換成其他完全平方邊長的標準 Sudoku 時，求解程式不必修改。

## 資料

求解模式只讀以下已就位的 canonical CSV：

- `Set_Row.csv`、`Set_Column.csv`、`Set_Digit.csv`、`Set_Block.csv`
- `Parameter_Given.csv`
- `Parameter_BlockCell.csv`
- `Parameter_ExactlyOne.csv`
- `Parameter_ObjCoef.csv`

`Parameter_Given` 與 `Parameter_BlockCell` 是純 key parameter，沒有 `QTY`。`Parameter_ExactlyOne` 是恰好一列的 scalar；`Parameter_ObjCoef` 依 DIGIT 各有一列。

## 執行

在 `OptimFoundation/OptimFoundation` repo 根目錄執行：

```powershell
# 正式求解與解驗證
dotnet run --project Templates\Sudoku_SHC279\Sudoku_SHC279.csproj

# r2：warm-up 後比較 baseline / feasibility emphasis / aggressive probe（3 seeds、輪替順序）
dotnet run --project Templates\Sudoku_SHC279\Sudoku_SHC279.csproj -- exp

# 將 Data/raw/Puzzle_SHC279.csv 展開並 Export 成 canonical CSV
dotnet run --project Templates\Sudoku_SHC279\Sudoku_SHC279.csproj -- import raw/Puzzle_SHC279
```

`exp` 會從 `Program.cs` 的 `productionBaseline` clone baseline 與 variants，輸出 Trial 設定快照及 metrics。AI 完成 tuning 時必須讀取本輪結果，只有在 champion 通過解品質、穩健效能與 production 驗證 gate 後，才把勝出設定寫回 `productionBaseline`。因此後續無參數命令直接使用已 promotion 的設定；`OptExperiment` 本身不會自動修改 config 或 source。若沒有 variant 能可靠勝過 baseline，production 保留原設定。

每輪 promotion 或 retain 決策記錄在 [`TuningHistory.md`](TuningHistory.md)。若修改 `productionBaseline`，該筆紀錄必須包含來源 experiment、勝出 Trial label、before/after config diff、比較證據及 production 驗證結果；`Program.cs` baseline 上方的 provenance 也必須同步更新。

預設需要 IBM ILOG CPLEX Studio 22.1.1；可用 MSBuild property `CplexDir` 覆寫安裝位置。

本專案位於 OptimFoundation framework repo 的 `Templates/`，因此刻意保留對 framework source 與 generator 的 `ProjectReference`，讓 template 隨框架一起建置並即時反映 API 變更。這是 framework repo 內 template 的限定例外；移到 `$OPT/AI-Modeling/Projects/` 的專案仍必須改用 `..\..\dlls\` HintPath。

## 預期結果

求解狀態應為 `Optimal`、目標值為 `0`，且驗證後盤面為：

```text
6 1 5 | 2 8 4 | 7 9 3
3 8 9 | 7 1 5 | 2 6 4
4 7 2 | 9 3 6 | 5 1 8
------+-------+------
9 3 4 | 5 7 2 | 1 8 6
7 6 1 | 8 9 3 | 4 2 5
2 5 8 | 4 6 1 | 3 7 9
------+-------+------
1 2 6 | 3 4 8 | 9 5 7
8 4 7 | 1 5 9 | 6 3 2
5 9 3 | 6 2 7 | 8 4 1
```

模型規模為 729 個 binary variables、351 條 constraints（81×4 + 27）。
