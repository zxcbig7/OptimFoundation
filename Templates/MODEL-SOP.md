# 模型層開發 SOP — AI 必 follow

適用：任何用 OptimFoundation 建的 LP / IP / MILP 專案。
範圍**只到模型**：Set、Parameter、Variable、Constraint、Objective、Dataload、Program 的組裝。
不含 CLI 參數、log 檔名、解的列印格式那類專案自訂行為。

canonical 參考實作：`Sudoku_SHC279/`（每個骨架都可在該專案找到對應檔）。

---

## 1. 七個必要元件

| # | 元件 | 位置 | 一句話職責 |
| --- | --- | --- | --- |
| 1 | Set | `SetClass/Sets.cs` | 索引集合（模型的維度）|
| 2 | Parameter | `ParameterClass/Parameter_*.cs` | 已知數值或 key 組合 |
| 3 | Variable | `VariableClass/Variable{B,X,I}_*.cs` | 決策變數，前綴決定型別 |
| 4 | Dataload | `Data/Dataload.cs` | 資料唯一入口 |
| 5 | Constraint | `Constraint/Constraint_*.cs` | 一條數學限制式一個類別 |
| 6 | Objective | `Constraint/ObjectiveFunction.cs` | 目標式 |
| 7 | Program | `Program.cs` | 唯一組裝點 |

缺任何一個都不算完整專案。可行性問題（無目標）仍 MUST 有 Objective，見 §7。

---

## 2. Set — `[OptSet<T>]` partial class

```csharp
using OptimFoundation.Modeling;

namespace <Project>.SetClass;

[OptSet<int>]
public partial class Set_<Name> { }
```

- 泛型參數是成員的**型別**（`int` / `string` / `DateTime`）。
- `partial` 不可省：成員由 source generator 補。
- 類別本體保持空的，NEVER 手寫成員欄位。

## 3. Parameter — `[OptParam]` + 每維一個 `[OptDim]`

```csharp
[OptParam]                        // 有數值 → 生成 QTY 欄位
[OptDim<Set_<A>>("<A>")]
[OptDim<Set_<B>>("<B>")]
public partial class Parameter_<Name> { }

[OptParam(HasValue = false)]      // 純 key 組合，不生成 QTY
[OptDim<Set_<A>>("<A>")]
public partial class Parameter_<Name> { }
```

- `[OptDim<TSet>("Xxx")]` 的字串是**產生的屬性名**，順序即 key 順序。
- 數值一律放 generator 產的 `QTY`，NEVER 自己加數值欄位。
- 模型裡出現的每個係數 MUST 是某個 Parameter，見 §8。

## 4. Variable — 前綴決定型別

```csharp
using OptimFoundation.Core;

namespace <Project>.VariableClass;

/// <summary>x[a,b] = 1 表示 …</summary>
public sealed class Variable<B|X|I>_<Name> : VariableBase
{
    public <T> <A> { get; set; }   // 每個註標一個 public 屬性
    public <T> <B> { get; set; }
}
```

| 前綴 | 型別 | Program 用的 builder |
| --- | --- | --- |
| `VariableB_` | Binary | `engine.BuildBVs<T>(setA, setB, …)` |
| `VariableX_` | Continuous | `engine.BuildCVs<T>(…)` |
| `VariableI_` | Integer | `engine.BuildIVs<T>(…)` |

前綴是 **load-bearing**（generator 依它判型），取錯名直接 compile error。NEVER 用 attribute 另外指定型別。

## 5. Dataload — `DataContext` 的 partial class

```csharp
public partial class Dataload : DataContext
{
    public Set_<A> <A> = new();
    public List<Parameter_<X>> parameter_<X> = new();

    public Dataload() : this(new CsvDataSource()) { }        // 預設入口

    public Dataload(IDataSource source)                      // 標準接口
    {
        <A>.Load(source, "Set_<A>");
        parameter_<X> = source.LoadParam<Parameter_<X>>("Parameter_<X>");
    }
}
```

- 唯一建構入口是 `OptData.Load(() => new Dataload())`，NEVER 直接 `new Dataload()` 用於求解。
- 載入後視為**唯讀**。`Freeze()` 只擋框架受控 API，直接寫 public field 不會被攔，所以專案 code 自己不能寫。
- 若原始資料格式不規則（矩陣、寬表），多開一個 ctor 把它攤平，再 `Export()` 寫回標準 CSV 當第二階段輸入；檔名 MUST 與 `IDataSource` ctor 讀的名稱一致。

## 6. Constraint — 一條數學式一個類別

```csharp
public sealed class Constraint_<Name> : ConstraintBase
{
    private readonly IReadOnlyList<<T>> _<setA>;
    private readonly IReadOnlyList<Parameter_<X>> _<paramX>;

    public Constraint_<Name>(IReadOnlyList<<T>> <setA>, IReadOnlyList<Parameter_<X>> <paramX>)
    {
        _<setA> = <setA>;
        _<paramX> = <paramX>;
    }

    public void Build(OptEngine engine)
    {
        foreach (var a in _<setA>)
        {
            var coefficient = _<paramX>.FirstOrDefault(p => p.<A> == a)?.QTY ?? 0.0;
            engine.AddLHS(coefficient, new Variable<B>_<Name> { <A> = a });

            engine.AddRHS(<rhsFromParameter>);
            engine.CreateLessEqual($"{ConstraintName}@{a}");
        }
    }
}
```

