using FJSP_BASIC_BRICK.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK.Constraint
{
    /// <summary>[LB] ∀ lot, 相鄰道次 (op → nextOp)：Start_{lot,nextOp} ≥ Complete_{lot,op}</summary>
    public class Constraint_RoutePrecedence : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly IReadOnlyList<string> _lots;
        private readonly IReadOnlyList<string> _operations; // 已依加工順序排序（字典序）

        public Constraint_RoutePrecedence(IReadOnlyList<string> lots, IReadOnlyList<string> operations, OptEngine engine)
        {
            _lots = lots;
            _operations = operations;
            _engine = engine;
        }

        public void Build()
        {
            foreach (var lot in _lots)
            {
                for (int i = 0; i + 1 < _operations.Count; i++)
                {
                    var op = _operations[i];
                    var nextOp = _operations[i + 1];

                    _engine.AddLHS(1.0, new VariableX_Start { Lot = lot, Operation = nextOp });
                    _engine.AddRHS(1.0, new VariableX_Complete { Lot = lot, Operation = op });
                    _engine.CreateGreatEqual($"{ConstraintName}@{lot}@{op}@{nextOp}");
                }
            }

        }
    }
}
