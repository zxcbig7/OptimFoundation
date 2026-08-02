using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>[LB] ∀ lot ∈ Lot, 相鄰道次 (op → nextOp)：Start_{lot,nextOp} ≥ Complete_{lot,op}</summary>
    public sealed class Constraint_RoutePrecedence : ConstraintBase
    {
        private readonly Set_Lot _lots;
        private readonly Set_Operation _operations; // 行序＝加工順序

        public Constraint_RoutePrecedence(Set_Lot lots, Set_Operation operations)
        {
            _lots = lots;
            _operations = operations;
        }

        public void Build(OptEngine engine)
        {
            foreach (var lot in _lots)
            {
                for (int i = 0; i + 1 < _operations.Count; i++)
                {
                    var op = _operations[i];
                    var nextOp = _operations[i + 1];

                    engine.AddLHS(1.0, new VariableX_Start { Lot = lot, Operation = nextOp });
                    engine.AddRHS(1.0, new VariableX_Complete { Lot = lot, Operation = op });
                    engine.CreateGreatEqual($"{ConstraintName}@{lot}@{op}@{nextOp}");
                }
            }
        }
    }
}
