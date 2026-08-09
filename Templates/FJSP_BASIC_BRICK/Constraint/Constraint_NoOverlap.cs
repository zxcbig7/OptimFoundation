using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>
    /// [Either-Or (Big-M)] ∀ lotA &lt; lotB, opA, opB ∈ Operation, eqp ∈ Eqp：跨批次兩作業若同機台不得重疊。
    /// Forward:  Complete_A ≤ Start_B + BigM·(ForwardOffset − Precede − Assign_A − Assign_B)
    /// Backward: Complete_B ≤ Start_A + BigM·(BackwardOffset + Precede − Assign_A − Assign_B)
    /// ForwardOffset(=3) / BackwardOffset(=2) 為 Either-Or pattern 結構常數，經 Parameter/CSV 取得。
    /// </summary>
    public sealed class Constraint_NoOverlap : ConstraintBase
    {
        private readonly List<Set_Lot> _lots;
        private readonly List<Set_Operation> _operations;
        private readonly List<Set_Eqp> _eqps;
        private readonly double _bigM;
        private readonly double _forwardOffset;
        private readonly double _backwardOffset;

        public Constraint_NoOverlap(
            List<Set_Lot> lots,
            List<Set_Operation> operations,
            List<Set_Eqp> eqps,
            double bigM,
            double forwardOffset,
            double backwardOffset)
        {
            _lots = lots;
            _operations = operations;
            _eqps = eqps;
            _bigM = bigM;
            _forwardOffset = forwardOffset;
            _backwardOffset = backwardOffset;
        }

        public void Build(OptEngine engine)
        {
            for (int a = 0; a < _lots.Count; a++)
            {
                for (int b = a + 1; b < _lots.Count; b++)
                {
                    var lotA = _lots[a];
                    var lotB = _lots[b];

                    foreach (var opA in _operations)
                    {
                        foreach (var opB in _operations)
                        {
                            foreach (var eqp in _eqps)
                            {
                                var precede = new VariableB_Precede { LotA = lotA, OperationA = opA, LotB = lotB, OperationB = opB };
                                var assignA = new VariableB_Assign { Lot = lotA, Operation = opA, Eqp = eqp };
                                var assignB = new VariableB_Assign { Lot = lotB, Operation = opB, Eqp = eqp };

                                // Forward: Complete_A <= Start_B + BigM*(ForwardOffset - Precede - Assign_A - Assign_B)
                                engine.AddLHS(1.0, new VariableC_Complete { Lot = lotA, Operation = opA });
                                engine.AddRHS(1.0, new VariableC_Start { Lot = lotB, Operation = opB });
                                engine.AddRHS(_forwardOffset * _bigM);
                                engine.AddRHS(-_bigM, precede);
                                engine.AddRHS(-_bigM, assignA);
                                engine.AddRHS(-_bigM, assignB);
                                engine.CreateLessEqual(this, "Fwd", lotA, opA, lotB, opB, eqp);

                                // Backward: Complete_B <= Start_A + BigM*(BackwardOffset + Precede - Assign_A - Assign_B)
                                engine.AddLHS(1.0, new VariableC_Complete { Lot = lotB, Operation = opB });
                                engine.AddRHS(1.0, new VariableC_Start { Lot = lotA, Operation = opA });
                                engine.AddRHS(_backwardOffset * _bigM);
                                engine.AddRHS(_bigM, precede);
                                engine.AddRHS(-_bigM, assignA);
                                engine.AddRHS(-_bigM, assignB);
                                engine.CreateLessEqual(this, "Bwd", lotA, opA, lotB, opB, eqp);
                            }
                        }
                    }
                }
            }
        }
    }
}
