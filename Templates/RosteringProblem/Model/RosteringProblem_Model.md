# RosteringProblem — 數學模型

員工排班 Binary/Continuous MILP。這份文件是把既有 `Template_CPLEX` 程式碼**逆向整理**成的模型文件（原專案沒有
`Model.md`，重構時依現有 `Constraint_*` 的實際邏輯還原）。轉譯時只重組程式結構，未更動任何係數、限制式邏輯或資料數值。

## 術語

| 詞                       | 定義（本模型採用）                                                                                                                                   |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| 員工（Employee）         | 可排班的人員，本範例資料共 16 人（E1..E16）                                                                                                          |
| 班別（Group）            | 排班的班別，含休假 "O" 與四個工作班別 D／E／N／C                                                                                                     |
| 日期（Date）             | 排班期間內的一天，本範例資料為 2026-01-01 至 2026-01-31                                                                                              |
| 跨組別（CrossGroup）     | 員工被排入非其主要班別、非休假、非 Backup 班別的班別                                                                                                 |
| 做休做（OffOneDay）      | 前一天休假、當天與前兩天皆工作的單日離峰休假型態                                                                                                     |
| 雙人連休（DoubleOffLT2） | 連續視窗內出現的連休型態；模型限制其累計次數低於門檻                                                                                                 |
| AND-linearization        | 用兩條不等式（`y ≤ 1-a`、`y ≥ 1-Σa`）線性表達「y=1 iff 所有 a 皆為 0」的標準手法，本模型 SixDayWork／NightToDay／OffOneDay／DoubleOffFlag 皆用此手法 |

## SET

| Set      | 語意           | 成員                               | → 程式         |
| -------- | -------------- | ---------------------------------- | -------------- |
| EMPLOYEE | 員工           | E1..E16（string）                  | `Set_Employee` |
| GROUP    | 班別（含休假） | O, D, E, N, C（string）            | `Set_Group`    |
| DATE     | 排班日期       | 2026-01-01..2026-01-31（DateTime） | `Set_Date`     |

## PARAM

| Param                                                                                                                                    | 語意                                  | Dim                   | 有值                                     | → 程式                          |
| ---------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------- | --------------------- | ---------------------------------------- | ------------------------------- |
| ShiftDemand                                                                                                                              | 每日各班別需求人數                    | Date, Group           | 是                                       | `Parameter_ShiftDemand`         |
| CrossGroup                                                                                                                               | 員工被視為跨組別的班別清單            | Employee, Group       | 是（QTY 未被讀取，見下方假設）           | `Parameter_CrossGroup`          |
| NightToDayRule                                                                                                                           | 視為違規的「前一天班別→當天班別」組合 | PreGroup, Group       | 是（QTY 未被讀取，見下方假設）           | `Parameter_NightToDay`          |
| PreAssign                                                                                                                                | 已預先指派的（日期,員工,班別）        | Date, Employee, Group | 是（QTY 恆為 0，未被讀取）               | `Parameter_PreAssign`           |
| BackupGroup                                                                                                                              | 員工的 Backup 班別設定                | Employee, Group       | 是（QTY 恆為 0；目前未被任何限制式讀取） | `Parameter_BackupGroup`         |
| One                                                                                                                                      | 結構常數「恰好一次／固定為 1」        | scalar                | 是                                       | `Parameter_One`                 |
| SixDayWindow                                                                                                                             | 連續工作天數上限視窗長度              | scalar                | 是                                       | `Parameter_SixDayWindow`        |
| NightToDayWindow                                                                                                                         | NightToDay 檢查所需歷史視窗長度       | scalar                | 是                                       | `Parameter_NightToDayWindow`    |
| OffOneDayWindow                                                                                                                          | OffOneDay 檢查所需歷史視窗長度        | scalar                | 是                                       | `Parameter_OffOneDayWindow`     |
| DoubleOffWindow                                                                                                                          | DoubleOffLT2 檢查所需歷史視窗長度     | scalar                | 是                                       | `Parameter_DoubleOffWindow`     |
| DoubleOffThreshold                                                                                                                       | 雙人連休次數門檻                      | scalar                | 是                                       | `Parameter_DoubleOffThreshold`  |
| WeekendOffThreshold                                                                                                                      | 員工週末休假天數上限門檻              | scalar                | 是                                       | `Parameter_WeekendOffThreshold` |
| OffOneDayPenalty / SixDayPenalty / GroupMismatchPenalty / NightToDayPenalty / DoubleOffLT2Penalty / BelowAVGPenalty / Weekend4DayPenalty | 對應旗標／缺口變數的目標式懲罰權重    | scalar                | 是                                       | `Parameter_*Penalty`            |

