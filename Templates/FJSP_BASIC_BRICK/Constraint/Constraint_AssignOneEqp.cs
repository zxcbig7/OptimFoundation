using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>[Exclusive XOR] ∀ lot ∈ Lot, op ∈ Operation：Σ_eqp Assign_{lot,op,eqp} = ExactlyOne</summary>
    public sealed class Constraint_AssignOneEqp : ConstraintBase
    {
        private readonly Set_Lot _lots;
        private readonly Set_Operation _operations;
        private readonly Set_Eqp _eqps;
        private readonly double _exactlyOne;

        public Constraint_AssignOneEqp(Set_Lot lots, Set_Operation operations, Set_Eqp eqps, double exactlyOne)
        {
            _lots = lots;
            _operations = operations;
            _eqps = eqps;
            _exactlyOne = exactlyOne;
        }

        public void Build(OptEngine engine)
        {
            foreach (var lot in _lots)
            {
                foreach (var op in _operations)
                {
                    foreach (var eqp in _eqps)
                        engine.AddLHS(1.0, new VariableB_Assign { Lot = lot, Operation = op, Eqp = eqp });

                    engine.AddRHS(_exactlyOne);
                    engine.CreateEqual($"{ConstraintName}@{lot}@{op}");
                }
            }
        }
    }
}
