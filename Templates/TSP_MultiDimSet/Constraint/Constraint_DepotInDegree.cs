using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace TSP_MultiDimSet
{
    /// <summary>
    /// [C4] DepotInDegree ∀ depot ∈ DEPOT：
    /// Σ_{(i,depot) ∈ ARC} UseArc[i][depot] = 1
    /// </summary>
    public sealed class Constraint_DepotInDegree : ConstraintBase
    {
        private readonly List<Set_Depot> _depots;
        private readonly List<Set_Arc> _arcs;

        public Constraint_DepotInDegree(
            List<Set_Depot> depots,
            List<Set_Arc> arcs)
        {
            _depots = depots;
            _arcs = arcs;
        }

        public void Build(OptEngine engine)
        {
            foreach (string depot in _depots)
            {
                foreach (var arc in _arcs.Where(arc => arc.To == depot))
                    engine.AddLHS(1.0, new VariableB_UseArc { From = arc.From, To = arc.To });

                engine.AddRHS(1.0);
                engine.CreateEqual(this, depot);
            }
        }
    }
}