## VAR

| Var           | 語意                                     | Dim                   | 型別       | LB  | UB  |
| ------------- | ---------------------------------------- | --------------------- | ---------- | --- | --- |
| ShiftAssign   | 員工是否於該日排入該班別                 | Date, Employee, Group | Binary     | 0   | 1   |
| GroupMismatch | 員工是否於該日排入跨組別班別             | Date, Employee        | Binary     | 0   | 1   |
| NightToDay    | 員工是否於該日觸犯前後班別違規組合       | Date, Employee        | Binary     | 0   | 1   |
| DoubleOffFlag | 員工是否於該日出現雙人連休型態           | Date, Employee        | Binary     | 0   | 1   |
| DoubleOffLT2  | 員工雙人連休次數是否超過門檻             | Employee              | Binary     | 0   | 1   |
| Off1Day       | 員工是否於該日出現「做休做」型態         | Date, Employee        | Binary     | 0   | 1   |
| SixDayWork    | 員工是否連續工作滿視窗天數（結束於該日） | Date, Employee        | Binary     | 0   | 1   |
| BelowAVG      | 員工休假天數低於平均目標的缺口量         | Employee              | Continuous | 0   | +∞  |
| WeekendLT4    | 員工週末休假天數低於門檻的缺口量         | Employee              | Continuous | 0   | +∞  |

→ 程式：`VariableB_ShiftAssign` / `VariableB_GroupMismatch` / `VariableB_NightToDay` / `VariableB_DoubleOffFlag` /
`VariableB_DoubleOffLT2` / `VariableB_Off1Day` / `VariableB_SixDayWork` / `VariableC_BelowAVG` / `VariableC_WeekendLT4`

## CONSTRAINT

### FullfillDemand `[Coverage]` ∀ date ∈ DATE, group ∈ GROUP \ {O}

$$\sum_{employee} ShiftAssign_{date,employee,group} = ShiftDemand_{date,group}$$

### OneGroup `[Assignment]` ∀ date ∈ DATE, employee ∈ EMPLOYEE

$$\sum_{group} ShiftAssign_{date,employee,group} = One$$

每位員工每天恰好排入一個班別（含休假）。

### PreAssign `[Fix]` ∀ (date, employee, group) ∈ PreAssign

$$ShiftAssign_{date,employee,group} = One$$

### SixDayWork `[Linearization]` ∀ date, employee，視窗 W = {sd : date-SixDayWindow < sd ≤ date}（|W| = SixDayWindow 才生效）

$$\forall sd \in W:\ SixDayWork_{date,employee} \le One - ShiftAssign_{sd,employee,O}$$
$$SixDayWork_{date,employee} \ge One - \sum_{sd \in W} ShiftAssign_{sd,employee,O}$$

SixDayWork = 1 若且唯若視窗內連續 SixDayWindow 天皆未排休（AND-linearization）。

### NightToDay `[Linearization]` ∀ date, employee（需 NightToDayWindow 天歷史）, rule ∈ NightToDayRule

$$NightToDay_{date,employee} \ge ShiftAssign_{date-1,employee,rule.PreGroup} + ShiftAssign_{date,employee,rule.Group} - One$$

### OffOneDay `[Linearization]`（做休做）∀ date, employee（需 OffOneDayWindow 天歷史）

$$Off1Day_{date,employee} \ge (One - ShiftAssign_{date,employee,O}) + ShiftAssign_{date-1,employee,O} + (One - ShiftAssign_{date-2,employee,O}) - (OffOneDayWindow - One)$$

### CrossGroup `[Linearization]` ∀ date, employee, group ∈ CrossGroup(employee)

$$ShiftAssign_{date,employee,group} \le GroupMismatch_{date,employee}$$

### BelowAVG `[Balance]` ∀ employee

$$avgOff = \left\lfloor \frac{|EMPLOYEE|\cdot|DATE| - \sum_{ShiftDemand.Group \ne O} ShiftDemand}{|EMPLOYEE|} \right\rfloor - 1$$
$$\sum_{date} ShiftAssign_{date,employee,O} + BelowAVG_{employee} \ge avgOff$$

### WeekendLT4 `[Balance]` ∀ employee，weekends = {date : date.DayOfWeek ∈ {Sat,Sun}}

$$WeekendLT4_{employee} + \sum_{date \in weekends} ShiftAssign_{date,employee,O} \ge WeekendOffThreshold$$

