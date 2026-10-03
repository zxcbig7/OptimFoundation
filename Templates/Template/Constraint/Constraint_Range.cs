using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Template;

public sealed class Constraint_Range : ConstraintBase
{
    private readonly List<Set_StringKey> keys;

    public Constraint_Range(List<Set_StringKey> keys)
    {
        this.keys = keys;
    }

    public void Build(OptEngine engine)
    {
        foreach (var key in keys)
            engine.AddLHS(1.0, new VariableB_Binary { Key = key.Key });

        engine.CreateRange(1.0, keys.Count - 1, this);
    }
}
