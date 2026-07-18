# Tutorial_Model — 多期多班次家具生產規劃（框架全元素展示）

Phase 1 產物。本範本刻意涵蓋框架**所有建模元素**，作為教學靶心：
- **Set 三種元素型別**：string（Product、Machine）、DateTime（Date）、int（Shift）
- **Param 多維**：1D / 2D / 3D 都有
- **Var 三種型別 + 多維，且全部有用到**：`VariableX_`（continuous）/ `VariableB_`（binary）/ `VariableI_`（integer）
- **限制式四種**：`≤`（CreateLessEqual）/ `≥`（CreateGreatEqual）/ `=`（CreateEqual）/ soft 放鬆（CreateLeSoft）

## 題目（去故事化）

家具廠在數個生產日、每日兩班次生產多種產品。每種產品在每台機器耗用工時，機器每班有工時上限。
每種產品每日有需求下限須滿足；生產以「批」為單位（每日總產量 = 整數批數 × 每批量）；生產某產品前須開線（一次性成本）。
另有換線預算為軟性目標（超過以 penalty 計）。求利潤最大的生產計畫。

## SET

- $Product = \{\text{Desk}, \text{Chair}, \text{Table}\}$ ── string
- $Machine = \{\text{Cutting}, \text{Assembly}\}$ ── string
- $Date = \{\text{2026-08-01}, \text{2026-08-02}\}$ ── **DateTime**
- $Shift = \{1, 2\}$ ── **int**

## PARAM（值進 QTY，經 Dataload 取得）

| 符號 | 意義 | 維度 |
| --- | --- | --- |
| $UnitProfit_{Product}$ | 單位利潤 | Product（1D） |
| $SetupCost_{Product}$ | 開線一次性成本 | Product（1D） |
| $BatchSize_{Product}$ | 每批數量 | Product（1D） |
| $MachineHours_{Product,Machine}$ | 每件耗用工時 | Product × Machine（**2D**） |
| $Demand_{Product,Date}$ | 每日需求下限 | Product × Date（**2D**） |
| $Capacity_{Machine,Date,Shift}$ | 每機每日每班工時上限（日班高、夜班低，Shift 維度實質影響） | Machine × Date × Shift（**3D**） |

衍生（由數據推導、NEVER 寫死）：$BigM = \dfrac{\max Capacity}{\min\{MachineHours>0\}}$（單班單品產量上界）；$SetupBudget = 4$、$SetupPenalty = 5$（soft demo）。

## VAR

| 符號 | 型別 | 意義 | 程式類別 | 維度 |
| --- | --- | --- | --- | --- |
| $Produce_{Product,Date,Shift} \ge 0$ | continuous | 生產量 | `VariableX_Produce` | 3D |
| $Setup_{Product,Date,Shift} \in \{0,1\}$ | binary | 是否開線 | `VariableB_Setup` | 3D |
| $Batch_{Product,Date} \in \mathbb{Z}_{\ge 0}$ | integer | 每日生產批數 | `VariableI_Batch` | 2D |

## CONSTRAINT

**Capacity**［≤；LB 資源上限］ $\forall m, d, s$：

$$\sum_{p} MachineHours_{p,m} \cdot Produce_{p,d,s} \le Capacity_{m,d,s}$$

**Demand**［≥；需求下限］ $\forall p, d$：

$$\sum_{s} Produce_{p,d,s} \ge Demand_{p,d}$$

**BatchDef**［=；連結 integer 與 continuous］ $\forall p, d$：

$$\sum_{s} Produce_{p,d,s} = BatchSize_{p} \cdot Batch_{p,d}$$

**SetupLink**［≤；fixed-charge Big-M］ $\forall p, d, s$：

$$Produce_{p,d,s} \le BigM \cdot Setup_{p,d,s}$$

**SetupBudgetSoft**［soft 放鬆；換線預算，超過以 penalty 計］：

$$\sum_{p,d,s} Setup_{p,d,s} \;\le\; SetupBudget \quad(\text{soft, penalty} = SetupPenalty)$$

## OBJ

$$\max \sum_{p,d,s} UnitProfit_{p} \cdot Produce_{p,d,s} \;-\; \sum_{p,d,s} SetupCost_{p} \cdot Setup_{p,d,s}$$

（soft 違反量的 penalty 由框架 `CreateLeSoft` 於 maximize 目標自動以 $-penalty$ 併入。）

## 建模自驗（元素涵蓋檢查）

- Set：string ✔ DateTime ✔ int ✔
- Param：1D ✔ 2D ✔ 3D ✔
- Var：`X_` continuous（Produce）✔ `B_` binary（Setup）✔ `I_` integer（Batch）✔，皆有出現在約束/目標 ✔
- 限制式：`≤`（Capacity/SetupLink）✔ `≥`（Demand）✔ `=`（BatchDef）✔ soft（SetupBudgetSoft）✔
- 每個 PARAM 都有 CSV 來源、無佔位；BigM 由數據推導
- 預期：Optimal，objective = 500；需滿足 3 產品 × 2 日需求 → 開線數 > SetupBudget(4) → soft 違反現形（penalty 進目標）
- 日班 / 夜班產能不同 → 打破 shift 對稱，solver 較快證出最優（教學範本求解穩定性考量）
