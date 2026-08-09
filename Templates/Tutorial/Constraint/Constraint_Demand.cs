using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial
{
    /// <summary>
    /// [≥] ∀ product, date：Σ_shift Produce_{p,d,s} ≥ Demand_{p,d}
    /// 展示 CreateGreatEqual + 2D 參數（Demand，含 DateTime 維度）。
    /// </summary>
    public sealed class Constraint_Demand : ConstraintBase
    {
        private readonly List<Set_Product> products;
        private readonly List<Set_Date> dates;
        private readonly List<Set_Shift> shifts;
        private readonly List<Parameter_Demand> demand;

        public Constraint_Demand(
            List<Set_Product> products, List<Set_Date> dates, List<Set_Shift> shifts,
            List<Parameter_Demand> demand)
        {
            this.products = products;
            this.dates = dates;
            this.shifts = shifts;
            this.demand = demand;
        }

        public void Build(OptEngine engine)
        {
            foreach (var product in products)
                foreach (var date in dates)
                {
                    foreach (var shift in shifts)
                        engine.AddLHS(1.0, new VariableC_Produce { Product = product, Date = date, Shift = shift });

                    var req = demand.FindParameterOrLog(
                        d => d.Product == product && d.Date == date,
                        product, date)?.QTY ?? 0.0;
                    engine.AddRHS(req);
                    engine.CreateGreatEqual(this, product, date);
                }
        }
    }
}
