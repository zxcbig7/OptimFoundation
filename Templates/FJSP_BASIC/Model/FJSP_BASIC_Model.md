# FJSP_BASIC — 數學模型

> Flexible Job Shop Scheduling Problem（彈性零工式排程）基礎版。
> Phase 1 產物；經使用者確認後才進 Foundation Coding。

## 1a · 問題敘述（去故事化）

有 N 個批次，每個批次依序有 M 道加工作業（OP1 → OP2 → …，前一道完成才能開始下一道）。
有 K 台機台，每道作業可在任一機台加工（total flexibility），加工時間依（批次, 作業, 機台）而異，單位一律**小時**。
每台機台同一時間只能加工一道作業，作業不可中斷（non-preemptive）。
實例由 `Dataload.GenerateInstance(lots, operations, eqps, seed)` seeded 生成（決定論、可重現）；預設 N=6、M=3、K=4、seed=42、加工時間 2..9 小時（較大規模，讓求解夠久、收斂軌跡有多點可畫）。
目標：所有作業完工的最晚時間（makespan）最小化。

## 1b · Terminology Mapping Table

| Term | 中文語意 | Role | Unit | Derived? | Raw phrase |
| --- | --- | --- | --- | --- | --- |
| Lot | 批次 | set | - | No | 「3 個批次」 |
| Operation | 作業道次（有序） | set | - | No | 「依序 2 道加工作業」 |
| Eqp | 機台 | set | - | No | 「3 台機台」 |
| ProcessTime | 加工時間 | parameter | hours | No | 「加工時間依（批次,作業,機台）而異」 |
| BigM | Disjunctive 鬆弛上界 | parameter | hours | Yes（= Σ各作業最大加工時間，見 PARAM） | linearization 需要 |
| Assign | 作業指派到機台 | variable | 0/1 | No | 「每道作業可在任一機台加工」 |
| Start | 作業開始時間 | variable | hours | No | 排程輸出 |
| Complete | 作業完成時間 | variable | hours | No | 「前一道完成才能開始下一道」 |
| Precede | 同機台兩作業先後序 | variable | 0/1 | No | 「同一時間只能加工一道」linearization 需要 |
| Makespan | 最晚完工時間 | variable | hours | No | 「最晚時間最小化」 |
| 不可中斷 | non-preemptive | constraint（由 Start/Complete 連續時段隱含） | - | No | 「作業不可中斷」 |

## SET

| Set | 語意 | 成員 | → 程式 |
| --- | --- | --- | --- |
| Lot | 批次 | LOT1..LOT{N}（預設 6） | `List<string>` / `string[]` |
| Operation | 作業道次（Set_Operation.csv 行序 = 加工順序） | OP1..OP{M}（預設 3） | `List<string>` / `string[]` |
| Eqp | 機台 | EQP1..EQP{K}（預設 4） | `List<string>` / `string[]` |

Set 由 `IDataSource.ReadSet` 讀入（CSV = `Data/Set_{Name}.csv`，一行一成員、無表頭）；`Dataload` 載入時檢查 parameter 出現的值 ⊆ 對應 set，失同步即 fail fast。

## PARAM

| Param | 語意 | Dim | 值 | → 程式 |
| --- | --- | --- | --- | --- |
| ProcessTime | 加工時間（小時） | Lot, Operation, Eqp | 下表 | `Parameter_ProcessTime`（QTY 欄） |
| BigM | Disjunctive 上界（小時） | -（scalar） | Σ_{lot,op} max_eqp ProcessTime（由實例推導、非寫死） | `Dataload.BigM`（derived property） |
| MakespanFloor | Range 規劃窗下界（示範，非綁定） | -（scalar） | 0 | `Dataload.MakespanFloor` |
| MakespanDeadline | Range 規劃窗上界（示範，非綁定） | -（scalar） | = BigM（保證放大實例仍可行） | `Dataload.MakespanDeadline`（derived） |
| SoftMakespanTarget | Soft 目標上限（示範，放大後會被違反） | -（scalar） | 10 | `Dataload.SoftMakespanTarget` |
| MakespanPenalty | Soft 每單位違反懲罰 | -（scalar） | 2 | `Dataload.MakespanPenalty` |

ProcessTime 數據：不再手寫固定表，改由 `Dataload.GenerateInstance` 以固定 seed 隨機生成（每個 (Lot, Operation, Eqp) 一個 2..9 小時的整數），決定論、可重現。要改規模/難度就調 `GenerateInstance(lots, operations, eqps, seed)` 的引數。

BigM 推導：`Σ_{lot,op} max_eqp ProcessTime`（最壞情況全序列排程長度上界，為最緊合法上界），由實例在 `Dataload.BigM` 動態算出，數據換掉自動重算、不寫死。

## VAR

| Var | 語意 | Dim | 型別 | LB | UB |
| --- | --- | --- | --- | --- | --- |
| Assign | 作業指派到該機台 =1 | Lot, Operation, Eqp | Binary | 0 | 1 |
| Start | 作業開始時間（小時） | Lot, Operation | Continuous | 0 | INFTY |
| Complete | 作業完成時間（小時） | Lot, Operation | Continuous | 0 | INFTY |
| Precede | 前者先於後者（同機台時）=1 | LotA, OperationA, LotB, OperationB | Binary | 0 | 1 |
| Makespan | 最晚完工時間（小時） | -（單一成員 set） | Continuous | 0 | INFTY |

