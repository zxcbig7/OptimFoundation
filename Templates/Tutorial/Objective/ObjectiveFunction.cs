using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial
{
    /// <summary>OBJ：max Σ_{p,d,s} UnitProfit_p·Produce_{p,d,s} − Σ_{p,d,s} SetupCost_p·Setup_{p,d,s}（負係數 = 減項，不移項）</summary>
    public sealed class ObjectiveFunction
    {
        private readonly Set_Product products;
        private readonly Set_Date dates;
        private readonly Set_Shift shifts;
        private readonly List<Parameter_UnitProfit> unitProfit;
        private readonly List<Parameter_SetupCost> setupCost;

        public ObjectiveFunction(
            Set_Product products, Set_Date dates, Set_Shift shifts,
            List<Parameter_UnitProfit> unitProfit, List<Parameter_SetupCost> setupCost)
        {
            this.products = products;
            this.dates = dates;
            this.shifts = shifts;
            this.unitProfit = unitProfit;
            this.setupCost = setupCost;
        }

        public void Build(OptEngine engine)
        {
            foreach (var product in products)
            {
                var profit = unitProfit.First(p => p.Product == product).QTY;
                var cost = setupCost.First(p => p.Product == product).QTY;

                foreach (var date in dates)
                    foreach (var shift in shifts)
                    {
                        engine.AddLHS(profit, new VariableX_Produce { Product = product, Date = date, Shift = shift });
                        engine.AddLHS(-cost, new VariableB_Setup { Product = product, Date = date, Shift = shift });
                    }
            }

            engine.CreateMaximize();
        }
    }
}
