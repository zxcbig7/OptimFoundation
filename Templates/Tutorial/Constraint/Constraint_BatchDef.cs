using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial
{
    /// <summary>
    /// [=] ∀ product, date：Σ_shift Produce_{p,d,s} = BatchSize_p·Batch_{p,d}
    /// 展示 CreateEqual + 連結連續變數（Produce）與整數變數（Batch）——每日總產量必為整數批的倍數。
    /// </summary>
    public sealed class Constraint_BatchDef : ConstraintBase
    {
        private readonly List<Set_Product> products;
        private readonly List<Set_Date> dates;
        private readonly List<Set_Shift> shifts;
        private readonly List<Parameter_BatchSize> batchSize;

        public Constraint_BatchDef(
            List<Set_Product> products, List<Set_Date> dates, List<Set_Shift> shifts,
            List<Parameter_BatchSize> batchSize)
        {
            this.products = products;
            this.dates = dates;
            this.shifts = shifts;
            this.batchSize = batchSize;
        }

        public void Build(OptEngine engine)
        {
            foreach (var product in products)
                foreach (var date in dates)
                {
                    foreach (var shift in shifts)
                        engine.AddLHS(1.0, new VariableC_Produce { Product = product, Date = date, Shift = shift });

                    var size = batchSize.FindParameterOrLog(
                        b => b.Product == product,
                        product)?.QTY ?? 0.0;
                    engine.AddRHS(size, new VariableI_Batch { Product = product, Date = date });
                    engine.CreateEqual(this, product, date);
                }
        }
    }
}
