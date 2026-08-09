using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace TSP_MultiDimSet
{
    /// <summary>
    /// [C1] CustomerInDegree ∀ node ∈ CUSTOMER：
    /// Σ_{(i,node) ∈ ARC} UseArc[i][node] = 1
    /// </summary>
    public sealed class Constraint_CustomerInDegree : ConstraintBase
    {
        private readonly List<Set_Customer> _customers;
        private readonly List<Set_Arc> _arcs;

        public Constraint_CustomerInDegree(
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
                foreach (var arc in _arcs.Where(arc => arc.To == customer))
                    engine.AddLHS(1.0, new VariableB_UseArc { From = arc.From, To = arc.To });

                engine.AddRHS(1.0);
                engine.CreateEqual(this, customer);
            }
        }
    }
}
