using FJSP_BASIC.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC.Constraint
{
    /// <summary>OBJ：min Makespan</summary>
    public class ObjectiveFunction
    {
        private readonly OptEngine _engine;
        private readonly List<string> _scope;

        public ObjectiveFunction(List<string> scope, OptEngine engine)
        {
            _scope = scope;
            _engine = engine;
        }

        public void Build()
        {
            _engine.AddLHS(1.0, new VariableX_Makespan { Scope = _scope[0] });
            _engine.CreateMinimize();
            Logging.Info("[Objective] min Makespan");
        }
    }
}
