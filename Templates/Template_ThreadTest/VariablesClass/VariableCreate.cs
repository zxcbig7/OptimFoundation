using OptimFoundation.Cplex;
using OptimFoundation.Core;
using ThreadTest.Data;
using ThreadTest.VariableClass;

namespace ThreadTest.VariablesClass
{
    public class VariableCreate
    {
        private OptEngine optEngine;
        private Dataload dataload;
        private int varCount { get { return optEngine.varCount; } }

        public VariableCreate(Dataload dataload, OptEngine engine)
        {
            this.dataload = dataload;
            this.optEngine = engine;
        }

        public void Build()
        {
            try
            {
                optEngine.BuildCVs<VariableX_Supply>(dataload.Sources, dataload.Dests);

                Logging.Info($"Variables created: {varCount}");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
