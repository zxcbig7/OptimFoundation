---
title: Engine 層整合與統一 Solver 開發流程
status: approved
created: 2026-05-18
updated: 2026-05-18
modules: [OptimFoundation.Core, OptimFoundation.Cplex, OptimFoundation.Gurobi]
---

# Engine 層整合與統一 Solver 開發流程

## Summary

補齊 OMG_Foundation engine 層移植缺口（CPLEX soft constraints），並為 EngineBase 定義明確的 solver 開發契約，讓所有現有及未來的 solver 都有統一的實作規範與生命週期。

## Motivation / Why

1. CPLEX soft constraints（`CreateLeSoft/GeSoft/EqSoft`）目前只 `throw NotImplementedException`，Gurobi 已有實作，兩者 API 行為不一致。
2. Gurobi `Configuration` 目前 `throw NotImplementedException`，Engine 未完整可用。
3. `CplexConfig` 同時有 camelCase field（`workThreads`、`epGap`）與 PascalCase property（`Threads`、`MipGap`），同一參數兩條路徑，容易造成設定失效。
4. 沒有文件明確說明「新增一個 solver 最少需要實作哪些方法」，未來擴充無標準可循。

## Scope

### In Scope

- EngineBase 定義完整 abstract/virtual solver contract（含 soft constraints 的 virtual hooks）
- CPLEX `CreateLeSoft / CreateGeSoft / CreateEqSoft` 實作（penalty 法，對齊 Gurobi 做法）
- Gurobi `Configuration` 完成實作（移除 `NotImplementedException`）
- `CplexConfig` 整理：移除重複的 ISolverConfig PascalCase properties，統一用 camelCase fields

### Out of Scope

- DB / CSV 功能
- 新增第三方 solver（只定義契約，不實作新 solver）
- Frontend / UI

## User Stories / Use Cases

1. As a model builder, I want `CreateLeSoft/GeSoft/EqSoft` to work on CPLEX, so that I can use the same soft constraint API across all solvers.
2. As a developer adding a new solver, I want a clear list of abstract methods to implement, so that I don't miss any required functionality.
3. As a maintainer, I want `CplexConfig` to have one way to set each parameter, so that there is no ambiguity about which value takes effect.

## Acceptance Criteria

- [ ] CPLEX `CreateLeSoft(rhs, penalty)` 有實作，不 throw
- [ ] CPLEX `CreateGeSoft(rhs, penalty)` 有實作，不 throw
- [ ] CPLEX `CreateEqSoft(rhs, penalty, name)` 有實作，不 throw
- [ ] Gurobi `Configuration(ISolverConfig)` 有實作，不 throw `NotImplementedException`
- [ ] `CplexConfig` 移除與 `ISolverConfig` 重複的 PascalCase properties（`TimeLimit`、`MipGap`、`Threads`、`LogToConsole`、`LogFilePath`、`RootAlgorithm`、`NodeAlgorithm`、`PreIndicator`）
- [ ] EngineBase 有 solver contract 註解，列出 abstract 必實作清單與 virtual 可 override 清單
- [ ] 整個 solution build 成功，無 error

## Module Interactions

- **OptimFoundation.Core** (`EngineBase`, `ISolverEngine`, `ISolverConfig`)：加 virtual soft constraint hooks，加 contract 文件
- **OptimFoundation.Cplex** (`OptEngine`, `CplexConfig`)：實作 soft constraints，清理 config
- **OptimFoundation.Gurobi** (`OptEngine`)：完成 `Configuration` 實作

## API Design

### Solver Contract — abstract（每個 solver 必須實作）

```
Configuration(ISolverConfig)         // 初始化 Model + 套用所有參數
AddVariable(name, lb, ub, type)      // 新增單一變數
LinearExpr(terms)                    // 建立線性表達式物件
AddConstraint(name, lhs, sense, rhs) // 新增限制式
AddRangeConstraint(name, expr, lb, ub)
SetObjective(expr, sense)
SetVariableBounds(var, lb, ub)       // 直接修改 solver 變數界限
Build()                              // 呼叫 Configuration(Config)
Solve()                              // 求解，回傳是否成功
GetObjectiveValue()
GetVariableValue(name)
Dispose()
```

### Solver Contract — virtual（可 override，EngineBase 有 default 實作）

