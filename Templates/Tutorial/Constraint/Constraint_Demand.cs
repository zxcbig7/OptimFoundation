using System;
using System.Collections.Generic;
using Tutorial.ParameterClass;
using Tutorial.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial.Constraint
{
    /// <summary>
    /// [≥] ∀ product, date：Σ_shift Produce_{p,d,s} ≥ Demand_{p,d}
    /// 展示 CreateGreatEqual + 2D 參數（Demand，含 DateTime 維度）。
    /// </summary>
    public class Constraint_Demand : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly IReadOnlyList<string> _products;
        private readonly IReadOnlyList<DateTime> _dates;
        private readonly IReadOnlyList<int> _shifts;
        private readonly List<Parameter_Demand> _demand;

        public Constraint_Demand(
            IReadOnlyList<string> products, IReadOnlyList<DateTime> dates, IReadOnlyList<int> shifts,
            List<Parameter_Demand> demand, OptEngine engine)
        {
            _products = products;
            _dates = dates;
            _shifts = shifts;
            _demand = demand;
            _engine = engine;
        }

        public void Build()
        {
            foreach (var product in _products)
                foreach (var date in _dates)
                {
                    foreach (var shift in _shifts)
                        _engine.AddLHS(1.0, new VariableX_Produce { Product = product, Date = date, Shift = shift });

                    var req = _demand.First(d => d.Product == product && d.Date == date).QTY;
                    _engine.AddRHS(req);
                    _engine.CreateGreatEqual($"{ConstraintName}@{product}@{date:yyyy-MM-dd}");
                    ConstraintCount++;
                }

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
