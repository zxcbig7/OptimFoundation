using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>[Balance] ∀ lot ∈ Lot, op ∈ Operation：Complete = Start + Σ_eqp ProcessTime·Assign</summary>
    public sealed class Constraint_CompleteDef : ConstraintBase
    {
        private readonly List<Set_Lot> _lots;
        private readonly List<Set_Operation> _operations;
        private readonly List<Set_Eqp> _eqps;
        private readonly List<Parameter_ProcessTime> _processTime;

        public Constraint_CompleteDef(
            List<Set_Lot> lots,
            List<Set_Operation> operations,
            List<Set_Eqp> eqps,
            List<Parameter_ProcessTime> processTime)
        {
            _lots = lots;
            _operations = operations;
            _eqps = eqps;
            _processTime = processTime;
        }

        public void Build(OptEngine engine)
        {
            foreach (var lot in _lots)
            {
                foreach (var op in _operations)
                {
                    engine.AddLHS(1.0, new VariableC_Complete { Lot = lot, Operation = op });
                    engine.AddRHS(1.0, new VariableC_Start { Lot = lot, Operation = op });

                    foreach (var eqp in _eqps)
                    {
                        var procTime = _processTime.FindParameterOrLog(
                            p => p.Lot == lot && p.Operation == op && p.Eqp == eqp,
                            lot, op, eqp)?.QTY ?? 0.0;
                        engine.AddRHS(procTime, new VariableB_Assign { Lot = lot, Operation = op, Eqp = eqp });
                    }

                    engine.CreateEqual(this, lot, op);
                }
            }
        }
    }
}
