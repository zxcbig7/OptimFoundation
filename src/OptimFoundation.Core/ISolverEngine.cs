using System;
using System.Collections.Generic;

namespace OptimFoundation.Core
{
    public interface ISolverEngine : IDisposable
    {
        ISolverConfig Config { get; }
        SolveStatus Status { get; }

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
