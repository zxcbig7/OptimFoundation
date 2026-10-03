using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Template;

public sealed class Constraint_GreaterEqual : ConstraintBase
{
    private readonly List<Set_DateKey> dates;
    private readonly List<Set_SparsePair> pairs;

    public Constraint_GreaterEqual(List<Set_DateKey> dates, List<Set_SparsePair> pairs)
    {
        this.dates = dates;
        this.pairs = pairs;
    }

    public void Build(OptEngine engine)
    {
        foreach (var date in dates)
        {
            engine.AddLHS(1.0, new VariableC_ZeroDim());
            foreach (var pair in pairs.Where(pair => pair.Date == date.Date))
                engine.AddRHS(1.0, new VariableI_Integer { Key = pair.Key, Date = pair.Date });
            engine.CreateGreaterEqual(this, date.Date);
        }
    }
}
