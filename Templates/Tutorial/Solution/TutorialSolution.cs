using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace Tutorial
{
    /// <summary>讀回解、以題目規則驗證、列印並輸出 CSV。</summary>
    public sealed class TutorialSolution
    {
        private readonly Dictionary<string, double> produce;

        private TutorialSolution(Dictionary<string, double> produce) => this.produce = produce;

        public static TutorialSolution ReadAndValidate(OptEngine engine, Dataload data)
        {
            Logging.Info($"Status={engine.Status} Obj={engine.GetObjectiveValue():F4} " +
                         $"BestBound={engine.BestObjValue:F4} MIPGap={engine.MIPGap:P2}");

            var produce = engine.GetSetVarValues<VariableC_Produce>();
            var setup = engine.GetSetVarValues<VariableB_Setup>();
            var batch = engine.GetSetVarValues<VariableI_Batch>();
            ValidateRules(produce, setup, batch, data);

            FolderDir.Solution.CreateFolder(); // MUST，否則 WriteSolution 丟 DirectoryNotFoundException
            CsvCtrl.WriteSolution<VariableC_Produce>(engine, "Tutorial", "SYSTEM");
            CsvCtrl.WriteSolution<VariableB_Setup>(engine, "Tutorial", "SYSTEM");
            CsvCtrl.WriteSolution<VariableI_Batch>(engine, "Tutorial", "SYSTEM");
            return new TutorialSolution(produce);
        }

        /// <summary>逐條把解代回 Model.md 的限制式；不成立就丟例外，NEVER 只記 log 繼續。</summary>
        private static void ValidateRules(
            Dictionary<string, double> produce, Dictionary<string, double> setup, Dictionary<string, double> batch,
            Dataload data)
        {
            double ProduceOf(string product, DateTime date, int shift) =>
                produce.TryGetValue(new VariableC_Produce
                {
                    Product = product,
                    Date = date,
                    Shift = shift
                }.ToString(), out var v) ? v : 0.0;

            // Demand：Σ_shift Produce ≥ Demand
            foreach (var product in data.set_Product)
                foreach (var date in data.set_Date)
                {
                    var used = data.set_Shift.Sum(shift => ProduceOf(product, date, shift));
                    var required = data.parameter_Demand.FindParameterOrLog(
                        d => d.Product == product && d.Date == date,
                        product, date)?.QTY ?? 0.0;
                    if (used < required - 1e-6)
                        throw new InvalidOperationException($"{product}@{date:yyyy-MM-dd} 違反 Demand：{used} < {required}。");
                }

            // Capacity：Σ_product MachineHours·Produce ≤ Capacity
            foreach (var machine in data.set_Machine)
                foreach (var date in data.set_Date)
                    foreach (var shift in data.set_Shift)
                    {
                        var used = data.set_Product.Sum(product =>
                        {
                            var hours = data.parameter_MachineHours.FindParameterOrLog(
                                h => h.Product == product && h.Machine == machine,
                                product, machine)?.QTY ?? 0.0;
                            return hours * ProduceOf(product, date, shift);
                        });
                        var cap = data.parameter_Capacity.FindParameterOrLog(
                            c => c.Machine == machine && c.Date == date && c.Shift == shift,
                            machine, date, shift)?.QTY ?? 0.0;
                        if (used > cap + 1e-6)
                            throw new InvalidOperationException($"{machine}@{date:yyyy-MM-dd}@{shift} 違反 Capacity：{used} > {cap}。");
                    }

            // BatchDef：Σ_shift Produce = BatchSize·Batch
            foreach (var product in data.set_Product)
                foreach (var date in data.set_Date)
                {
                    var used = data.set_Shift.Sum(shift => ProduceOf(product, date, shift));
                    var size = data.parameter_BatchSize.FindParameterOrLog(
                        b => b.Product == product,
                        product)?.QTY ?? 0.0;
                    var batches = batch.TryGetValue(new VariableI_Batch
                    {
                        Product = product,
                        Date = date
                    }.ToString(), out var b) ? b : 0.0;
                    if (Math.Abs(used - size * batches) > 1e-6)
                        throw new InvalidOperationException($"{product}@{date:yyyy-MM-dd} 違反 BatchDef：{used} ≠ {size}×{batches}。");
                }

            // SetupLink：Produce ≤ BigM·Setup
            foreach (var product in data.set_Product)
                foreach (var date in data.set_Date)
                    foreach (var shift in data.set_Shift)
                    {
                        var qty = ProduceOf(product, date, shift);
                        var opened = setup.TryGetValue(new VariableB_Setup
                        {
                            Product = product,
                            Date = date,
                            Shift = shift
                        }.ToString(), out var s) ? s : 0.0;
                        if (qty > data.BigM * opened + 1e-6)
                            throw new InvalidOperationException($"{product}@{date:yyyy-MM-dd}@{shift} 違反 SetupLink：{qty} > BigM×{opened}。");
                    }
        }

        public void Print()
        {
            foreach (var kv in produce.Where(kv => kv.Value > 1e-6))
                Console.WriteLine($"{kv.Key} = {kv.Value:0.##}");
        }
    }
}
