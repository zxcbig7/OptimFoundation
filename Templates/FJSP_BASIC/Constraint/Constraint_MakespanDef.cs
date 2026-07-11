using FJSP_BASIC.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC.Constraint
{
    /// <summary>[max→輔助變數] ∀ lot：Makespan ≥ Complete_{lot,LastOperation}</summary>
    public class Constraint_MakespanDef : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<string> _lots;
        private readonly List<string> _operations; // 已依加工順序排序，最後一個 = LastOperation
        private readonly List<string> _scope;

        public Constraint_MakespanDef(List<string> lots, List<string> operations, List<string> scope, OptEngine engine)
        {
            _lots = lots;
            _operations = operations;
            _scope = scope;
            _engine = engine;
        }

        public void Build()
        {
            var lastOp = _operations[_operations.Count - 1];
            var scope = _scope[0];

            foreach (var lot in _lots)
            {
                _engine.AddLHS(1.0, new VariableX_Makespan { Scope = scope });
                _engine.AddRHS(1.0, new VariableX_Complete { Lot = lot, Operation = lastOp });
                _engine.CreateGreatEqual($"{ConstraintName}@{lot}");
                ConstraintCount++;
            }

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
