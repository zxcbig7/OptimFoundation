---
title: EngineBase Pool API — 統一限制式建構介面
status: implementing
created: 2026-05-17
updated: 2026-05-17
modules: [backend]
---

## Summary

在 `EngineBase<TModel, TVar, TExpr, TConstr>` 加入 Pool-based 限制式建構 API
（`AddLHS` / `AddRHS` / `CreateGreatEqual` 等），讓 CPLEX、Gurobi 及未來所有
solver 透過繼承自動獲得此能力。

> **命名說明**：新框架統一使用 `AddLHS` / `AddRHS`（PascalCase）。
> 舊 Template 呼叫 `AddPool` / `AddPoolRHS`（camelCase），遷移時一併替換；
> Template 遷移屬 out-of-scope，另開 spec。

## Motivation / Why

- 新 `OptimFoundation.Cplex/OptEngine.cs` 缺少整組 Pool API
- `OptimFoundation.Gurobi/OptEngine.cs` 有類似機制但命名不同（舊 `AddLhs`/`CommitGE`），引擎間不一致
- 放在 `EngineBase` 使第三個 solver 繼承即用，不需重複實作

## Scope

### In Scope

- `EngineBase.cs` 加入 Pool state、`AddLHS` / `AddRHS` 所有 overload、
  `CreateGreatEqual` / `CreateLessEqual` / `CreateEqual` / `CreateRange`、
  `CreateMinimize` / `CreateMaximize`、`ClearPool`、`HasPool`
- `EngineBase.cs` 新增 `protected abstract TConstr AddRangeConstraint(...)` 抽象方法
- `OptimFoundation.Cplex/OptEngine.cs` 實作 `AddRangeConstraint`
- `OptimFoundation.Gurobi/OptEngine.cs` 移除舊 Pool（`AddLhs`/`AddRhs`/`CommitLE/GE/EQ/Range`）；
  實作 `AddRangeConstraint`；舊便捷方法暫以 `[Obsolete]` 標記（待 Template 遷移後刪）
- Soft constraint stub（`CreateLESoft`/`CreateGESoft`/`CreateEqSoft`）— body `throw new NotImplementedException()`

### Out of Scope

- Template 呼叫端遷移（`AddPool` → `AddLHS`、`AddPoolRHS` → `AddRHS`），另開 spec
- Soft constraint 實作邏輯
- 其他 solver（SCIP、HiGHS）的 `AddRangeConstraint` 實作

## User Stories / Use Cases

1. As a constraint developer, I want to call `optEngine.AddLHS(1, new VariableB_ShiftAssign(d, e, g))` on any `OptEngine`, so that constraints are built with a solver-agnostic API.
2. As a framework maintainer, I want Pool logic in `EngineBase`, so that future solver implementations inherit it for free.
3. As a Gurobi user, I want `AddLHS`/`CreateGreatEqual` to work the same as CPLEX, so that I can switch engines by changing one project reference.

## Acceptance Criteria

- [ ] `EngineBase` 有 Pool state：`_lhsTerms`、`_rhsTerms`（`List<(double coef, TVar var)>`）、`_lhsConst`、`_rhsConst`
- [ ] `HasPool`（public property）：`_lhsTerms.Count > 0 || _rhsTerms.Count > 0`
- [ ] `ClearPool()`（public）清除四個 pool field
- [ ] `CheckHasPool()`（private）移植舊版三條 guard（見 API Design 說明）
- [ ] `AddLHS(double coeff, object varSpec)` — LHS 加入 `Variables[varSpec.ToString()]` * coeff
- [ ] `AddLHS(double constant)` — LHS 加入常數項
- [ ] `AddRHS(double coeff, object varSpec)` — RHS 加入 var * coeff
- [ ] `AddRHS(double constant)` — RHS 加入常數項
- [ ] `CreateGreatEqual(string name)` — LHS >= RHS，guard → 建立限制式 → 清 pool
- [ ] `CreateGreatEqual(double rhs, string name)` — LHS >= 常數 rhs（舊版相容 overload）
- [ ] `CreateLessEqual(string name)` 同上模式
- [ ] `CreateLessEqual(double rhs, string name)` 同上
- [ ] `CreateEqual(string name)` 同上
- [ ] `CreateEqual(double rhs, string name)` 同上
- [ ] `CreateRange(double lb, double ub, string name)` — lb <= LHS <= ub，呼叫 `AddRangeConstraint`
- [ ] `CreateMinimize()` / `CreateMaximize()` — 以 LHS pool 建目標式
- [ ] `EngineBase` 新增 `protected abstract TConstr AddRangeConstraint(string name, TExpr expr, double lb, double ub)`
- [ ] `OptimFoundation.Cplex/OptEngine.cs` 實作 `AddRangeConstraint`（`Model.AddRange(lb, expr, ub, name)`）
- [ ] `OptimFoundation.Gurobi/OptEngine.cs` 實作 `AddRangeConstraint`；舊 `AddLhs`/`AddRhs`/`CommitLE`/`CommitGE`/`CommitEQ`/`CommitRange` 加 `[Obsolete]`
- [ ] `dotnet build OptimFoundation.sln` 無 error（CPLEX 環境）
- [ ] `dotnet build OptimFoundation.Core` 無 error（無 solver 環境）

