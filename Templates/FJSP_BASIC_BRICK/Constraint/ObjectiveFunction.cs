using FJSP_BASIC_BRICK.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK.Constraint
{
    /// <summary>OBJ：min Makespan</summary>
    public class ObjectiveFunction
    {
        private readonly OptEngine _engine;

        public ObjectiveFunction(OptEngine engine)
        {
            _engine = engine;
        }

        public void Build()
        {
            _engine.AddLHS(1.0, new VariableX_Makespan());
            _engine.CreateMinimize();
            Logging.Info("[Objective] min Makespan");
        }
    }
}