MUST：

- 建構子只收**實際用到**的 Set / Parameter / scalar —— 建構子簽名就是依賴清單。NEVER 傳整包 `Dataload`。
- 模型左側 → `AddLHS`、右側 → `AddRHS`。`>=`→`CreateGreatEqual`、`<=`→`CreateLessEqual`、`=`→`CreateEqual`。
- NEVER 移項、改號、翻轉比較方向、合併化簡 —— 轉譯必須能逐條對照數學式驗證。
- 係數查詢先存局部變數再傳入，NEVER 把 LINQ 內嵌進 `AddLHS(...)`。
- 限制式命名一律 `$"{ConstraintName}@{註標}@{註標}"`，讓 log 與 IIS 指得回具體那一條。
- RHS 是常數時可用 `CreateLessEqual(rhs, name)` 的多載省掉 `AddRHS`。

## 7. Objective — 一定要有

```csharp
public sealed class ObjectiveFunction
{
    public void Build(OptEngine engine)
    {
        foreach (var a in _<setA>)
            engine.AddLHS(<cost>, new Variable<X>_<Name> { <A> = a });

        engine.CreateMinimize();   // 或 CreateMaximize()
    }
}
```

可行性問題（只要找到可行解、沒有最佳化目標）也 MUST 建目標式——用零係數的 `CreateMinimize()`，讓建模生命週期仍有統一的開始／完成 log。

## 8. Program.cs — 唯一組裝點

```csharp
var data = OptData.Load(() => new Dataload());

var model = new OptModel("<ModelName>")
    .AddVariables(e => e.BuildBVs<Variable B_<A>>(data.<SetA>, data.<SetB>))
    .AddVariables(e => e.BuildCVs<VariableX_<B>>(data.<SetA>))
    .AddObjective(e => new ObjectiveFunction(<deps>).Build(e))
    .AddConstraints(e => new Constraint_<C1>(<deps>).Build(e))
    .AddConstraints(e => new Constraint_<C2>(<deps>).Build(e));
```

- 每種變數一行 `AddVariables`、目標式一行 `AddObjective`、**每條限制式各一行** `AddConstraints`。pipeline 本身就是模型組成清單。
- NEVER 建立只做轉呼叫的類別或 local function（`BuildModel`、`VariableCreate`、`XxxOptModel.Build()` 這類）把組裝順序藏起來。
- NEVER 在 fluent call 內直接寫 `AddLHS` / `AddRHS`——數學式留在 Constraint / Objective 類別內。
- 階段順序由 `OptModel` 保證（variables → objective → constraints），寫的順序不影響套用順序。
- 新增一條限制式 = Constraint/ 加一個檔 + pipeline 加一行，**沒有第三個地方要改**。

---

## 9. 硬規則總表

| 規則 | 說明 |
| --- | --- |
| NEVER 裸數字 | 係數、容量、比例一律經 Parameter 的 `QTY`。結構性常數（如「每格恰好一個」的 1）可直接寫，但**任何來自題目的數值**都不行 |
| NEVER 移項改號 | 見 §6 |
| NEVER 四捨五入 | 數值與題目描述完全一致，不推算、不填佔位符 |
| NEVER 傳整包 Dataload | 只出現在 `Program.cs` |
| NEVER 用單字母命名 | `Assign_{Employee,Date}` 而非 `x[i,j]`；程式類別名對應數學符號 |
| Set 成員字串 | PascalCase 單數：`"Truck"` ✅、`"trucks"` ❌ |
| 前綴決定型別 | `VariableB_` / `VariableX_` / `VariableI_`，取錯即 compile error |

## 10. 完成前的四步驗證

1. `dotnet build` 通過。
2. `Status` 分流判斷：Optimal / Infeasible / Unbounded。
3. 把解**代回每一條** constraint 檢查。
4. LP bound sanity：min 問題的整數解 ≥ LP bound，max 反之。

四步全過才可宣稱完成。Infeasible 時先跑 IIS 找最小衝突集合，NEVER 直接放寬限制式。

---

## 附註：兩處已知不一致（待收斂）

同一份框架目前有兩種寫法並存，本 SOP 採 `Sudoku_SHC279` 的版本：

| 項目 | 本 SOP（Sudoku） | 另一種（Template_CPLEX / `$FW` 規範文字） |
| --- | --- | --- |
| engine 傳入時機 | `new Constraint_X(deps).Build(engine)` | `new Constraint_X(deps, engine).Build()` |
| 資料夾命名 | `SetClass/` `ParameterClass/` `VariableClass/` `Constraint/` | `Set/` `Parameter/` `Variable/` `Objective/` `Constraint/` |

要讓 AI 產出穩定，這兩項應該擇一收斂後把另一種從 template 移除。
