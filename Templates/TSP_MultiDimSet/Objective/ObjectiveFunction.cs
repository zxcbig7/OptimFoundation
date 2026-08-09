using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace TSP_MultiDimSet
{
    /// <summary>min Σ_{(i,j) ∈ ARC} ArcCost[i][j] · UseArc[i][j]；對應 Model.md 的 OBJ。</summary>
    public sealed class ObjectiveFunction
    {
        private readonly List<Set_Arc> _arcs;
        private readonly List<Parameter_ArcCost> _arcCosts;

        public ObjectiveFunction(
            List<Set_Arc> arcs,
            List<Parameter_ArcCost> arcCosts)
        {
            _arcs = arcs;
            _arcCosts = arcCosts;
        }

        public void Build(OptEngine engine)
        {
            foreach (var arc in _arcs)
            {
                double cost = _arcCosts.FindParameterOrLog(
                    parameter => parameter.From == arc.From && parameter.To == arc.To,
                    arc.From, arc.To)?.QTY ?? 0.0;

                engine.AddLHS(cost, new VariableB_UseArc { From = arc.From, To = arc.To });
            }

            engine.CreateMinimize();
        }
    }
}
