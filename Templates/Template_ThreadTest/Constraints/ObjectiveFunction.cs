using OptimFoundation.Cplex;
using OptimFoundation.Core;
using ThreadTest.Data;
using ThreadTest.VariableClass;

namespace ThreadTest.Constraints
{
    public class ObjectiveFunction
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public ObjectiveFunction(Dataload dataload, OptEngine engine)
        {
            this.dataload = dataload;
            this.optEngine = engine;
        }

        public void Build()
        {
            try
            {
                foreach (var s in dataload.Sources)
                    foreach (var d in dataload.Dests)
                    {
                        double cost = dataload.parameter_Cost
                            .FirstOrDefault(p => p.Source == s && p.Dest == d)?.QTY ?? 0;
                        optEngine.AddLHS(cost, new VariableX_Supply { Source = s, Dest = d });
                    }

                optEngine.CreateMinimize();
                Logging.Info("目標式建構完成");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
