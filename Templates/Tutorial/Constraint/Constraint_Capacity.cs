using System;
using System.Collections.Generic;
using Tutorial.ParameterClass;
using Tutorial.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial.Constraint
{
    /// <summary>
    /// [≤] ∀ machine, date, shift：Σ_product MachineHours_{p,m}·Produce_{p,d,s} ≤ Capacity_{m,d,s}
    /// 展示 CreateLessEqual + 3D 參數（Capacity）+ 3D 變數（Produce）。
    /// </summary>
    public class Constraint_Capacity : ConstraintBase
    {
        private readonly OptEngine _engine;
        private readonly IReadOnlyList<string> _products;
        private readonly IReadOnlyList<string> _machines;
        private readonly IReadOnlyList<DateTime> _dates;
        private readonly IReadOnlyList<int> _shifts;
        private readonly List<Parameter_MachineHours> _hours;
        private readonly List<Parameter_Capacity> _capacity;

        public Constraint_Capacity(
            IReadOnlyList<string> products, IReadOnlyList<string> machines,
            IReadOnlyList<DateTime> dates, IReadOnlyList<int> shifts,
            List<Parameter_MachineHours> hours, List<Parameter_Capacity> capacity, OptEngine engine)
        {
            _products = products;
            _machines = machines;
            _dates = dates;
            _shifts = shifts;
            _hours = hours;
            _capacity = capacity;
            _engine = engine;
        }

        public void Build()
        {
            foreach (var machine in _machines)
                foreach (var date in _dates)
                    foreach (var shift in _shifts)
                    {
                        foreach (var product in _products)
                        {
                            var h = _hours.FirstOrDefault(x => x.Product == product && x.Machine == machine)?.QTY ?? 0.0;
                            _engine.AddLHS(h, new VariableX_Produce { Product = product, Date = date, Shift = shift });
                        }

                        var cap = _capacity.First(c => c.Machine == machine && c.Date == date && c.Shift == shift).QTY;
                        _engine.AddRHS(cap);
                        _engine.CreateLessEqual($"{ConstraintName}@{machine}@{date:yyyy-MM-dd}@{shift}");
                        ConstraintCount++;
                    }

            Logging.Info($"[{ConstraintName}] {ConstraintCount}");
        }
    }
}
