---
title: 文件與 AI-Modeling 專案遷移到新 config / runner API
status: draft
created: 2026-08-01
updated: 2026-08-01
modules: [docs, ai-modeling-projects, dlls]
depends_on: [2026-08-01-optimfoundation-dual-config.md, 2026-08-01-optimfoundation-runner-symmetry.md]
---

# Docs & Projects Migration

## Summary

前兩份規格把框架改完但**刻意不動 `AI-Modeling/`**。本規格負責善後三件事，順序不可調換：

1. **文件**（約 32 份）改成描述新 API —— 其中 15 份是 **AI 引導**，不改的話後續 agent 會照舊架構生 code
2. **`AI-Modeling/dlls/`** 換上新 DLL（換完 8 個練習專案會全部編不過，這是預期狀態）
3. **17 個 `.cs`** 遷移（Projects 16 + Template 1），同時完成 config 遷移與 runner 改名，**一刀改完不分兩次**

## Motivation / Why

- 前兩份規格完成後，`src/` 與 `AI-Modeling/dlls/` 版本不一致。這是刻意的 —— 讓 8 個練習專案在文件還沒改好前保持可用
- **AI 引導文件是 code 的上游**：`automated/Prompts/*` 與 `claudemdTemplate/*` 會被 agent 讀來生成新專案。它們沒改就派 agent 遷移專案，等於一邊改一邊被舊規範帶回去
- 17 個 `.cs` 的 config 遷移與 runner 改名**必須同一刀**：分兩次的話，8 個 `ExperimentRunner.cs` 第一次的遷移工完全白做（第二次直接刪檔）

## Scope

### In Scope

- 文件更新：Tier 1（15）+ Tier 2（2）+ Tier 3（9）+ Tier 4（8）= 34 份
- Tier 5（8 份歷史 spec）：只加 `superseded_by` frontmatter，**不逐字改內容**
- rebuild `OptimFoundation.Core` / `.Cplex` → 覆蓋 `AI-Modeling/dlls/` 六個 DLL + `VERSION.txt` 解 pin
- 遷移 `AI-Modeling/Projects/*` 16 檔 + `AI-Modeling/Template/Program.cs`
- 刪除 8 個 `ExperimentRunner.cs`

### Out of Scope

- 框架 `src/` 的任何改動（前兩份規格已完成）
- `HISTORY/` 底下的舊 code 與文件
- `Projects/WoodworkingShop/`：**無 `.csproj`，空殼資料夾**，不列入遷移
- 新增練習專案 / 改任何模型的數學內容

## 前置：一件已完成但文件未同步的事

`AI-Modeling/Template` 的 **Constraint / Objective 與 Dataload 解耦**已於 2026-08-01 實作完成並驗證（build + solve + experiment 全過，目標值 158、限制式 93/155/5/125/305/5/5），但下列文件的文字**尚未同步**：

- `$FW/MILP Model/optimfoundation-api-guide.md` §4.2 / §4.3 / §5
- `$FW/MILP Model/Foundation Coding/CLAUDE.md`
- `$OPT/AI-Modeling/Template/Constraint/CLAUDE.md`
- `$OPT/AI-Modeling/Template/Objective/CLAUDE.md`

改動內容：`Constraint_*` / `ObjectiveFunction` 的建構子**不再收整包 `Dataload`**，只收自己用得到的 Set 積木、Parameter 清單與界限值；`Dataload` 只出現在 `Program.cs`。

MUST 在 Packet 2a 一併處理，否則新舊兩種寫法會並存在同一份文件裡。

## 檔案清單

### Tier 1 — AI 引導（15 份）★ 最高優先，是 Packet 4 的前置

