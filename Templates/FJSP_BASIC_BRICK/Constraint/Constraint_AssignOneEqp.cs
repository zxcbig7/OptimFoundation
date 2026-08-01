using FJSP_BASIC_BRICK.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK.Constraint
{
    /// <summary>[XOR] ∀ lot, op：Σ_eqp Assign_{lot,op,eqp} = 1（每道作業恰好指派一台機台）</summary>
    public class Constraint_AssignOneEqp : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly IReadOnlyList<string> _lots;
        private readonly IReadOnlyList<string> _operations;
        private readonly IReadOnlyList<string> _eqps;

        public Constraint_AssignOneEqp(IReadOnlyList<string> lots, IReadOnlyList<string> operations, IReadOnlyList<string> eqps, OptEngine engine)
        {
            _lots = lots;
            _operations = operations;
            _eqps = eqps;
            _engine = engine;
        }

        public void Build()
        {
            foreach (var lot in _lots)
            {
                foreach (var op in _operations)
                {
                    foreach (var eqp in _eqps)
                        _engine.AddLHS(1.0, new VariableB_Assign { Lot = lot, Operation = op, Eqp = eqp });

                    _engine.AddRHS(1.0);
                    _engine.CreateEqual($"{ConstraintName}@{lot}@{op}");
                }
            }

        }
    }
}
