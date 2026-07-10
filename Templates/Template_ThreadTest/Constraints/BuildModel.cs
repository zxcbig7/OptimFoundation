using OptimFoundation.Cplex;
using OptimFoundation.Core;
using ThreadTest.Data;

namespace ThreadTest.Constraints
{
    public class BuildModel
    {
        private OptEngine engine;
        private Dataload dataload;

        public BuildModel(Dataload dataload, OptEngine engine)
        {
            this.engine = engine;
            this.dataload = dataload;
        }

        public void Build()
        {
            try
            {
                Logging.Info("【建構目標式】");
                new ObjectiveFunction(dataload, engine).Build();

                Logging.Info("【建構限制式】");
                new Constraint_Supply(dataload, engine).Build();
                new Constraint_Demand(dataload, engine).Build();
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
