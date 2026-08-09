# TSP MultiDimSet — 數學模型

> Traveling Salesperson Problem（有向、不完全圖）範本。
> Phase 1 產物；經使用者確認後才進入 Foundation Coding。

## 1a · 問題敘述（去故事化）

給定一個有向圖，節點集合為 `NODE`，實際允許行走的有向弧集合為 `ARC ⊆ NODE × NODE`，每條弧都有非負旅行成本。業務資料不假設完整圖：不存在於 `ARC` 的 `(from, to)` 不可行，程式不得為它建立變數。

從唯一出發節點 `DEPOT` 出發，選擇一組弧形成一個 Hamiltonian cycle：每個節點恰好被進入與離開一次、沒有任何子迴路，最後回到 `DEPOT`。目標是旅行總成本最小。

本範本刻意以多維 Set 表示稀疏弧：

```csharp
[OptSet<string, string>("NodeFrom", "NodeTo")]
public sealed partial class Set_Arc { }
```

`Set_Arc` 的每個成員是具名 tuple `(NodeFrom, NodeTo)`，所以 `ARC` 本身是一個維度，而不是 `NODE × NODE` 的全笛卡兒積。後續程式必須以 `BuildBVs<VariableB_VisitArc>(data.Arc)` 建立變數；變數數量應精確等於可行弧數。

## 1b · Terminology Mapping Table

| Term | 中文語意 | Role | Unit | Derived? | 程式對應 |
| --- | --- | --- | --- | --- | --- |
| Node | 節點／城市 | set | - | No | `Set_Node` |
| Depot | 出發與返回節點 | set（單一成員） | - | No | `Set_Depot` |
| Customer | 非 Depot 節點 | set | - | No | `Set_Customer` |
| Arc | 實際可行的有向弧 `(NodeFrom, NodeTo)` | **2D set** | - | No | `Set_Arc` |
| ArcCost | 有向弧旅行成本 | parameter | cost unit | No | `Parameter_ArcCost` |
| ExactlyOne | 入／出度限制式右側 | parameter | count | No | `Parameter_ExactlyOne` |
| SubtourCoefficient | MTZ 式中的 `|CUSTOMER|` | parameter | count | Yes, from customer count | `Parameter_SubtourCoefficient` |
| SubtourRightHandSide | MTZ 式右側 `|CUSTOMER|-1` | parameter | count | Yes, from customer count | `Parameter_SubtourRightHandSide` |
| VisitArc | 是否選擇有向弧 | variable | 0/1 | No | `VariableB_VisitArc` |
| VisitOrder | 客戶被拜訪的 MTZ 順序 | variable | count | No | `VariableX_VisitOrder` |

`DEPOT` 與 `CUSTOMER` 都是 `NODE` 的子集合，並且必須滿足：`|DEPOT| = 1`、`DEPOT ∩ CUSTOMER = ∅`、`DEPOT ∪ CUSTOMER = NODE`。這些是資料驗證契約，並非靠限制式靜默修正。

## SET

| Set | 語意 | 成員／格式 | → 程式 |
| --- | --- | --- | --- |
| NODE | 所有節點 | 唯一、非空的節點名稱 | `Set_Node`，`[OptSet<string>]` |
| DEPOT | 唯一出發／返回節點 | `NODE` 的一個成員 | `Set_Depot`，`[OptSet<string>]` |
| CUSTOMER | 需要拜訪的非 depot 節點 | `NODE \ DEPOT` | `Set_Customer`，`[OptSet<string>]` |
| ARC | 實際可行的有向弧 | 唯一 tuple `(NodeFrom, NodeTo)` | `Set_Arc`，`[OptSet<string,string>("NodeFrom","NodeTo")]` |

`ARC` 以單一資料表與成本共讀，而不是另存 `Set_Arc.csv`：

```text
# Data/ArcCost.csv（RFC4180；有表頭）
NODEFROM,NODETO,QTY
Depot,A,8
Depot,B,6
A,Depot,7
A,B,3
A,C,4
B,Depot,5
B,A,3
B,C,2
C,Depot,6
C,A,4
C,B,2
```

Phase 2 的 `Dataload(IDataSource source)` 必須從同一份表讀取兩種模型元素：

```csharp
Arc.Load(source, "ArcCost");
arcCost = source.LoadParam<Parameter_ArcCost>("ArcCost");
```

