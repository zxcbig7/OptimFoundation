# Sudoku SHC 279 — 數學模型

標準 9×9 Sudoku 的 Binary MILP 表述。題盤來源：Smart Hobbies 影片 SHC 279（Pretzaal 設計）。

模型不綁定 9×9：邊長、宮的劃分與數字集合全部來自資料，換一份 `Data/raw/` 題盤即可求解 4×4、16×16 等任何完全平方邊長的標準數獨。

## 術語

| 詞 | 定義（本模型採用） |
| --- | --- |
| 格（Cell） | 盤面上由（列, 欄）唯一決定的一個位置 |
| 宮（Block） | √N × √N 的子區塊；標準 9×9 盤面共 9 個 3×3 宮。哪些格屬於哪個宮由 `Set_BlockCell` 資料決定，不寫死在 code |
| Given | 題目一開始就填好的格子；以（列, 欄, 數字）三元組 Set 表示 |
| 恰好一次（ExactlyOne） | 每個群組（格 / 列 / 欄 / 宮）中某數字必須出現的次數，值為 1。以 scalar parameter 表示，不在限制式裡寫裸數字 |
| 群組（Group） | 數獨規則中「同一個數字不得重複」的範圍，共四種：單一格內的數字、同列、同欄、同宮 |
| 邊長 N | 盤面的列數（= 欄數 = 數字個數）。由 `Data/raw/` 的題盤矩陣尺寸決定，非固定 9 |
| 可行性問題 | 沒有優劣之分、只要求「找到一個滿足所有限制式的解」的問題。Sudoku 屬此類，故目標式係數全為 0 |

## SET

| Set | 語意 | 成員 | → 程式 |
| --- | --- | --- | --- |
| ROW | 盤面的列索引 | 1..N | `Set_Row`：`[OptSet]` + `OptDim<int>("Row")` |
| COLUMN | 盤面的欄索引 | 1..N | `Set_Column`：`[OptSet]` + `OptDim<int>("Column")` |
| DIGIT | 可填入的數字 | 1..N | `Set_Digit`：`[OptSet]` + `OptDim<int>("Digit")` |
| BLOCK | 宮（√N × √N 的子區塊） | 1..N | `Set_Block`：`[OptSet]` + `OptDim<int>("Block")` |
| GIVEN | 題盤已知數字的（列, 欄, 數字）tuple | ROW, COLUMN, DIGIT | `Set_Given`：三個 primitive `OptDim<int>` |
| BLOCKCELL | 每個宮涵蓋的（宮, 列, 欄）tuple | BLOCK, ROW, COLUMN | `Set_BlockCell`：三個 primitive `OptDim<int>` |

## PARAM

| Param | 語意 | Dim | 有值 | → 程式 |
| --- | --- | --- | --- | --- |
| ExactlyOne | 「恰好一次」的計數值 | —（scalar） | 是 | `Parameter_ExactlyOne` |
| ObjCoef | 目標式中各數字的權重 | DIGIT | 是 | `Parameter_ObjCoef` |

`BlockCell` 是宮的劃分規則資料化的結果——宮的邊長不寫在 code 裡，由資料準備階段依盤面邊長算出後落成 CSV。

## VAR

| Var | 語意 | Dim | 型別 | LB | UB |
| --- | --- | --- | --- | --- | --- |
| CellDigit | 該格是否填入該數字 | ROW, COLUMN, DIGIT | Binary | 0 | 1 |

→ 程式：`VariableB_CellDigit`

## CONSTRAINT

### CellValue `[Assignment]` ∀ row ∈ ROW, column ∈ COLUMN

$$\sum_{digit \in DIGIT} CellDigit_{row,\,column,\,digit} = ExactlyOne$$

每一格恰好填入一個數字。

### RowDigit `[Assignment]` ∀ row ∈ ROW, digit ∈ DIGIT

$$\sum_{column \in COLUMN} CellDigit_{row,\,column,\,digit} = ExactlyOne$$

每一列的每個數字恰好出現一次。

### ColumnDigit `[Assignment]` ∀ column ∈ COLUMN, digit ∈ DIGIT

$$\sum_{row \in ROW} CellDigit_{row,\,column,\,digit} = ExactlyOne$$

每一欄的每個數字恰好出現一次。

### BlockDigit `[Assignment]` ∀ block ∈ BLOCK, digit ∈ DIGIT

$$\sum_{(row,\,column)\;:\;BlockCell_{block,\,row,\,column}} CellDigit_{row,\,column,\,digit} = ExactlyOne$$

每一宮的每個數字恰好出現一次。宮涵蓋哪些格由 `BlockCell` 決定。

### Given `[Fix]` ∀ (row, column, digit) ∈ Given

$$CellDigit_{row,\,column,\,digit} = ExactlyOne$$

題盤已知的格子固定為該數字。

## OBJ

$$\min \sum_{row \in ROW} \sum_{column \in COLUMN} \sum_{digit \in DIGIT} ObjCoef_{digit} \cdot CellDigit_{row,\,column,\,digit}$$

Sudoku 是**可行性問題**：任何滿足上述五組限制式的解都同樣好。`ObjCoef` 全為 0，目標式因此對可行域沒有偏好，只用來讓建模生命週期有統一的目標式階段與求解 log。

權重放在 `Parameter_ObjCoef` 而非寫死在 code，是為了：若日後要在多解題盤中做 tie-break（例如偏好較小數字），只需改 CSV，不動任何 `.cs`。

## 預設假設

1. 盤面為正方形，邊長 N 為完全平方數（宮的邊長 = √N）。
2. `DIGIT` 的成員數 = N，與列數、欄數相同。
3. 題盤有解；若 `Status == Infeasible`，代表題盤本身矛盾或 givens 抄錯，依驗收協定讀 `IISs/*.ilp` 診斷。
4. 五組限制式皆為 hard constraint。

## 手算 sanity（供解驗證比對）

本題盤（27 個 givens）的唯一解：

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

規模：變數 9×9×9 = 729；限制式 81 + 81 + 81 + 81 + 27 = 351。目標值恆為 0。
