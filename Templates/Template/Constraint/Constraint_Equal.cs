using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Template;

public sealed class Constraint_Equal : ConstraintBase
{
    private readonly List<Set_StringKey> keys;
    private readonly List<Set_DateKey> dates;
    private readonly List<Set_SparsePair> pairs;
    private readonly List<Parameter_TwoDim> requirements;

    public Constraint_Equal(
        List<Set_StringKey> keys,
        List<Set_DateKey> dates,
        List<Set_SparsePair> pairs,
        List<Parameter_TwoDim> requirements)
    {
        this.keys = keys;
        this.dates = dates;
        this.pairs = pairs;
        this.requirements = requirements;
    }

    public void Build(OptEngine engine)
    {
        foreach (var key in keys)
        {
            foreach (var date in dates)
            {
                if (pairs.Any(pair => pair.Key == key.Key && pair.Date == date.Date))
                    engine.AddLHS(1.0, new VariableI_Integer { Key = key.Key, Date = date.Date });
                engine.AddLHS(1.0, new VariableC_Continuous { Key = key.Key, Date = date.Date });

                double requirement = requirements
                    .FindParameterOrLog(row => row.Key == key.Key && row.Date == date.Date, key.Key, date.Date)?.QTY ?? 0.0;
                engine.AddRHS(requirement);

                engine.CreateEqual(this, key.Key, date.Date);
            }
        }
    }
}