轉譯 metadata：
- `VariableB_Assign`、`VariableX_Start`、`VariableX_Complete`、`VariableB_Precede`、`VariableX_Makespan`
- Precede 以 (Lot, Operation, Lot, Operation) 全笛卡兒積建立；constraint 只引用 lotA < lotB（字典序）的跨批次組合，其餘為未引用變數（presolve 自動剪除）
- Makespan 為 scalar：以單一成員 set（如 `["Total"]`）建立

## CONSTRAINT

### AssignOneEqp `[7. Exclusive XOR]` ∀ lot ∈ Lot, op ∈ Operation

$$\sum_{eqp \in Eqp} Assign_{lot,op,eqp} = 1$$

每道作業恰好指派到一台機台。

### CompleteDef `[2. Balance]` ∀ lot ∈ Lot, op ∈ Operation

$$Complete_{lot,op} = Start_{lot,op} + \sum_{eqp \in Eqp} ProcessTime_{lot,op,eqp} \cdot Assign_{lot,op,eqp}$$

完成時間 = 開始時間 + 所指派機台的加工時間（non-preemptive 由此隱含）。

### RoutePrecedence `[1. LB]` ∀ lot ∈ Lot, (op, nextOp) ∈ 相鄰道次（OP1→OP2）

$$Start_{lot,nextOp} \ge Complete_{lot,op}$$

同批次下一道作業必須等前一道完成。

### NoOverlapForward `[Either-Or (Big-M)]` ∀ lotA, lotB ∈ Lot (lotA < lotB), opA, opB ∈ Operation, eqp ∈ Eqp

$$Complete_{lotA,opA} \le Start_{lotB,opB} + BigM \cdot (3 - Precede_{lotA,opA,lotB,opB} - Assign_{lotA,opA,eqp} - Assign_{lotB,opB,eqp})$$

### NoOverlapBackward `[Either-Or (Big-M)]` ∀ lotA, lotB ∈ Lot (lotA < lotB), opA, opB ∈ Operation, eqp ∈ Eqp

$$Complete_{lotB,opB} \le Start_{lotA,opA} + BigM \cdot (2 + Precede_{lotA,opA,lotB,opB} - Assign_{lotA,opA,eqp} - Assign_{lotB,opB,eqp})$$

兩條合為 disjunctive：兩作業若指派到同一機台（兩個 Assign 皆 =1），依 Precede 二選一決定先後，不得重疊；
只要其中一個 Assign =0，Big-M 鬆弛使該條自動不生效。式中 3 與 2 為 Either-Or pattern 的結構常數（非數據）。
同批次作業已由 RoutePrecedence 排序，故只需跨批次（lotA < lotB）組合。

### MakespanDef `[max in objective → 輔助變數]` ∀ lot ∈ Lot

$$Makespan \ge Complete_{lot,LastOperation}$$

LastOperation = OP2（每批次最後一道）。

### MakespanWindow `[Range]`（示範 CreateRange，demo 值非綁定）

$$MakespanFloor \le Makespan \le MakespanDeadline$$

規劃窗：makespan 須落在 [MakespanFloor, MakespanDeadline]。demo 值 [4, 12] 皆非綁定（4 < 最佳 6 < 12，不改最佳解），僅示範 range 限制式（`CreateRange`）。

### MakespanTargetSoft `[Soft (Le)]`（示範軟性限制式）

$$Makespan \le SoftMakespanTarget \quad (\text{soft})$$

期望 makespan ≤ SoftMakespanTarget，允許違反。線性化：加彈性變數 $Overage \ge 0$，建 $Makespan - Overage \le SoftMakespanTarget$，並把 $MakespanPenalty \cdot Overage$ 併入目標式（框架 `CreateLeSoft` 自動處理）。demo：target = 5 < 最佳 6 → 被違反 $Overage = 1$ 小時。

### MakespanInfeasibleCap `[1. UB]`（模型 C 專用，保證 infeasible 的 IIS 示範）

$$Makespan \le InfeasibleMakespanCap$$

其中 $InfeasibleMakespanCap = \max_{lot} \sum_{op} \min_{eqp} ProcessTime_{lot,op,eqp} - 1$（任一 lot 的 makespan 理論下界取最大再減 1）。上限嚴格低於任何可行 makespan → 模型必定 Infeasible，用來觸發 CPLEX conflict 分析並輸出 IIS（`IISs/*.ilp`）。非業務限制，只掛在模型 C（= 模型 A + 本條）。

## OBJ

$$\min \; Makespan + MakespanPenalty \cdot Overage$$

硬性最小化目標仍是 Makespan；$Overage$ 為 MakespanTargetSoft 引入的違反量（$= \max(0, Makespan - SoftMakespanTarget)$），penalty 項來自 soft constraint。demo：$\min\; 6 + 2 \cdot 1 = 8$。無 soft 時退化為原始 $\min Makespan$。

## 已套用的預設假設（請一併確認）

1. **每批次固定 2 道作業（OP1→OP2）、所有機台皆可加工每道作業（total flexibility）**——BASIC 範本的最小示範規模；道次數不同或機台 eligibility 限制屬進階版
2. ProcessTime demo 數據為我編的示範值（單位小時），如有真實數據直接替換
3. 機台無可用時段限制（隨時可用）；作業無個別 release / due date（僅有一個「示範用」全域 soft makespan 目標 + range 規劃窗，非核心 FJSP，可移除）
4. 變數 LB=0, UB=INFTY（時間類），符合預設慣例
5. BigM = 32 由 demo 數據推導；數據換掉時 BigM 需同步重算（Coding 端由數據計算，不寫死）
