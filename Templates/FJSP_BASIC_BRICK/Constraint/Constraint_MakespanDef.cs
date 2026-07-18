using FJSP_BASIC_BRICK.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK.Constraint
{
    /// <summary>[max→輔助變數] ∀ lot：Makespan ≥ Complete_{lot,LastOperation}</summary>
    public class Constraint_MakespanDef : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly IReadOnlyList<string> _lots;
        private readonly IReadOnlyList<string> _operations; // 已依加工順序排序，最後一個 = LastOperation

        public Constraint_MakespanDef(IReadOnlyList<string> lots, IReadOnlyList<string> operations, OptEngine engine)
        {
            _lots = lots;
            _operations = operations;
            _engine = engine;
        }

        public void Build()
        {
            var lastOp = _operations[_operations.Count - 1];

            foreach (var lot in _lots)
            {
                _engine.AddLHS(1.0, new VariableX_Makespan());
                _engine.AddRHS(1.0, new VariableX_Complete { Lot = lot, Operation = lastOp });
                _engine.CreateGreatEqual($"{ConstraintName}@{lot}");
                ConstraintCount++;
            }

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
