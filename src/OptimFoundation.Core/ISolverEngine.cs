using System;
using System.Collections.Generic;

namespace OptimFoundation.Core
{
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

    public enum SolveStatus
    {
        NotSolved,
        Optimal,
        Feasible,
        Infeasible,
        Unbounded,
        TimeLimit,
        Error
    }
}
