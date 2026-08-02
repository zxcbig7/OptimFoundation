using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>min Makespan；對應 Model.md OBJ 的硬性最小化部分（soft penalty 只由 Phase 3 demo variant 疊加，見 Constraint_MakespanTargetSoft）。</summary>
    public sealed class ObjectiveFunction
    {
        public void Build(OptEngine engine)
        {
            engine.AddLHS(1.0, new VariableX_Makespan());
            engine.CreateMinimize();
        }
    }
}