## Module Interactions

- **OptimFoundation.Core / EngineBase.cs**：Pool state + 全部 Pool API（solver-agnostic）
- **OptimFoundation.Cplex / OptEngine.cs**：僅新增 `AddRangeConstraint` override
- **OptimFoundation.Gurobi / OptEngine.cs**：新增 `AddRangeConstraint` override；舊 Pool 方法 `[Obsolete]`
- **Template（暫不動）**：遷移時 `AddPool` → `AddLHS`、`AddPoolRHS` → `AddRHS`

## API Design

### Pool State（EngineBase 私有欄位）

```csharp
private readonly List<(double coef, TVar var)> _lhsTerms = new List<(double, TVar)>();
private readonly List<(double coef, TVar var)> _rhsTerms = new List<(double, TVar)>();
private double _lhsConst = 0;
private double _rhsConst = 0;
```

### CheckHasPool（移植說明）

舊版三條 guard：

1. `variablePoolLHS.Count == 0 && variablePoolRHS.Count == 0` → return false
2. `variablePoolLHS.Count != scalePoolLHS.Count` → return false
3. `variablePoolRHS.Count != scalePoolRHS.Count` → return false

新版以 tuple 儲存，coef/var 永遠成對，guard 2 & 3 結構上不可能觸發。
移植策略：保留 guard 1；guard 2 & 3 以 comment 說明。

```csharp
private bool CheckHasPool()
{
    if (_lhsTerms.Count == 0 && _rhsTerms.Count == 0) return false;
    // guard 2 & 3: coef/var stored as tuples — count mismatch structurally impossible
    return true;
}
```

### AddLHS / AddRHS

```csharp
// LHS
public bool AddLHS(double coeff, object varSpec);  // 主要用法：AddLHS(1, new VarB_ShiftAssign(...))
public bool AddLHS(double constant);               // 常數項

// RHS
public bool AddRHS(double coeff, object varSpec);
public bool AddRHS(double constant);               // 常數項：AddRHS(-1)
```

> 舊 Template 對應：`AddPool` → `AddLHS`、`AddPoolRHS` → `AddRHS`

### CreateXxx 內部實作邏輯

`CreateGreatEqual(string name)` 實作：

```text
combined = _lhsTerms + [ (-coef, var) for each in _rhsTerms ]
netRhs   = _rhsConst - _lhsConst
AddConstraint(name, LinearExpr(combined), ConstraintSense.GreaterEqual, netRhs)
ClearPool()
```

將 `LHS >= RHS` 轉換為 `(LHS − RHS) >= 0`，複用現有 `AddConstraint(expr, sense, double)` 簽名，
不需新增 expr-vs-expr 抽象方法（`CreateRange` 除外）。

### 新增抽象方法

```csharp
protected abstract TConstr AddRangeConstraint(string name, TExpr expr, double lb, double ub);
```

| Engine | 實作 |
| --- | --- |
| CPLEX | `Model.AddRange(lb, expr, ub, name)` |
| Gurobi | `Model.AddRange(expr, lb, ub, name)` |

### CreateMinimize / CreateMaximize

```csharp
public void CreateMinimize()
{
    if (_lhsTerms.Count == 0) { Logging.Warn("[OptEngine] CreateMinimize: LHS pool is empty"); return; }
    SetObjective(LinearExpr(_lhsTerms), ObjectiveSense.Minimize);
    ClearPool();
}
```

