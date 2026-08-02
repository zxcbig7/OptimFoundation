using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial
{
    /// <summary>
    /// [≤] ∀ machine, date, shift：Σ_product MachineHours_{p,m}·Produce_{p,d,s} ≤ Capacity_{m,d,s}
    /// 展示 CreateLessEqual + 3D 參數（Capacity）+ 3D 變數（Produce）。
    /// </summary>
    public sealed class Constraint_Capacity : ConstraintBase
    {
        private readonly Set_Product products;
        private readonly Set_Machine machines;
        private readonly Set_Date dates;
        private readonly Set_Shift shifts;
        private readonly List<Parameter_MachineHours> hours;
        private readonly List<Parameter_Capacity> capacity;

        public Constraint_Capacity(
            Set_Product products, Set_Machine machines, Set_Date dates, Set_Shift shifts,
            List<Parameter_MachineHours> hours, List<Parameter_Capacity> capacity)
        {
            this.products = products;
            this.machines = machines;
            this.dates = dates;
            this.shifts = shifts;
            this.hours = hours;
            this.capacity = capacity;
        }

        public void Build(OptEngine engine)
        {
            foreach (var machine in machines)
                foreach (var date in dates)
                    foreach (var shift in shifts)
                    {
                        foreach (var product in products)
                        {
                            var h = hours.FirstOrDefault(x => x.Product == product && x.Machine == machine)?.QTY ?? 0.0;
                            engine.AddLHS(h, new VariableX_Produce { Product = product, Date = date, Shift = shift });
                        }

                        var cap = capacity.First(c => c.Machine == machine && c.Date == date && c.Shift == shift).QTY;
                        engine.AddRHS(cap);
                        engine.CreateLessEqual($"{ConstraintName}@{machine}@{date:yyyy-MM-dd}@{shift}");
                    }
        }
    }
}
