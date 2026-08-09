using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace TSP_MultiDimSet
{
    /// <summary>
    /// [C6] VisitOrderRange ∀ node ∈ CUSTOMER：
    /// 1 ≤ VisitOrder[node] ≤ |NODE| − 1
    /// 界限寫成獨立限制式，不藏進變數 builder 的 bounds 參數。
    /// </summary>
    public sealed class Constraint_VisitOrderRange : ConstraintBase
    {
        private readonly List<Set_Node> _nodes;
        private readonly List<Set_Customer> _customers;

        public Constraint_VisitOrderRange(
            List<Set_Node> nodes,
            List<Set_Customer> customers)
        {
            _nodes = nodes;
            _customers = customers;
        }

        public void Build(OptEngine engine)
        {
            int nodeCount = _nodes.Count;

            foreach (string customer in _customers)
            {
                engine.AddLHS(1.0, new VariableC_VisitOrder { Node = customer });
                engine.CreateRange(1.0, nodeCount - 1, this, customer);
            }
        }
    }
}
