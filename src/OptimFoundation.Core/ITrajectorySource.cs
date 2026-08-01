using System.Collections.Generic;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 收斂軌跡來源的可擴展 hook。EngineBase 提供「不支援」的預設實作；
    /// 支援的 engine（本期僅 CPLEX）override。未支援者呼叫端不報錯。
    /// </summary>
    public interface ITrajectorySource
    {
        /// <summary>本 engine 是否能記錄收斂軌跡。</summary>
        bool SupportsTrajectory { get; }

        /// <summary>開啟軌跡記錄，MUST 在 Solve() 之前呼叫；不支援的 engine 為 no-op。</summary>
        void EnableTrajectory();

        /// <summary>最近一次求解的軌跡；未開啟或不支援時為空清單。</summary>
        IReadOnlyList<ConvergencePoint> Trajectory { get; }
    }
}
