# Template_Model — 框架功能展示用的抽象模型

每個積木以它示範的框架功能命名；數學意義只寫在本檔。複製到新專案時，整個檔案改成自己的業務名。

## SET

| 符號 | 程式類別 | 維度 | 示範 |
| --- | --- | --- | --- |
| $K = \{K1, K2, K3\}$ | `Set_StringKey` | Key（string） | 一維 Set |
| $D = \{2026\text{-}01\text{-}01, 2026\text{-}01\text{-}02\}$ | `Set_DateKey` | Date（DateTime） | DateTime 維度 |
| $P \subseteq K \times D$ | `Set_SparsePair` | Key × Date | 稀疏多維 Set：只列實際存在的組合 |

## PARAM（值進 QTY，經 Dataload 取得）

| 符號 | 程式類別 | 維度 | 意義 |
| --- | --- | --- | --- |
| $\pi > 0$ | `Parameter_Scalar` | scalar | 單位罰分（缺口與軟性限制式共用） |
| $c_k \ge 0$ | `Parameter_OneDim` | Key | 選取成本 |
| $r_{k,d} \ge 0$ | `Parameter_TwoDim` | Key × Date | 需求量（$K \times D$ 每格都有） |

## VAR

| 符號 | 程式類別 | 型別 | 定義域 | 示範 |
| --- | --- | --- | --- | --- |
| $y_k \in \{0,1\}$ | `VariableB_Binary` | binary | $K$ | 一維 domain |
| $x_{k,d} \in \mathbb{Z}_{\ge 0}$ | `VariableI_Integer` | integer | $P$ | 稀疏多維 Set 當 domain |
| $s_{k,d} \ge 0$ | `VariableC_Continuous` | continuous | $K \times D$ | 兩個 Set 的笛卡兒積 |
| $z \ge 0$ | `VariableC_ZeroDim` | continuous | — | 零維變數 |

## OBJECTIVE

$$\min \sum_k c_k y_k + \pi \sum_{k,d} s_{k,d} + z + \pi \cdot \max\left(0, \sum_k y_k - 1\right)$$

最後一項是 `Constraint_LessEqualSoft` 的違反量罰分，由框架自動加進目標式。

## CONSTRAINTS

| 程式類別 | 式子 | 示範 |
| --- | --- | --- |
| `Constraint_Equal` | $[(k,d) \in P]\, x_{k,d} + s_{k,d} = r_{k,d} \quad \forall k \in K, d \in D$ | `CreateEqual`，二維索引含 DateTime |
| `Constraint_LessEqual` | $x_{k,d} \le r_{k,d}\, y_k \quad \forall (k,d) \in P$ | `CreateLessEqual`，RHS 帶係數的變數項 |
| `Constraint_GreaterEqual` | $z \ge \sum_{k:(k,d) \in P} x_{k,d} \quad \forall d \in D$ | `CreateGreaterEqual`，零維變數在 LHS、加總在 RHS |
| `Constraint_Range` | $1 \le \sum_k y_k \le \lvert K \rvert - 1$ | `CreateRange`，無索引 |
| `Constraint_LessEqualSoft` | $\sum_k y_k \le 1$（軟性，每超過 1 單位罰 $\pi$） | `CreateLessEqualSoft`，無索引 |

## MIP START

選取成本最低的 Key：$y_{k^*} = 1$、其餘 $y_k = 0$，$k^* = \arg\min_k c_k$。只給 $y$，其餘變數由 CPLEX 補齊。

## VALIDATION RULES

- 每格 $(k,d) \in K \times D$：$x + s = r$（$(k,d) \notin P$ 時 $x = 0$）。
- 每個 $(k,d) \in P$：$x \le r \cdot y$。
- 每個 $d$：$z \ge$ 當日 $x$ 加總。
- $1 \le \sum_k y_k \le \lvert K \rvert - 1$。
- Binary、Integer 與非負條件在 tolerance 內成立。
- 軟性限制式允許違反，不檢查。

## 範例資料的最佳解

$y_{K1} = y_{K3} = 1$；$x_{K1,d_1} = 3$、$x_{K1,d_2} = 2$、$x_{K3,d_2} = 5$；缺口 $s_{K2,d_1} = 4$、$s_{K2,d_2} = 1$、$s_{K3,d_1} = 2$；$z = 7$；軟性違反 1。目標值 $13 + 4 \times 7 + 7 + 4 = 52$。