| # | 路徑 | 要改什麼 |
| --- | --- | --- |
| 1 | `$OPT/AI-Modeling/automated/Prompts/10_VarCreateCode.md` | `VariableCreate.cs` → `OptModel.AddVariables` |
| 2 | `$OPT/AI-Modeling/automated/Prompts/11_BuildModelCode.md` | `BuildModel.cs` → `AddObjective` + `AddConstraints` |
| 3 | `$OPT/AI-Modeling/automated/Prompts/12_ProjectCode.md` | `new OptModel(name)` → 模型定義 + `OptProject` |
| 4 | `$OPT/AI-Modeling/automated/Prompts/13_ProgramCode.md` | 四個輸出開關 → `ProjectConfig` |
| 5 | `$OPT/AI-Modeling/automated/CLAUDE.md` | 16-stage pipeline 的階段說明 |
| 6 | `$OPT/AI-Modeling/claudemdTemplate/Root/CLAUDE.md` | 專案結構、Program.cs 骨架 |
| 7 | `$OPT/AI-Modeling/claudemdTemplate/Variable/CLAUDE.md` | 刪 `VariableCreate` 規範 |
| 8 | `$OPT/AI-Modeling/claudemdTemplate/Constraint/CLAUDE.md` | 刪 `BuildModel` 規範；建構子解耦 |
| 9 | `$OPT/AI-Modeling/claudemdTemplate/Experiment/CLAUDE.md` | `ExperimentRunner` → `OptExperiment` |
| 10 | `$OPT/AI-Modeling/interactive/phase-2-coding.md` | 專案結構 + Program.cs 骨架 |
| 11 | `$OPT/AI-Modeling/interactive/phase-3-tuning.md` | Experiment API |
| 12 | `$FW/MILP Model/optimfoundation-api-guide.md` | §4 §5 §7 全面改寫 + 前置解耦同步 |
| 13 | `$FW/MILP Model/Foundation Coding/CLAUDE.md` | Program.cs 骨架 + 前置解耦同步 |
| 14 | `$FW/MILP Model/Foundation Tuning/CLAUDE.md` | Experiment API |
| 15 | `$FW/workflow skill/SKILL.md` | Phase 2 轉譯順序 |

### Tier 2 — API 權威（2 份）

| # | 路徑 | 要改什麼 |
| --- | --- | --- |
| 16 | `$OPT/AI-Modeling/CPLEX_API_REFERENCE.md` | §4（**含修正 `enableLog` 預設值：文件寫 `false`，原始碼是 `true`**）、§12、§14、§16、附錄速查卡 |
| 17 | `$OPT/OptimFoundation/OptimFoundation/specs/developer-guide.md` | 13 章 API 手冊全面對照 |

### Tier 3 — 教學 / 導覽 / 規範（9 份）

| # | 路徑 |
| --- | --- |
| 18 | `$OPT/AI-Modeling/README.md` |
| 19 | `$OPT/AI-Modeling/CodeMap.md` |
| 20 | `$OPT/AI-Modeling/ROADMAP.md` |
| 21 | `$OPT/AI-Modeling/tuning/CLAUDE.md` |
| 22 | `$OPT/AI-Modeling/tutorial/ai-modeling-framework-tutorial.md` |
| 23 | `$OPT/AI-Modeling/tutorial/development-workflow.md` |
| 24 | `$OPT/OptimFoundation/OptimFoundation/specs/cplex-project-dev-spec.md` |
| 25 | `$OPT/OptimFoundation/OptimFoundation/specs/framework-dev-spec.md` |
| 26 | `$FW/CodeMap.md` |

### Tier 4 — 專案自述（8 份）→ 併入 Packet 4，與 code 同時改

`$OPT/AI-Modeling/` 底下：`Template/CLAUDE.md`、`Template/Variable/CLAUDE.md`、`Template/Constraint/CLAUDE.md`、`Template/Objective/CLAUDE.md`、`Projects/CLAUDE.md`、`Projects/FactorioOptimization/CLAUDE.md`、`Projects/HospitalRostering_Generator/CLAUDE.md`、`Projects/HospitalRostering_Manual/CLAUDE.md`、`Projects/MaxWeightIndependentSet/CLAUDE.md`

### Tier 5 — 歷史 spec（只加 frontmatter）

`$OPT/AI-Modeling/specs/`：`2026-07-07-tech-doc-story-loop.md`、`2026-07-21-newcomer-onboarding.md`、`2026-07-22-tutorial-reference-architecture.md`
`$OPT/OptimFoundation/OptimFoundation/specs/`：`2026-06-18-experiment-tuning-tracking.md`、`2026-07-13-optset-basic-objects.md`
`$FW/specs/`：`2026-07-14-ai-modeling-governance-wave2.md`（D13 定版語法表已被本三份規格取代）

### 程式碼 — 17 個 `.cs`

| 專案 | 檔案 | 動作 |
| --- | --- | --- |
| ClinicVitamin | `Program.cs` / `ExperimentRunner.cs` | 改寫 / **刪除** |
| FactorioOptimization | `Program.cs` / `ExperimentRunner.cs` | 改寫 / **刪除** |
| GlassFactory | `Program.cs` / `ExperimentRunner.cs` | 改寫 / **刪除** |
| HospitalRostering_Generator | `Program.cs` / `ExperimentRunner.cs` | 改寫 / **刪除** |
| HospitalRostering_Manual | `HospitalRosteringProblem.cs` / `ExperimentRunner.cs` | 改寫 / **刪除** |
| MaxWeightIndependentSet | `Project/Project.cs` / `Project/ExperimentRunner.cs` | 改寫 / **刪除** |
| SandwichProduction | `Program.cs` / `ExperimentRunner.cs` | 改寫 / **刪除** |
| WeeniesBuns | `Program.cs` / `ExperimentRunner.cs` | 改寫 / **刪除** |
| Template | `Program.cs` | 改寫 |

