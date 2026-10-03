using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Template;

public sealed class Constraint_LessEqual : ConstraintBase
{
    private readonly List<Set_SparsePair> pairs;
    private readonly List<Parameter_TwoDim> requirements;

    public Constraint_LessEqual(
        List<Set_SparsePair> pairs,
        List<Parameter_TwoDim> requirements)
    {
        this.pairs = pairs;
        this.requirements = requirements;
    }

    public void Build(OptEngine engine)
    {
        foreach (var pair in pairs)
        {
            double requirement = requirements
                .FindParameterOrLog(row => row.Key == pair.Key && row.Date == pair.Date, pair.Key, pair.Date)?.QTY ?? 0.0;

            engine.AddLHS(1.0, new VariableI_Integer { Key = pair.Key, Date = pair.Date });
            engine.AddRHS(requirement, new VariableB_Binary { Key = pair.Key });
            engine.CreateLessEqual(this, pair.Key, pair.Date);
        }
    }
}