> `_lhsConst` 常數項在目標式暫不支援（Template 未使用）。

### Soft Constraints（Stub Only）

```csharp
public bool CreateLESoft(double rhs, double multiplier)
    => throw new NotImplementedException("Soft constraints not yet implemented");
public bool CreateGESoft(double rhs, double multiplier)
    => throw new NotImplementedException("Soft constraints not yet implemented");
public bool CreateEqSoft(double rhs, double multiplier, string name)
    => throw new NotImplementedException("Soft constraints not yet implemented");
```

## Data Model

不涉及 DB schema。

## Edge Cases & Error Handling

| 情境 | 處理 |
| --- | --- |
| `varSpec.ToString()` 找不到 key | `KeyNotFoundException`（與舊版行為一致） |
| `AddLHS` / `AddRHS` varSpec 為 null | return false（不加入 pool） |
| `CreateGreatEqual` 前 pool 為空 | `CheckHasPool` return false，不建限制式 |
| Gurobi stub 被呼叫 | `AddRangeConstraint` stub 內 throw `NotSupportedException` |

## Non-Functional Requirements

- **Solver-agnostic**：Pool 邏輯 100% 在 Core，不引任何 solver DLL
- **Build**：`OptimFoundation.Core` 在無 solver 環境單獨 build 通過
- **Naming**：`AddLHS` / `AddRHS` PascalCase；breaking change 記錄於 MERGE-DECISIONS.md

## Open Questions

- [ ] `_lhsConst` 在目標式的支援：是否擴展 `SetObjective(expr, constant, sense)`？（defer）
- [ ] Gurobi 舊 Pool 方法（`AddLhs`/`CommitGE` 等）何時實際刪除？（等 Template 遷移完成）

## Implementation Plan

### Stub 階段

- [ ] `EngineBase.cs`：加入 Pool state fields
- [ ] `EngineBase.cs`：加入 `HasPool`、`ClearPool()`、`CheckHasPool()`（private）
- [ ] `EngineBase.cs`：加入 `AddLHS` / `AddRHS` 所有 overload（body: `throw new NotImplementedException`）
- [ ] `EngineBase.cs`：加入 `CreateGreatEqual` / `CreateLessEqual` / `CreateEqual` / `CreateRange` / `CreateMinimize` / `CreateMaximize`（body: TODO）
- [ ] `EngineBase.cs`：加入 `CreateLESoft` / `CreateGESoft` / `CreateEqSoft`（body: `throw new NotImplementedException`）
- [ ] `EngineBase.cs`：加入 `protected abstract TConstr AddRangeConstraint(...)`
- [ ] `OptimFoundation.Cplex/OptEngine.cs`：加入 `AddRangeConstraint` stub（`throw new NotImplementedException`）
- [ ] `OptimFoundation.Gurobi/OptEngine.cs`：加入 `AddRangeConstraint` stub；舊 Pool 方法加 `[Obsolete]`
- [ ] `dotnet build` 確認無 error

### 逐層實作

- [ ] `EngineBase.cs`：`AddLHS` / `AddRHS` 實作（lookup `Variables[varSpec.ToString()]`）
- [ ] `EngineBase.cs`：`CheckHasPool` 完整 guard
- [ ] `EngineBase.cs`：`CreateGreatEqual` / `CreateLessEqual` / `CreateEqual` 實作（combined expr）
- [ ] `EngineBase.cs`：`CreateRange` 實作（呼叫 `AddRangeConstraint`）
- [ ] `EngineBase.cs`：`CreateMinimize` / `CreateMaximize` 實作
- [ ] `OptimFoundation.Cplex/OptEngine.cs`：`AddRangeConstraint` 完整實作
- [ ] `OptimFoundation.Gurobi/OptEngine.cs`：`AddRangeConstraint` 完整實作
- [ ] 更新 `MERGE-DECISIONS.md`

## References

- 來源：`OMG_Foundation/CplexEngine.cs`（line 524–1105）
- 介面宣告：`OMG_MILP Development APIs/Interfaces.cs`（line 199–200）
- 呼叫端：`Template/Constraints/Constraint_*.cs`、`Template_New/Constraints/Constraint_*.cs`
- 受影響的既有規格：[2026-05-14-solver-abstraction-merge.md](2026-05-14-solver-abstraction-merge.md)