> `HospitalRostering_Manual` 是**手寫後路架構**（`XxxProblem.Execute()`），不是 `Program.cs`；`MaxWeightIndependentSet` 的 code 在 `Project/` 子資料夾。這兩個是例外，agent 要特別留意。

## before / after pattern

### Pattern A — solve 模式

```csharp
// ── BEFORE ──
if (args.Contains("experiment")) { ExperimentRunner.Run(); return; }

var dataload = OptData.Load(() => new Dataload());
using (var m = new OptModel("GlassFactory")
    .UseConfig(() => new CplexConfig { epGap = 0.03, timeLimit = 300, workThreads = 8,
                                       enableLog = true, exportSol = true, exportLP = true })
    .AddVariables(e => new VariableCreate(dataload, e).Build())
    .AddModel(e => new BuildModel(dataload, e).Build())
    .OnSolved(e => dataload.WriteToCSV(e)))
{
    bool ok = m.Execute();
}

// ── AFTER ──
var data = OptData.Load(() => new Dataload());

var projectConfig = new ProjectConfig
{
    ProjectName = "GlassFactory",
    EnableSolverLog = true,
    ExportSol = true,
    ExportLP = true,
};
var solverConfig = new CplexConfig { epGap = 0.03, timeLimit = 300, workThreads = 8 };

var model = new OptModel("GlassFactory")
    .AddVariables(e => CreateVariables(data, e))
    .AddObjective(e => BuildObjective(data, e))
    .AddConstraints(e => BuildConstraints(data, e));

using var project = new OptProject(model)
    .UseConfig(() => projectConfig)
    .UseConfig(() => solverConfig)
    .OnSolved(e => data.WriteToCSV(e));

bool ok = project.Execute();
```

**注意三處容易做錯的地方**：

1. `new OptModel("GlassFactory")` 舊字串是**專案名** → 搬去 `ProjectConfig.ProjectName`；`OptModel` 的名字是模型名（單一模型時可同名，但語意不同）
2. 舊的 `AddModel(e => new BuildModel(…).Build())` 要**拆成** `AddObjective` + `AddConstraints`。原 `BuildModel.Build()` 內第一行是 `new ObjectiveFunction(…).Build()`，那行歸 `AddObjective`，其餘歸 `AddConstraints`
3. `enableLog` 舊預設是 **`true`**（原始碼），不是 `CPLEX_API_REFERENCE.md` §4 寫的 `false`。原本沒寫 `enableLog` 的專案，遷移後 MUST 得到 `EnableSolverLog = true`

### Pattern B — `ExperimentRunner.cs` → `OptExperiment`

```csharp
// ── BEFORE：整個檔案 ──
public static class ExperimentRunner {
    public static void Run() {
        var exp = new Experiment("glass-tuning", "…");
        var variants = new (string label, Action<CplexConfig> tune)[] {
            ("baseline", _ => { }),
            ("emphasis=optimal", c => c.Emphasis = 2),
        };
        foreach (var (label, tune) in variants) {
            var config = new CplexConfig { epGap = 0.03, timeLimit = 60, enableLog = false };
            tune(config);
            var dataload = OptData.Load(() => new Dataload());
            using var engine = new OptEngine(config);
            engine.Build();
            new VariableCreate(dataload, engine).Build();
            new BuildModel(dataload, engine).Build();
            exp.AddTrial(Trial.Capture(engine, label, () => engine.Solve()));
        }
        exp.Save();
    }
}

// ── AFTER：檔案刪除，內容收進 Program.cs ──
var baseline = new CplexConfig { epGap = 0.03, timeLimit = 60 };
var emphasis = baseline.Clone(); emphasis.Emphasis = 2;

var result = new OptExperiment("glass-tuning", "…")
    .AddModel(model)
    .AddConfig("baseline", baseline)
    .AddConfig("emphasis=optimal", emphasis)
    .Run();
```

- `enableLog = false` **不用搬** —— `OptExperiment` 預設就是 log OFF（Spec 2 AC9）
- 每個 variant 各自 `OptData.Load` 的舊寫法**改為共用一份 `data`**（Spec 2 D9：`DataContext` 凍結後共用是安全的）
- `Action<CplexConfig> tune` 突變寫法 → `Clone()` + 具體物件

## Packet 切分與驗收

