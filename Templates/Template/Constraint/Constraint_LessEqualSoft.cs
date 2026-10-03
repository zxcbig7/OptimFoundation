using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Template;

public sealed class Constraint_LessEqualSoft : ConstraintBase
{
    private readonly List<Set_StringKey> keys;
    private readonly double penalty;

    public Constraint_LessEqualSoft(List<Set_StringKey> keys, double penalty)
    {
        this.keys = keys;
        this.penalty = penalty;
    }

    public void Build(OptEngine engine)
    {
        foreach (var key in keys)
            engine.AddLHS(1.0, new VariableB_Binary { Key = key.Key });

        engine.CreateLessEqualSoft(1.0, penalty, this);
    }
}