```
// VariableManager
BuildCVs<T>(sets)
BuildIVs<T>(sets)
BuildBVs<T>(sets)
BuildCVs<T>(lb, ub, sets)
BuildIVs<T>(lb, ub, sets)

// Soft constraints（default: throw NotImplementedException）
CreateLeSoft(rhs, penalty)     // Pool LHS <= rhs，penalty 懲罰
CreateGeSoft(rhs, penalty)     // Pool LHS >= rhs，penalty 懲罰
CreateEqSoft(rhs, penalty, name) // Pool LHS == rhs，slack var 鬆弛
```

### Soft Constraints 實作策略（Penalty 法）

CPLEX 對齊 Gurobi 的 penalty 做法：

**CreateLeSoft**（minimize 模型）
```
obj += max(0, LHS - rhs) * penalty
→ 等效：目標式加入 (LHS - rhs) * penalty 項
```

**CreateGeSoft**（minimize 模型）
```
obj += max(0, rhs - LHS) * penalty
→ 等效：目標式加入 (rhs - LHS) * penalty 項
```

**CreateEqSoft**
```
建 delta_pos, delta_neg >= 0
LHS + delta_neg - delta_pos = rhs
obj += (delta_pos + delta_neg) * penalty
```

### CplexConfig 整理

移除（重複，在 ISolverConfig 已有同等意義）：
```diff
- public double? TimeLimit { get; set; }
- public double? MipGap { get; set; }
- public int? Threads { get; set; }
- public bool LogToConsole { get; set; }
- public string LogFilePath { get; set; }
- public int? RootAlgorithm { get; set; }
- public int? NodeAlgorithm { get; set; }
- public bool? PreIndicator { get; set; }
```

保留（CPLEX 專屬）：
```
workThreads, enableLog, exportLP, exportSol, exportMPS
rowRead, workMemory, epGap, nodeSelect, randomSeed
epOpt, epRHS, timeLimit, polishAfterTime
mipEmphasis, varSel, algorithm, nodeFileInd
```

## Edge Cases & Error Handling

- `CreateEqSoft` 需在 `Model` 已初始化後才能建 slack variables（必須在 `Build()` 之後呼叫）
- Soft constraints 使用 penalty 法，不保證嚴格滿足（這是 soft constraint 的預期行為）
- CPLEX `Build()` 呼叫 `Configuration(Config)`，`Configuration` 負責建立 `Model`；子類別 override `Build()` 時 **必須先呼叫 `base.Build()`**
- 移除 CplexConfig PascalCase properties 後，若有外部程式碼使用，會出現 compile error（屬預期內的 breaking change，一次性修正）

## Non-Functional Requirements

- **Build 正確性**：solution 內所有專案 build 無 error、無 warning
- **API 一致性**：CPLEX 與 Gurobi 的 soft constraint 方法簽名完全相同
- **不破壞 Pool API**：EngineBase `AddLHS / AddRHS / CreateEqual` 等方法行為不變

## Open Questions

- [ ] `CreateLeSoft/GeSoft/EqSoft` 是否要加入 `ISolverEngine` interface，讓 consumer 可透過 interface 呼叫？（目前為 `protected`）

## Implementation Plan

### Stub 階段（先做）

- [ ] EngineBase：`CreateLeSoft/GeSoft/EqSoft` 改為 `virtual`（default 仍 throw，各 solver override）
- [ ] CPLEX OptEngine：加 soft constraint method stubs（`throw new NotImplementedException()`）
- [ ] Gurobi OptEngine：`Configuration` stub（空 body，不 throw）
- [ ] CplexConfig：移除重複 PascalCase properties
- [ ] Build 確認結構正確

### 逐層實作

- [ ] Gurobi `Configuration` — 移植 `Build()` 內的 `GurobiConfig` 設定邏輯
- [ ] CPLEX `CreateLeSoft` — penalty 項加入目標式
- [ ] CPLEX `CreateGeSoft` — 同上（方向相反）
- [ ] CPLEX `CreateEqSoft` — 建 slack vars + penalty
- [ ] EngineBase contract 註解 — 在 abstract region 加 solver checklist 說明

## References

- 先前規格：`specs/2026-05-17-enginebase-pool-api.md`
- OMG_Foundation 參考：`OMG_Foundation/CplexEngine.cs`（soft constraint 原始做法）
- Gurobi soft constraint 現有實作：`OptimFoundation.Gurobi/OptEngine.cs`（`CreateLeSoft/GeSoft/EqSoft`）