| Packet | 內容 | 平行度 | 驗收指令 |
| --- | --- | --- | --- |
| **2a** | Tier 1（15 份）+ 前置解耦同步 | 2–3 agents | `rg "VariableCreate\|BuildModel\.cs\|ExperimentRunner\|new OptModel\(.*\)\s*$\|enableLog\|exportLP" --glob "*.md" <各自檔案>` → 0 hit（明確標為「舊寫法對照」者除外） |
| **2b** | Tier 2（2 份） | 1 agent | 同上 + `enableLog` 預設值已修正為 `true` |
| **2c** | Tier 3（9 份） | 2 agents | 同上 |
| **2d** | Tier 5（8 份，加 frontmatter） | 1 agent | 每份都有 `superseded_by` |
| **3** | rebuild + 覆蓋 `dlls/` + 解 pin | 1 agent | 見下方特別說明 |
| **4** | 17 個 `.cs` + Tier 4（8 份） | **8 agents**（一專案一個） | `dotnet build` 0 error；`dotnet run` Status/目標值符合 `baseline/BASELINE.md` |

### Packet 3 的驗收是反直覺的

它做完之後 **`AI-Modeling` 是紅的（編不過），而且這是正確狀態**：

```
✅ dlls/ 六個 DLL 時間戳 = 本次 rebuild
✅ VERSION.txt 已解 pin
✅ AI-Modeling/Template 執行 dotnet build → 預期出現 CS0117（正常，Packet 4 會修）
❌ NEVER 看到 CS0117 就跑去改專案 code —— 那是 Packet 4 的工作，會與 Packet 4 的 agent 撞車
```

Packet 3 與 4 之間 **MUST NOT** 插入其他工作；`AI-Modeling` 在這段期間不可用。

## 執行順序（依賴鏈）

```
Spec 1 → Spec 2 → 2a → 3 → 4
                   ├─ 2b、2c、2d 可與 3、4 平行（人讀文件，不影響 code）
                   └─ 2a MUST 先於 4
```

`2a → 4` 是本規格最關鍵的依賴：Tier 1 是 AI 引導，沒改就派 agent 做 Packet 4，它會讀著舊的 `claudemdTemplate` 生出舊架構的 code。

## Acceptance Criteria

- [ ] AC1：Tier 1–3 共 26 份文件內，`rg` 查無舊 API 殘留（`VariableCreate` / `BuildModel.cs` / `ExperimentRunner` / `enableLog` / `exportLP` / `exportMPS` / `exportSol` / runner 語意的 `new OptModel(`）
- [ ] AC2：`CPLEX_API_REFERENCE.md` §4 的 `enableLog` 預設值已修正為 `true`
- [ ] AC3：Tier 5 共 8 份加上 `superseded_by`，內容未逐字改動
- [ ] AC4：`AI-Modeling/dlls/` 六個 DLL 已更新、`VERSION.txt` 解 pin
- [ ] AC5：8 個練習專案 `dotnet build` 0 error
- [ ] AC6：8 個練習專案 `dotnet run` 的 Status 與目標值與 `baseline/BASELINE.md` 一致（浮點相對差 ≤ `1e-6`）
- [ ] AC7：各專案限制式條數與 `baseline/<專案名>.txt` 內的 `[Constraint_*] N` 一致
- [ ] AC8：`HospitalRostering_Generator` 與 `_Manual` 的目標值仍相同（交叉檢查，兩者共用同一份數學模型）
- [ ] AC9：8 個 `ExperimentRunner.cs` 已刪除，`rg "ExperimentRunner"` 在 `AI-Modeling/Projects` 查無殘留
- [ ] AC10：`AI-Modeling/Template` build + `dotnet run` 目標值 158、限制式 93/155/5/125/305/5/5；`dotnet run -- experiment` 全 Optimal
- [ ] AC11：`/harness-eval milp-model` 不退步

## 給執行 agent 的硬規則

- **NEVER 改任何模型的數學內容**。本規格只做 config / runner 遷移。目標值變了 = bug，不是預期
- **NEVER 為了讓目標值對上而調整容差**。不符就停下回報
- 檔案內容與 before pattern 不符 → **跳過該檔並回報**，NEVER 自行推測
- `dotnet build` 修 5 次仍失敗 → 停下回報
- 只准動派工單列出的檔案

## References

- 依賴：[`2026-08-01-optimfoundation-dual-config.md`](2026-08-01-optimfoundation-dual-config.md)、[`2026-08-01-optimfoundation-runner-symmetry.md`](2026-08-01-optimfoundation-runner-symmetry.md)
- 基準值：`$OPT/AI-Modeling/baseline/BASELINE.md`（Packet 4 的唯一驗收依據）
- `$FW` = `C:/Users/zxcbi/Desktop/Projects/LLMDevFramework`
- `$OPT` = `C:/Users/zxcbi/Desktop/Projects/OptimizationFramework`
