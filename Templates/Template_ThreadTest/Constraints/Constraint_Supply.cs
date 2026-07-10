using OptimFoundation.Cplex;
using OptimFoundation.Core;
using ThreadTest.Data;
using ThreadTest.VariableClass;

namespace ThreadTest.Constraints
{
    // Σ_d X[s,d] ≤ supply[s]  for each source s
    public class Constraint_Supply : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_Supply(Dataload dataload, OptEngine engine)
        {
            this.dataload = dataload;
            this.optEngine = engine;
        }

        public void Build()
        {
            try
            {
                foreach (var s in dataload.Sources)
                {
                    foreach (var d in dataload.Dests)
                        optEngine.AddLHS(1, new VariableX_Supply { Source = s, Dest = d });

                    optEngine.AddRHS(dataload.Supply[s]);
                    optEngine.CreateLessEqual($"{ConstraintName}@{s}");
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
