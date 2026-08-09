using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace TSP_MultiDimSet
{
    /// <summary>
    /// [C2] CustomerOutDegree ∀ node ∈ CUSTOMER：
    /// Σ_{(node,j) ∈ ARC} UseArc[node][j] = 1
    /// </summary>
    public sealed class Constraint_CustomerOutDegree : ConstraintBase
    {
        private readonly List<Set_Customer> _customers;
        private readonly List<Set_Arc> _arcs;

        public Constraint_CustomerOutDegree(
            List<Set_Customer> customers,
            List<Set_Arc> arcs)
        {
            _customers = customers;
            _arcs = arcs;
        }

        public void Build(OptEngine engine)
        {
            foreach (string customer in _customers)
            {
                foreach (var arc in _arcs.Where(arc => arc.From == customer))
                    engine.AddLHS(1.0, new VariableB_UseArc { From = arc.From, To = arc.To });

                engine.AddRHS(1.0);
                engine.CreateEqual(this, customer);
            }
        }
    }
}
