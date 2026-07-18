using System;
using System.Collections.Generic;
using Tutorial.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial.Constraint
{
    /// <summary>
    /// [soft 放鬆] 換線預算：Σ_{p,d,s} Setup_{p,d,s} ≤ SetupBudget（軟性，超過以 penalty 計）。
    /// 展示 CreateLeSoft——框架加 surplus 變數並把 penalty 併入目標式（maximize 時自動取 −penalty）。
    /// MUST 在 ObjectiveFunction 之後建構（penalty 加到已存在的目標式上）。
    /// </summary>
    public class Constraint_SetupBudgetSoft : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly IReadOnlyList<string> _products;
        private readonly IReadOnlyList<DateTime> _dates;
        private readonly IReadOnlyList<int> _shifts;
        private readonly double _budget;
        private readonly double _penalty;

        public Constraint_SetupBudgetSoft(
            IReadOnlyList<string> products, IReadOnlyList<DateTime> dates, IReadOnlyList<int> shifts,
            double budget, double penalty, OptEngine engine)
        {
            _products = products;
            _dates = dates;
            _shifts = shifts;
            _budget = budget;
            _penalty = penalty;
            _engine = engine;
        }

        public void Build()
        {
            foreach (var product in _products)
                foreach (var date in _dates)
                    foreach (var shift in _shifts)
                        _engine.AddLHS(1.0, new VariableB_Setup { Product = product, Date = date, Shift = shift });

            _engine.CreateLeSoft(_budget, _penalty);
            ConstraintCount++;

            Logging.Info($"[{ConstraintName}] budget={_budget} penalty={_penalty}");
        }
    }
}
