using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial
{
    /// <summary>
    /// [≤] fixed-charge Big-M：∀ product, date, shift：Produce_{p,d,s} ≤ BigM·Setup_{p,d,s}
    /// 未開線（Setup=0）時，該班該產品的產量必須為 0；BigM 由輸入資料計算，見 Dataload.BigM。
    /// </summary>
    public sealed class Constraint_SetupLink : ConstraintBase
    {
        private readonly List<Set_Product> products;
        private readonly List<Set_Date> dates;
        private readonly List<Set_Shift> shifts;
        private readonly double bigM;

        public Constraint_SetupLink(
            List<Set_Product> products, List<Set_Date> dates, List<Set_Shift> shifts, double bigM)
        {
            this.products = products;
            this.dates = dates;
            this.shifts = shifts;
            this.bigM = bigM;
        }

        public void Build(OptEngine engine)
        {
            foreach (var product in products)
                foreach (var date in dates)
                    foreach (var shift in shifts)
                    {
                        engine.AddLHS(1.0, new VariableC_Produce { Product = product, Date = date, Shift = shift });
                        engine.AddRHS(bigM, new VariableB_Setup { Product = product, Date = date, Shift = shift });
                        engine.CreateLessEqual(this, product, date, shift);
                    }
        }
    }
}
