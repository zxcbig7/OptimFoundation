# TSP_MultiDimSet — 數學模型

> 本 template 的教學重點是**多維 Set**：`Set_Arc` 一列就是一條實際存在的弧，`BuildVars` 只建立這些弧，不展開 `NODE × NODE` 全格。

## 問題描述

一台車從單一 depot 出發，拜訪每個 customer 恰好一次後返回 depot。可行駛的路段由 `ARC` 明確列出（不假設完全圖），每條弧有其成本。求總成本最小的封閉巡迴路線。

## Terminology Mapping Table

| Term | 中文語意 | Role | Unit | Derived? | Raw phrase |
| --- | --- | --- | --- | --- | --- |
| Node | 節點 | parameter | — | No | 「所有節點」 |
| Depot | 出發／返回節點 | parameter | — | No | 「出發/返回節點」 |
| Customer | 必須拜訪的節點 | parameter | — | No | 「必須拜訪的節點」 |
| Arc | 實際可用有向弧 | parameter | — | No | 「實際可用有向弧」 |
| ArcCost | 弧成本 | parameter | cost | No | 「最小化使用弧成本」 |
| UseArc | 是否使用該弧 | variable | 0/1 | No | 「標準 TSP constraints」 |
| VisitOrder | 拜訪次序 | variable | order | No | 「subtour elimination（例如 MTZ）」 |

## SET

| Set | 語意 | 成員範例 | 程式宣告 |
| --- | --- | --- | --- |
| NODE | 所有節點 | N1, N2, N3 | `[OptSet]` + `[OptDim<string>("Node")]` |
| DEPOT | 出發／返回節點（單一） | N1 | `[OptSet]` + `[OptDim<string>("Node")]` |
| CUSTOMER | 必須拜訪的節點 | N2, N3 | `[OptSet]` + `[OptDim<string>("Node")]` |
| ARC | 實際可用有向弧 | (N1, N2) | `[OptSet]` + `[OptDim<string>("From")]` + `[OptDim<string>("To")]` |

`DEPOT ∪ CUSTOMER = NODE`，且兩者不相交。

## PARAM

| Param | 語意 | Dim | 值 | 單位 |
| --- | --- | --- | --- | --- |
| ArcCost | 行駛該弧的成本 | From, To | (N1,N2)=3 … | cost |

## VAR

| Var | 語意 | Dim | 型別 | LB | UB |
| --- | --- | --- | --- | --- | --- |
| UseArc | 是否使用該弧 | From, To（僅 ARC 成員） | Binary | 0 | 1 |
| VisitOrder | 拜訪次序 | Node（僅 CUSTOMER） | Continuous | 1 | \|NODE\| − 1 |

`VisitOrder` 的界限不寫進變數宣告，改以 `VisitOrderRange` 一條限制式表達。

## CONSTRAINT

### CustomerInDegree `[XOR]` ∀ node ∈ CUSTOMER

$$\sum_{(i,\,node)\,\in\,ARC} UseArc_{i,node} = 1$$

每個 customer 恰好有一條入弧。

### CustomerOutDegree `[XOR]` ∀ node ∈ CUSTOMER

$$\sum_{(node,\,j)\,\in\,ARC} UseArc_{node,j} = 1$$

每個 customer 恰好有一條出弧。

### DepotOutDegree `[XOR]` ∀ depot ∈ DEPOT

$$\sum_{(depot,\,j)\,\in\,ARC} UseArc_{depot,j} = 1$$

depot 恰好有一條出弧。

### DepotInDegree `[XOR]` ∀ depot ∈ DEPOT

$$\sum_{(i,\,depot)\,\in\,ARC} UseArc_{i,depot} = 1$$

depot 恰好有一條入弧。

### SubtourMTZ `[BigM]` ∀ (i, j) ∈ ARC 且 i ∈ CUSTOMER 且 j ∈ CUSTOMER 且 i ≠ j

$$VisitOrder_i - VisitOrder_j + \lvert NODE \rvert \cdot UseArc_{i,j} \le \lvert NODE \rvert - 1$$

MTZ 子迴圈消除：若使用弧 (i,j)，則 j 的次序必須大於 i。不含 depot 的弧，否則無法回到起點。

### VisitOrderRange `[Range]` ∀ node ∈ CUSTOMER

$$1 \le VisitOrder_{node} \le \lvert NODE \rvert - 1$$

拜訪次序的取值範圍；MTZ 生效所需。

## OBJ

$$\min \sum_{(i,j)\,\in\,ARC} ArcCost_{i,j} \cdot UseArc_{i,j}$$

最小化總行駛成本。

## 已套用假設

原始 Model.md 只有五行中文的 constraint 列點與程式宣告示範，以下為補全時所做的建模決定：

1. **subtour elimination 採 MTZ**（原文寫「例如 MTZ」，未指定形式）。使用標準大 M = `|NODE|` 版本；`|NODE|` 由 `set_Node.Count` 取得，非寫死。
2. **原文第 4 點「flow conservation」未實作為獨立限制式**。對單車輛 TSP，`CustomerInDegree` + `CustomerOutDegree` + depot 的一出一入已蘊含每個節點入度 = 出度；額外加入會是冗餘限制式，改變模型規模而不改變可行域。
3. **DEPOT 假設恰有一個成員**。多 depot 屬另一類問題（MDVRP），不在本 template 範圍。
4. **`VisitOrder` 只對 CUSTOMER 建立**，depot 不參與 MTZ（標準做法，等價於固定 depot 次序為 0）。
5. **`VisitOrder` 宣告為 Continuous**。MTZ 在度數限制式成立時會自然取整數值，宣告為連續可降低求解難度。
6. **`VisitOrder` 界限 `[1, |NODE|−1]`** 寫成獨立限制式 `VisitOrderRange`，而非藏進變數 builder 的 bounds 參數。
7. **ARC 不假設為完全圖**：所有加總都只在 `Set_Arc` 實際存在的成員上展開。

## 驗收基準

`Data/` 的預設實例為 5 節點（depot `N1` + customer `N2`–`N5`）完全有向圖，成本對稱。

**已知最佳解 = 14**，路線 `N1 → N2 → N3 → N4 → N5 → N1`（或其反向）。

| 弧 | 成本 |
| --- | --- |
| N1→N2 | 3 |
| N2→N3 | 4 |
| N3→N4 | 3 |
| N4→N5 | 2 |
| N5→N1 | 2 |
| **合計** | **14** |
