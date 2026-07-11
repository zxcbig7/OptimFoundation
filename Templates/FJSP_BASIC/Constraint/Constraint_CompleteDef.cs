using FJSP_BASIC.ParameterClass;
using FJSP_BASIC.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC.Constraint
{
    /// <summary>[Balance] ∀ lot, op：Complete = Start + Σ_eqp ProcessTime·Assign</summary>
    public class Constraint_CompleteDef : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly List<string> _lots;
        private readonly List<string> _operations;
        private readonly List<string> _eqps;
        private readonly List<Parameter_ProcessTime> _processTime;

        public Constraint_CompleteDef(
            List<string> lots, List<string> operations, List<string> eqps,
            List<Parameter_ProcessTime> processTime, OptEngine engine)
        {
            _lots = lots;
            _operations = operations;
            _eqps = eqps;
            _processTime = processTime;
            _engine = engine;
        }

        public void Build()
        {
            foreach (var lot in _lots)
            {
                foreach (var op in _operations)
                {
                    _engine.AddLHS(1.0, new VariableX_Complete { Lot = lot, Operation = op });

                    _engine.AddRHS(1.0, new VariableX_Start { Lot = lot, Operation = op });

                    foreach (var eqp in _eqps)
                    {
                        var procTime = _processTime.FirstOrDefault(p => p.Lot == lot && p.Operation == op && p.Eqp == eqp)?.QTY ?? 0.0;
                        _engine.AddRHS(procTime, new VariableB_Assign { Lot = lot, Operation = op, Eqp = eqp });
                    }
                    _engine.CreateEqual($"{ConstraintName}@{lot}@{op}");
                    ConstraintCount++;
                }
            }

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
