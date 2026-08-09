using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace TSP_MultiDimSet
{
    /// <summary>
    /// [C5] SubtourMTZ ∀ (i,j) ∈ ARC 且 i ∈ CUSTOMER 且 j ∈ CUSTOMER 且 i ≠ j：
    /// VisitOrder[i] − VisitOrder[j] + |NODE| · UseArc[i][j] ≤ |NODE| − 1
    /// </summary>
    public sealed class Constraint_SubtourMTZ : ConstraintBase
    {
        private readonly List<Set_Node> _nodes;
        private readonly List<Set_Customer> _customers;
        private readonly List<Set_Arc> _arcs;

        public Constraint_SubtourMTZ(
            List<Set_Node> nodes,
            List<Set_Customer> customers,
            List<Set_Arc> arcs)
        {
            _nodes = nodes;
            _customers = customers;
            _arcs = arcs;
        }

        public void Build(OptEngine engine)
        {
            int nodeCount = _nodes.Count;
            var customerNodes = _customers.Select(customer => customer.Node).ToHashSet();

            foreach (var arc in _arcs)
            {
                if (!customerNodes.Contains(arc.From) || !customerNodes.Contains(arc.To))
                    continue;
                if (arc.From == arc.To)
                    continue;

                engine.AddLHS(1.0, new VariableC_VisitOrder { Node = arc.From });
                engine.AddLHS(-1.0, new VariableC_VisitOrder { Node = arc.To });
                engine.AddLHS(nodeCount, new VariableB_UseArc { From = arc.From, To = arc.To });

                engine.AddRHS(nodeCount - 1);
                engine.CreateLessEqual(this, arc.From, arc.To);
            }
        }
    }
}
