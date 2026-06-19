using System.Collections.Generic;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 收斂軌跡來源的可擴展 hook。EngineBase 提供「不支援」的預設實作；
    /// 支援的 engine（本期僅 CPLEX）override。未支援者呼叫端不報錯。
    /// </summary>
    public interface ITrajectorySource
    {
        bool SupportsTrajectory { get; }
        void EnableTrajectory();
        IReadOnlyList<ConvergencePoint> Trajectory { get; }
    }
}
