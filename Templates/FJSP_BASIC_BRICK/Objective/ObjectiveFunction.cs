using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>最小化整體完工時間 Makespan，對應 Model.md 的 OBJ；只有 Phase 3 示範模型會另加超時罰分，見 Constraint_MakespanTargetSoft。</summary>
    public sealed class ObjectiveFunction
    {
        public void Build(OptEngine engine)
        {
            engine.AddLHS(1.0, new VariableC_Makespan());
            engine.CreateMinimize();
        }
    }
}
