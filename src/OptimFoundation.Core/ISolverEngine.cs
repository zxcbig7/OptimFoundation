using System;
using System.Collections.Generic;

namespace OptimFoundation.Core
{

    /// <summary>跨引擎共通的基本求解設定（時間上限 / gap / 執行緒 / log）；各 solver 的 config 實作此介面。</summary>
    public interface ISolverConfig
    {
        double? TimeLimit { get; set; }
        double? MipGap { get; set; }
        int? Threads { get; set; }
        bool LogToConsole { get; set; }
        string LogFilePath { get; set; }

        /// <summary>Solve 前 scale guard 門檻：TotalVarCount 超過此值 → PreSolveGuard 只 Warn 不阻擋。預設值 default interface member，不破壞既有實作者。</summary>
        int ScaleWarnThreshold => 10_000_000;
    }


    /// <summary>求解引擎的統一介面：建模型 → 求解 → 取解/telemetry。EngineBase 提供泛型實作。</summary>
    public interface ISolverEngine : IDisposable
    {
        ISolverConfig Config { get; }
        SolveStatus Status { get; }

        /// <summary>最近一次 Solve() 的統一 telemetry；尚未求解為 null。</summary>
        SolveMetrics LastMetrics { get; }

        void Build();
        bool Solve();
        double GetObjectiveValue();
        double GetVariableValue(string name);

        // 取得解結果字典；varTypeName = null 回傳所有變數，否則過濾前綴 "TypeName@..."
        IReadOnlyDictionary<string, double> GetSolution(string varTypeName = null);
    }

    /// <summary>特殊限制式的選用介面（SOS1/2、indicator、lazy）；只有支援的 solver 實作。</summary>
    public interface ISpecialConstraints<TVar, TExpr>
    {
        void AddSOS1(IEnumerable<TVar> vars);
        void AddSOS2(IEnumerable<TVar> vars);
        void AddIndicator(TVar binary, TExpr expr, ConstraintSense sense, double rhs);
        void AddLazyConstraint(TExpr expr, ConstraintSense sense, double rhs);
    }

    /// <summary>求解結果狀態。</summary>
    public enum SolveStatus
    {
        NotSolved, // 尚未求解
        Optimal, // 找到並證明最佳解
        Feasible, // 有可行解但未證明最佳（如 timeout）
        Infeasible, // 無可行解
        Unbounded, // 無界
        TimeLimit, // 時間到且無可用結果
        Error // 求解發生錯誤
    }



}
