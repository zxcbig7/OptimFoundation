using System;
using System.Collections.Generic;
using Tutorial.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial.Constraint
{
    /// <summary>
    /// [≤] fixed-charge Big-M：∀ product, date, shift：Produce_{p,d,s} ≤ BigM·Setup_{p,d,s}
    /// 不開線（Setup=0）→ 該班該品產量強制為 0。BigM 由數據推導（見 Dataload.BigM），NEVER 寫死。
    /// </summary>
    public class Constraint_SetupLink : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly IReadOnlyList<string> _products;
        private readonly IReadOnlyList<DateTime> _dates;
        private readonly IReadOnlyList<int> _shifts;
        private readonly double _bigM;

        public Constraint_SetupLink(
            IReadOnlyList<string> products, IReadOnlyList<DateTime> dates, IReadOnlyList<int> shifts,
            double bigM, OptEngine engine)
        {
            _products = products;
            _dates = dates;
            _shifts = shifts;
            _bigM = bigM;
            _engine = engine;
        }

        public void Build()
        {
            foreach (var product in _products)
                foreach (var date in _dates)
                    foreach (var shift in _shifts)
                    {
                        _engine.AddLHS(1.0, new VariableX_Produce { Product = product, Date = date, Shift = shift });
                        _engine.AddRHS(_bigM, new VariableB_Setup { Product = product, Date = date, Shift = shift });
                        _engine.CreateLessEqual($"{ConstraintName}@{product}@{date:yyyy-MM-dd}@{shift}");
                        ConstraintCount++;
                    }

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
