using System;
using System.Collections.Generic;
using Tutorial.ParameterClass;
using Tutorial.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial.Constraint
{
    /// <summary>
    /// [=] ∀ product, date：Σ_shift Produce_{p,d,s} = BatchSize_p·Batch_{p,d}
    /// 展示 CreateEqual + 連結連續變數（Produce）與整數變數（Batch）——每日總產量必為整數批的倍數。
    /// </summary>
    public class Constraint_BatchDef : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly IReadOnlyList<string> _products;
        private readonly IReadOnlyList<DateTime> _dates;
        private readonly IReadOnlyList<int> _shifts;
        private readonly List<Parameter_BatchSize> _batchSize;

        public Constraint_BatchDef(
            IReadOnlyList<string> products, IReadOnlyList<DateTime> dates, IReadOnlyList<int> shifts,
            List<Parameter_BatchSize> batchSize, OptEngine engine)
        {
            _products = products;
            _dates = dates;
            _shifts = shifts;
            _batchSize = batchSize;
            _engine = engine;
        }

        public void Build()
        {
            foreach (var product in _products)
                foreach (var date in _dates)
                {
                    // 左：Σ_shift Produce
                    foreach (var shift in _shifts)
                        _engine.AddLHS(1.0, new VariableX_Produce { Product = product, Date = date, Shift = shift });

                    // 右：BatchSize·Batch（整數變數）
                    var size = _batchSize.First(b => b.Product == product).QTY;
                    _engine.AddRHS(size, new VariableI_Batch { Product = product, Date = date });
                    _engine.CreateEqual($"{ConstraintName}@{product}@{date:yyyy-MM-dd}");
                    ConstraintCount++;
                }

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
