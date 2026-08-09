using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>[max→輔助變數] ∀ lot ∈ Lot：Makespan ≥ Complete_{lot,LastOperation}</summary>
    public sealed class Constraint_MakespanDef : ConstraintBase
    {
        private readonly List<Set_Lot> _lots;
        private readonly List<Set_Operation> _operations; // 已依加工順序排序，最後一個＝LastOperation

        public Constraint_MakespanDef(List<Set_Lot> lots, List<Set_Operation> operations)
        {
            _lots = lots;
            _operations = operations;
        }

        public void Build(OptEngine engine)
        {
            var lastOp = _operations[_operations.Count - 1];

            foreach (var lot in _lots)
            {
                engine.AddLHS(1.0, new VariableC_Makespan());
                engine.AddRHS(1.0, new VariableC_Complete { Lot = lot, Operation = lastOp });
                engine.CreateGreatEqual(this, lot);
            }
        }
    }
}
