using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Template;

public sealed class ObjectiveFunction
{
    private readonly List<Set_StringKey> keys;
    private readonly List<Set_DateKey> dates;
    private readonly List<Parameter_OneDim> costs;
    private readonly double penalty;

    public ObjectiveFunction(
        List<Set_StringKey> keys,
        List<Set_DateKey> dates,
        List<Parameter_OneDim> costs,
        double penalty)
    {
        this.keys = keys;
        this.dates = dates;
        this.costs = costs;
        this.penalty = penalty;
    }

    public void Build(OptEngine engine)
    {
        foreach (var key in keys)
        {
            double cost = costs
                .FindParameterOrLog(row => row.Key == key.Key, key.Key)?.QTY ?? 0.0;
            engine.AddLHS(cost, new VariableB_Binary { Key = key.Key });

            foreach (var date in dates)
                engine.AddLHS(penalty, new VariableC_Continuous { Key = key.Key, Date = date.Date });
        }

        engine.AddLHS(1.0, new VariableC_ZeroDim());

        engine.CreateMinimize();
    }
}
