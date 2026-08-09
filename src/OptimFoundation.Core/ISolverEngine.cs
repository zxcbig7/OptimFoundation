using System;
using System.Collections.Generic;

namespace OptimFoundation.Core
{

    /// <summary>跨引擎共通的基本求解設定（時間上限 / gap / 執行緒）；各 solver 的 config 實作此介面。</summary>
    public interface ISolverConfig
    {
        /// <summary>求解時間上限（秒）；null = 不限制。</summary>
        double? TimeLimit { get; set; }

        /// <summary>相對 MIP gap 收斂門檻；null = 用 solver 預設。</summary>
        double? MipGap { get; set; }

        /// <summary>可用執行緒數；null = 由 solver 自行決定。</summary>
        int? Threads { get; set; }

        /// <summary>Solve 前 scale guard 門檻：RegisteredVariableCount 超過此值 → PreSolveGuard 只 Warn 不阻擋。預設值 default interface member，不破壞既有實作者。</summary>
        int ScaleWarnThreshold => 10_000_000;
    }

    /// <summary>
    /// 跨引擎共通的 tuning 控制項目抽象。與 <see cref="ISolverConfig"/> 並存（加法，不破壞既有）。
    /// 各 concrete config 將這些抽象控制項目對映到自家專屬欄位；null = 使用 solver 預設。
    /// </summary>
    public interface ITunableConfig
    {
        /// <summary>隨機種子（CPLEX randomSeed / Gurobi Seed）。要重現結果就固定它。</summary>
        int? Seed { get; set; }

        /// <summary>求解重點（CPLEX mipEmphasis / Gurobi MipFocus）。只記原始整數值，各 solver 語意不同、不做正規化。</summary>
        int? Emphasis { get; set; }

        /// <summary>可行性容差（CPLEX epRHS / Gurobi FeasibilityTol / Solver Epsilon）。</summary>
        double? FeasibilityTol { get; set; }

        /// <summary>最佳性容差（CPLEX epOpt / Gurobi OptimalityTol）。</summary>
        double? OptimalityTol { get; set; }

        /// <summary>根節點 LP 演算法（CPLEX algorithm / Gurobi Method）。取值語意依 solver。</summary>
        int? RootAlgorithm { get; set; }

        /// <summary>前處理開關（CPLEX PreInd / Gurobi Presolve）；0 = 關閉。</summary>
        int? Presolve { get; set; }

        /// <summary>啟發式投入程度（Gurobi Heuristics 0~1 / CPLEX HeuristicEffort）。</summary>
        double? HeuristicEffort { get; set; }

        /// <summary>記憶體上限 MB（CPLEX workMemory / Gurobi SoftMemLimit）。</summary>
        double? MemoryLimitMb { get; set; }
    }


    /// <summary>求解引擎的統一介面：建模型 → 求解 → 取解/telemetry。EngineBase 提供泛型實作。</summary>
    public interface ISolverEngine : IDisposable
    {
        /// <summary>本引擎使用的求解組態。</summary>
        ISolverConfig Config { get; }

        /// <summary>求解狀態；未求解為 NotSolved。</summary>
        SolveStatus Status { get; }

        /// <summary>最近一次 Solve() 的統一 telemetry；尚未求解為 null。</summary>
        SolveMetrics LastMetrics { get; }

        /// <summary>建立 solver 模型並套用組態；建變數 / 限制式前 MUST 先呼叫。</summary>
        void Build();

        /// <summary>求解。回傳 true 代表取得 Optimal 或 Feasible 解。</summary>
        bool Solve();

        /// <summary>目標式解值；MUST 在求解成功後呼叫。</summary>
        double GetObjectiveValue();

        /// <summary>依變數全名取解值（TypeName@s1@s2@…）；MUST 在求解成功後呼叫。</summary>
        double GetVariableValue(string name);

        /// <summary>取解結果字典；varTypeName = null 回傳所有變數，否則只回該型別（前綴 "TypeName@"）。</summary>
        IReadOnlyDictionary<string, double> GetSolution(string varTypeName = null);
    }

    /// <summary>特殊限制式的選用介面（SOS1/2、indicator、lazy）；只有支援的 solver 實作。</summary>
    public interface ISpecialConstraints<TVar, TExpr>
    {
        /// <summary>SOS1：這組變數中最多只有一個可以非零。</summary>
        void AddSOS1(IEnumerable<TVar> vars);

        /// <summary>SOS2：這組變數中最多兩個非零，且必須相鄰（分段線性常用）。</summary>
        void AddSOS2(IEnumerable<TVar> vars);

        /// <summary>indicator：binary = 1 時才強制 expr (sense) rhs 成立；可避免自己湊 Big-M。</summary>
        void AddIndicator(TVar binary, TExpr expr, ConstraintSense sense, double rhs);

        /// <summary>lazy constraint：先不放進模型，solver 找到候選解時才檢查並補上。</summary>
        void AddLazyConstraint(TExpr expr, ConstraintSense sense, double rhs);
    }

    /// <summary>求解結果狀態。</summary>
    public enum SolveStatus
    {
        /// <summary>尚未求解。</summary>
        NotSolved,

        /// <summary>找到並證明最佳解。</summary>
        Optimal,

        /// <summary>有可行解但未證明最佳（例如逾時停下）。Solve() 仍回傳 true。</summary>
        Feasible,

        /// <summary>無可行解；CPLEX 這一側會自動跑 conflict(IIS) 分析。</summary>
        Infeasible,

        /// <summary>目標式無界，多半是漏了某條限制式。</summary>
        Unbounded,

        /// <summary>時間到且沒有任何可用解。</summary>
        TimeLimit,

        /// <summary>求解過程發生錯誤。</summary>
        Error
    }



}
