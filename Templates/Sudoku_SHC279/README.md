# Sudoku SHC 279 — CPLEX Template

這個範例把標準 9×9 Sudoku 建成 Binary MILP，並求解 Smart Hobbies 影片中的 SHC 279 題盤。
題目由 Pretzaal 設計；來源：[This AMAZING Trick Can Solve Extreme Sudoku](https://www.youtube.com/watch?v=m9Xaa4GXs9I)。

## 資料

- `Data/Set_Row.csv`、`Set_Column.csv`、`Set_Digit.csv`：三個索引集合，各為 1..9。
- `Data/Parameter_Given.csv`：題目的 27 個已知數字，欄位為 `Row,Column,Digit`。
- `Parameter_Given` 使用 `[OptParam(HasValue=false)]`，是沒有 QTY 的純 key parameter。
- `Dataload` 透過 `CsvDataSource` 載入，再由 `OptData.Load`／`DataContext` 驗證 set 參照、型別與重複 key。

題盤沒有硬編碼在模型中；更換 `Parameter_Given.csv` 就能求解另一題標準 9×9 Sudoku。

## 模型

- `x[row,column,digit] ∈ {0,1}`：該格是否填入該數字，共 `9×9×9 = 729` 個變數。
- 每格恰好一個數字：81 條。
- 每列的每個數字恰好一次：81 條。
- 每欄的每個數字恰好一次：81 條。
- 每個 3×3 宮的每個數字恰好一次：81 條。
- 題目 givens：27 條。
- 限制式合計：351 條。

`Program` 是唯一組裝點：變數、目標式與五種限制式直接在同一條 `OptModel` fluent chain 中建立並呼叫 `Build(engine)`；沒有額外的 `SudokuModel`、預先宣告的 builder 變數或集中 constraints class。
所有元件由單一 `AddModel(...)` callback 依序建立：變數 → 目標式 → 限制式；不再額外使用 `AddVariables(...)` 或 `VariableCreate` 包裝。
限制式不依賴 `Dataload`；建構子只接收各限制式實際需要的 Row、Column、Digit Set 或 Given Parameter。

Sudoku 是可行性問題，範例使用零係數的最小化目標式。求解後會另外用一般 C# 程式驗證 givens、9 列、9 欄與 9 個宮，避免只依賴 Solver status。

## 執行

```powershell
dotnet run --project Templates\Sudoku_SHC279\Sudoku_SHC279.csproj
```

需要本機安裝 IBM ILOG CPLEX Studio 22.1.1；若安裝位置不同，建置時覆寫 `CplexDir`。

本範例實際求得並驗證的解：

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
