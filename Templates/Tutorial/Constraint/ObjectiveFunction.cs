using Tutorial.Data;
using Tutorial.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial.Constraint
{
    /// <summary>OBJ：max Σ_{p,d,s} UnitProfit_p·Produce_{p,d,s} − Σ_{p,d,s} SetupCost_p·Setup_{p,d,s}（負係數 = 減項，不移項）</summary>
    public class ObjectiveFunction
    {
        private readonly OptEngine _engine;
        private readonly Dataload _data;

        public ObjectiveFunction(Dataload data, OptEngine engine)
        {
            _data = data;
            _engine = engine;
        }

        public void Build()
        {
            foreach (var product in _data.PRODUCT)
            {
                var profit = _data.parameter_UnitProfit.First(p => p.Product == product).QTY;
                var setupCost = _data.parameter_SetupCost.First(p => p.Product == product).QTY;

                foreach (var date in _data.DATE)
                    foreach (var shift in _data.SHIFT)
                    {
                        _engine.AddLHS(profit, new VariableX_Produce { Product = product, Date = date, Shift = shift });
                        _engine.AddLHS(-setupCost, new VariableB_Setup { Product = product, Date = date, Shift = shift });
                    }
            }

            _engine.CreateMaximize();
            Logging.Info("[Objective] max Σ profit·Produce − Σ setupCost·Setup");
        }
    }
}
