using OptimFoundation.Cplex;
using OptimFoundation.Core;
using ThreadTest.Data;
using ThreadTest.VariableClass;

namespace ThreadTest.Constraints
{
    // Σ_s X[s,d] ≥ demand[d]  for each destination d
    public class Constraint_Demand : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_Demand(Dataload dataload, OptEngine engine)
        {
            this.dataload = dataload;
            this.optEngine = engine;
        }

        public void Build()
        {
            try
            {
                foreach (var d in dataload.Dests)
                {
                    foreach (var s in dataload.Sources)
                        optEngine.AddLHS(1, new VariableX_Supply { Source = s, Dest = d });

                    optEngine.AddRHS(dataload.Demand[d]);
                    optEngine.CreateGreatEqual($"{ConstraintName}@{d}");
                    ConstraintCount++;
                }

                Logging.Info($"[{ConstraintName}] {ConstraintCount}");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