這是本範本的核心驗收點：`Set_Arc` 依 `NodeFrom`、`NodeTo` 表頭擷取前兩欄，`Parameter_ArcCost` 讀取相同兩個 key 與 `QTY`；禁止人工維護兩份 arc 清單。

## PARAM

| Param | 語意 | Dim | 值／來源 | → 程式 |
| --- | --- | --- | --- | --- |
| ArcCost | 選擇弧的旅行成本 | NodeFrom, NodeTo | `ArcCost.csv` 的 `QTY` | `Parameter_ArcCost` |
| ExactlyOne | 每個節點恰好一進一出 | scalar | 1 | `Parameter_ExactlyOne` |
| SubtourCoefficient | MTZ 中鬆弛係數 | scalar | `|CUSTOMER|` | `Parameter_SubtourCoefficient` |
| SubtourRightHandSide | MTZ 右側 | scalar | `|CUSTOMER| - 1` | `Parameter_SubtourRightHandSide` |
| OrderLowerBound | 客戶順序下界 | scalar | 1 | `Parameter_OrderLowerBound` |
| OrderUpperBound | 客戶順序上界 | scalar | `|CUSTOMER|` | `Parameter_OrderUpperBound` |

`SubtourCoefficient`、`SubtourRightHandSide` 與 order bounds 是由 `CUSTOMER` cardinality 推導後寫入 canonical CSV 的資料值；後續資料 import／export 可計算它們，但求解期不可在 constraint 內寫裸數字或自行重算。

`Parameter_ArcCost` 的目標宣告契約如下。兩個 `[OptDim]` 順序是變數 key 與 multi-dimensional set 攤平順序的一部分，不可交換。

```csharp
[OptParam]
[OptDim<Set_Node>("NodeFrom")]
[OptDim<Set_Node>("NodeTo")]
public sealed partial class Parameter_ArcCost { }
```

## VAR

| Var | 語意 | Dim | 型別 | LB | UB | → 程式 |
| --- | --- | --- | --- | --- | --- | --- |
| VisitArc | 弧 `(from,to)` 被選中 | ARC | Binary | 0 | 1 | `VariableB_VisitArc` |
| VisitOrder | 客戶的拜訪次序 | CUSTOMER | Continuous | OrderLowerBound | OrderUpperBound | `VariableX_VisitOrder` |

`VisitArc` 的宣告仍沿用兩個一維 `Set_Node` metadata，因為變數 key 需保持平坦；只有 builder 的定義域改成一個 2D set：

```csharp
[OptVar]
[OptDim<Set_Node>("NodeFrom")]
[OptDim<Set_Node>("NodeTo")]
public sealed partial class VariableB_VisitArc { }

public sealed class VariableX_VisitOrder : VariableBase
{
    public string Customer { get; set; } = string.Empty;
}
```

Phase 2 組裝時的必要契約：

```csharp
engine.BuildVars<VariableB_VisitArc>(data.Arc);      // |ARC| 個 binary vars，不是 |NODE|²
engine.BuildCVs<VariableX_VisitOrder>(
    data.OrderLowerBound,
    data.OrderUpperBound,
    data.Customer);
```

`VisitArc` key 的格式預期為 `VariableB_VisitArc@Depot@A`。tuple 分量按 `Set_Arc` 宣告順序攤平，因此不可把 `(NodeTo, NodeFrom)` 傳入或以 `Node × Node` 取代 `Arc`。

## CONSTRAINT

### LeaveOnce `[Assignment]` ∀ node ∈ NODE

$$
\sum_{(node,j) \in ARC} VisitArc_{node,j} = ExactlyOne
$$

每個節點恰好離開一次。若某節點沒有任何以它為起點的弧，該空 pool 必須 fail fast，不能把限制式略過。

### EnterOnce `[Assignment]` ∀ node ∈ NODE

$$
\sum_{(i,node) \in ARC} VisitArc_{i,node} = ExactlyOne
$$

每個節點恰好進入一次。若某節點沒有任何以它為終點的弧，該空 pool 必須 fail fast。

### SubtourEliminationMtz `[MTZ]` ∀ `(i,j) ∈ ARC`，其中 `i,j ∈ CUSTOMER`

$$
VisitOrder_i - VisitOrder_j
+ SubtourCoefficient \cdot VisitArc_{i,j}
\le SubtourRightHandSide
$$

其中 `SubtourCoefficient = |CUSTOMER|`，`SubtourRightHandSide = |CUSTOMER|-1`。對任一被選中的 customer-to-customer 弧，此式強制次序嚴格遞增；配合 order bounds 後，所有不含 `DEPOT` 的子迴路均不可行。

