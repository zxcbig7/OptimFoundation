using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial
{
    /// <summary>
    /// [≤] fixed-charge Big-M：∀ product, date, shift：Produce_{p,d,s} ≤ BigM·Setup_{p,d,s}
    /// 不開線（Setup=0）→ 該班該品產量強制為 0。BigM 由數據推導（見 Dataload.BigM），NEVER 寫死。
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
                        engine.AddLHS(1.0, new VariableX_Produce { Product = product, Date = date, Shift = shift });
                        engine.AddRHS(bigM, new VariableB_Setup { Product = product, Date = date, Shift = shift });
                        engine.CreateLessEqual($"{ConstraintName}@{product}@{date:yyyy-MM-dd}@{shift}");
                    }
        }
    }
}
