using FJSP_BASIC_BRICK.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK.Constraint
{
    /// <summary>
    /// [Either-Or (Big-M)] ∀ lotA &lt; lotB, opA, opB, eqp：跨批次兩作業若同機台不得重疊。
    /// Forward:  Complete_A ≤ Start_B + BigM·(3 − Precede − Assign_A − Assign_B)
    /// Backward: Complete_B ≤ Start_A + BigM·(2 + Precede − Assign_A − Assign_B)
    /// 式中 3 / 2 為 Either-Or pattern 結構常數（非數據）。
    /// </summary>
    public class Constraint_NoOverlap : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly IReadOnlyList<string> _lots;
        private readonly IReadOnlyList<string> _operations;
        private readonly IReadOnlyList<string> _eqps;
        private readonly double _bigM;

        public Constraint_NoOverlap(
            IReadOnlyList<string> lots, IReadOnlyList<string> operations, IReadOnlyList<string> eqps,
            double bigM, OptEngine engine)
        {
            _lots = lots;
            _operations = operations;
            _eqps = eqps;
            _bigM = bigM;
            _engine = engine;
        }

        public void Build()
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

                                // Forward: Complete_A <= Start_B + BigM*(3 - Precede - Assign_A - Assign_B)
                                _engine.AddLHS(1.0, new VariableX_Complete { Lot = lotA, Operation = opA });
                                _engine.AddRHS(1.0, new VariableX_Start { Lot = lotB, Operation = opB });
                                _engine.AddRHS(3.0 * _bigM);
                                _engine.AddRHS(-_bigM, precede);
                                _engine.AddRHS(-_bigM, assignA);
                                _engine.AddRHS(-_bigM, assignB);
                                _engine.CreateLessEqual($"{ConstraintName}_Fwd@{lotA}@{opA}@{lotB}@{opB}@{eqp}");

                                // Backward: Complete_B <= Start_A + BigM*(2 + Precede - Assign_A - Assign_B)
                                _engine.AddLHS(1.0, new VariableX_Complete { Lot = lotB, Operation = opB });
                                _engine.AddRHS(1.0, new VariableX_Start { Lot = lotA, Operation = opA });
                                _engine.AddRHS(2.0 * _bigM);
                                _engine.AddRHS(_bigM, precede);
                                _engine.AddRHS(-_bigM, assignA);
                                _engine.AddRHS(-_bigM, assignB);
                                _engine.CreateLessEqual($"{ConstraintName}_Bwd@{lotA}@{opA}@{lotB}@{opB}@{eqp}");
                            }
                        }
                    }
                }
            }

        }
    }
}