僅為 `ARC` 中實際存在、且兩端皆在 `CUSTOMER` 的弧建立此限制式。`DEPOT` 相關弧不套 MTZ；`ARC` 若含自迴路，應在資料驗證階段拒絕而非由本式碰巧排除。

## OBJ

$$
\min \sum_{(i,j) \in ARC} ArcCost_{i,j} \cdot VisitArc_{i,j}
$$

目標式只能迭代 `ARC`，以 `(NodeFrom, NodeTo)` 對位查得 `ArcCost` 後加入 `VisitArc`。每一條 ARC 必須恰有一筆成本；缺成本、重複成本或成本對應到不存在弧都是資料錯誤，必須在建模前 fail fast。

## 解的驗證契約

Phase 2 的 `TSP_MultiDimSetSolution` 必須以選中的 `VisitArc` 重建路徑，並驗證：

1. 目標狀態為 `Optimal`；若為 `Infeasible`，先輸出 IIS，不能放寬限制式。
2. 選中弧數 = `|NODE|`，且每個 NODE 的入度與出度皆為 1。
3. 從唯一 `DEPOT` 沿選中弧走 `|NODE|` 步後第一次且僅一次回到 `DEPOT`。
4. 途中每個 `CUSTOMER` 恰出現一次；不可有與 depot cycle 分離的子迴路。
5. 重新加總所選 `ArcCost`，須與 solver objective 在容忍誤差內相等。

上述範例資料的預期最佳 tour 為：

```text
Depot → B → C → A → Depot
```

預期目標值：`6 + 2 + 4 + 7 = 19`；變數數：11 個 `VisitArc` binary + 3 個 `VisitOrder` continuous = 14；限制式數：4 個 LeaveOnce + 4 個 EnterOnce + 6 個 MTZ = 14。

## 已套用的預設假設（請一併確認）

1. 這是**有向** TSP；`(A,B)` 與 `(B,A)` 可同時存在且成本可不同。若是對稱 TSP，仍以兩條有向 ARC 資料列表示。
2. 圖可以不完整，但資料必須至少容許一個 Hamiltonian cycle；否則預期為 Infeasible 並依 IIS 診斷。
3. 節點名稱不可含 `@`，因為 `@` 是框架 variable key 分隔符；ARC 不可有重複 tuple 或 self-loop。
4. `ArcCost` 為非負有限數值；負成本與額外業務條件不在本基礎範本範圍。
5. MTZ 是此 Phase 的標準、易對照 MILP 表述；未來若要處理大型圖，可另行改為 DFJ lazy cuts／flow formulation，不在本次範圍。
6. `Set_Arc` 與 `Parameter_ArcCost` 的同檔共讀依賴 `2026-08-08-multidim-set.md` 規格；若框架尚未完成該規格，Phase 2 不得偽造為 `NODE × NODE` 或雙 CSV 降級實作。

## Phase 2 檔案與組裝契約

經確認後，Foundation Coding 必須建立下列 canonical 元件，且一條數學式對應一個 Constraint 檔案：

```text
TSP_MultiDimSet/
├── Set/Set_Node.cs
├── Set/Set_Depot.cs
├── Set/Set_Customer.cs
├── Set/Set_Arc.cs
├── Parameter/Parameter_ArcCost.cs
├── Parameter/Parameter_ExactlyOne.cs
├── Parameter/Parameter_SubtourCoefficient.cs
├── Parameter/Parameter_SubtourRightHandSide.cs
├── Parameter/Parameter_OrderLowerBound.cs
├── Parameter/Parameter_OrderUpperBound.cs
├── Variable/VariableB_VisitArc.cs
├── Variable/VariableX_VisitOrder.cs
├── Objective/ObjectiveFunction.cs
├── Constraint/Constraint_LeaveOnce.cs
├── Constraint/Constraint_EnterOnce.cs
├── Constraint/Constraint_SubtourEliminationMtz.cs
├── Data/Dataload.cs
├── Data/ArcCost.csv
├── Solution/TSP_MultiDimSetSolution.cs
└── Program.cs
```

`Program.cs` 是唯一組裝點，將有兩個 `AddVariables`、一個 `AddObjective` 與三個 `AddConstraints`。Constraint 與 Objective 只能收取其實際依賴的 Set／Parameter／scalar，禁止傳入整個 `Dataload`，也禁止在 fluent pipeline 直接操作 pool。