### DoubleOffLT2 `[Linearization]` ∀ date，視窗 W = {sd : date-DoubleOffWindow < sd ≤ date}（|W| ≥ 2 才生效）, employee

$$|W|=2:\ DoubleOffFlag_{date,employee} \ge ShiftAssign_{date,employee,O} + ShiftAssign_{date-1,employee,O} - (|W|-One)$$
$$|W|=3:\ DoubleOffFlag_{date,employee} \ge ShiftAssign_{date,employee,O} + ShiftAssign_{date-1,employee,O} + One - ShiftAssign_{date-2,employee,O} - (|W|-One)$$

彙總（∀ employee）：

$$\sum_{date} DoubleOffFlag_{date,employee} + DoubleOffThreshold \cdot DoubleOffLT2_{employee} \ge DoubleOffThreshold$$

## OBJ

$$\min \sum_{date}\sum_{employee} \big(OffOneDayPenalty \cdot Off1Day + SixDayPenalty \cdot SixDayWork + GroupMismatchPenalty \cdot GroupMismatch + NightToDayPenalty \cdot NightToDay\big)$$
$$+ \sum_{employee} \big(DoubleOffLT2Penalty \cdot DoubleOffLT2 + BelowAVGPenalty \cdot BelowAVG + Weekend4DayPenalty \cdot WeekendLT4\big)$$

## 預設假設（重構時的判斷紀錄，供人工複核）

1. **CrossGroup.QTY／NightToDayRule.QTY／PreAssign.QTY／BackupGroup.QTY 目前未被任何 Constraint 讀取**——
   還原自原始程式碼：`Constraint_CrossGroup`／`Constraint_NightToDay`／`Constraint_PreAssign` 只用這些 Parameter 的
   **key 欄位**（Group／PreGroup／Employee 等）做集合過濾，QTY 值本身從未進入任何 `AddLHS`/`AddRHS`。
   為了不擅自更動數學語意，重構後保留這些 QTY 快照值（照原始生成邏輯算出），但明確記錄「目前未使用」，
   由使用者日後決定是否要把它們接進限制式或目標式。
2. `Parameter_BackupGroup` 目前只作資料紀錄，不影響求解——與原始程式碼行為一致（原本只在資料生成階段用來調整
   `CrossGroup.QTY`，但 `CrossGroup.QTY` 本身未被讀取，所以整條鏈路都不影響模型）。
3. `avgOff` 公式中的 `-1` 為原始程式碼既有的緩衝設計，重構未拆成獨立 Parameter（未被稽核清單列為違規項，
   且非 `AddLHS`/`AddRHS` 常數位字面數字，屬控制流程運算的一部分，保留原樣）。
4. `DoubleOffLT2` 的 `window.Count == 2` / `== 3` 分支條件為既有演算法對 `DoubleOffWindow=3` 這個特定值的
   手動展開（非 `AddLHS`/`AddRHS` 常數位），重構未嘗試將其推廣為任意視窗長度，維持原始程式碼的行為邊界。
5. 十條限制式皆為 hard constraint；模型未使用 soft constraint。
6. **限制式命名 bug 修正（影響求解結果，非模型語意變更）**：原始程式碼的 `Constraint_CrossGroup`／
   `Constraint_SixDayWork`／`Constraint_NightToDay` 在同一組 (date, employee) 內對多個 rule／視窗內多個
   sd 重複使用同一個限制式名稱（缺少 `rule.Group`／`sd`／`rule.PreGroup_rule.Group` 索引）。框架偵測到
   重複名稱時會靜默丟棄後續同名限制式（`kept_existing`），導致這三條限制式在原始程式碼中**只有一小部分真正
   進入 solver**（例如 NightToDay 原本 3840 條只剩 480 條、SixDayWork 原本 2912 條只剩 416 條）。
   這不符合本文件 ∀-量詞所定義的限制式集合，重構時已補上遺漏的索引使名稱唯一（見對應 `Constraint_*.cs`），
   讓程式碼確實實作 Model.md 所定義的完整限制式。修正後 objective 從（有 bug 版本的）1.6 變為 3.4——
   數值差異來自「原本被靜默丟棄的限制式現在真正生效」，不是本次重構更動了係數或限制式定義。

## 規模（本範例資料）

EMPLOYEE=16，GROUP=5，DATE=31。變數與限制式數量隨 `Constraint_SixDayWork` / `Constraint_NightToDay` 等視窗式限制式的
歷史門檻而略有差異，實際數量以 `dotnet run` 的 build summary 為準。
