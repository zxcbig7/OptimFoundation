using ILOG.Concert;
using OptimFoundation.Cplex;
using OptimFoundation.Core;
using ThreadTest.Data;
using ThreadTest.VariableClass;

namespace ThreadTest.Benders
{
    /// <summary>
    /// Benders 子問題引擎。
    ///
    /// 子問題（LP，給定主問題 Y*）：
    ///   min  Σ_s Σ_d  vc[s,d] * X[s,d]
    ///   s.t. Σ_d X[s,d] ≤ supply[s] * Y*[s]   ∀s  [supply]
    ///        Σ_s X[s,d] ≥ demand[d]             ∀d  [demand]
    ///        X[s,d] ≥ 0
    ///
    /// 繼承 OptEngine 以直接存取 protected 成員：
    ///   Model  (Cplex)、ReadVar、AddLE、AddGE
    /// </summary>
    public class SubProblemEngine : OptEngine
    {
        private BendersDataload _data;

        // 追蹤限制式物件，供求解後取 dual 值
        private readonly List<(string source, IRange r)> _supplyConstraints = new();
        private readonly List<(string dest, IRange r)> _demandConstraints = new();

        public SubProblemEngine(CplexConfig config) : base(config) { }

        // ─────────────────────────────────────────
        // 初始化（只做一次）
        // ─────────────────────────────────────────

        public void InitializeVariables(BendersDataload data)
        {
            _data = data;
            BuildCVs<VariableX_Supply>(data.Sources, data.Dests);
        }

        // ─────────────────────────────────────────
        // 每輪迭代：ResetConstraint → 重建目標式與限制式
        // ─────────────────────────────────────────

        public void RebuildWithY(Dictionary<string, double> yFixed)
        {
            ResetConstraint();          // 清空目標式、限制式（變數保留）
            _supplyConstraints.Clear();
            _demandConstraints.Clear();

            BuildObjective();
            BuildConstraints(yFixed);
        }

        private void BuildObjective()
        {
            foreach (var s in _data.Sources)
                foreach (var d in _data.Dests)
                {
                    double cost = _data.parameter_Cost
                        .First(p => p.Source == s && p.Dest == d).QTY;
                    AddLHS(cost, new VariableX_Supply { Source = s, Dest = d });
                }
            CreateMinimize();
        }

        private void BuildConstraints(Dictionary<string, double> yFixed)
        {
            // Supply：Σ_d X[s,d] ≤ supply[s] * Y*[s]
            foreach (var s in _data.Sources)
            {
                var lhs = Model.LinearNumExpr();
                foreach (var d in _data.Dests)
                    lhs.AddTerm(1.0, ReadVar(new VariableX_Supply { Source = s, Dest = d }));

                double rhs = _data.Supply[s] * yFixed[s];
                var r = AddLE($"Supply@{s}", lhs, rhs);
                _supplyConstraints.Add((s, r));
            }

            // Demand：Σ_s X[s,d] ≥ demand[d]
            foreach (var d in _data.Dests)
            {
                var lhs = Model.LinearNumExpr();
                foreach (var s in _data.Sources)
                    lhs.AddTerm(1.0, ReadVar(new VariableX_Supply { Source = s, Dest = d }));

                var r = AddGE($"Demand@{d}", lhs, _data.Demand[d]);
                _demandConstraints.Add((d, r));
            }
        }

        // ─────────────────────────────────────────
        // Dual 值存取（供 Benders cut 生成使用）
        //   CPLEX 最小化：≤ 約束 dual ≤ 0，≥ 約束 dual ≥ 0
        // ─────────────────────────────────────────

        public double GetSupplyDual(string source)
            => Model.GetDual(_supplyConstraints.First(x => x.source == source).r);

        public double GetDemandDual(string dest)
            => Model.GetDual(_demandConstraints.First(x => x.dest == dest).r);
    }
}
