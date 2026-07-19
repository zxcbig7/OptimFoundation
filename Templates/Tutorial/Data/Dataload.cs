using Tutorial.ParameterClass;
using Tutorial.SetClass;
using Tutorial.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace Tutorial.Data
{
    // 資料層唯一入口：ctor 就是「寫讀檔的家」——每行一句、顯式讀檔。
    // 換來源：CSV↔InMemory 只換傳入的 IDataSource（DB query-only 用型別化 DbDataSource，見 developer-guide）。
    public partial class Dataload : DataContext
    {
        // Set 積木：三種元素型別 string / DateTime / int
        public Set_Product PRODUCT = new();
        public Set_Machine MACHINE = new();
        public Set_Date DATE = new();
        public Set_Shift SHIFT = new();

        public List<Parameter_UnitProfit> parameter_UnitProfit = new();
        public List<Parameter_SetupCost> parameter_SetupCost = new();
        public List<Parameter_BatchSize> parameter_BatchSize = new();
        public List<Parameter_MachineHours> parameter_MachineHours = new();
        public List<Parameter_Demand> parameter_Demand = new();
        public List<Parameter_Capacity> parameter_Capacity = new();

        // BigM = max Capacity / min{正的 MachineHours}：單班單品產量上界，由數據推導、NEVER 寫死
        public double BigM => Numeric.SafeRatio(
            parameter_Capacity.Max(c => c.QTY),
            parameter_MachineHours.Where(h => h.QTY > 0).Min(h => h.QTY),
            context: "BigM");

        // soft 換線預算（demo）：需滿足 3 產品 × 2 日 → 至少 6 次開線 > 4，soft 違反現形
        public double SetupBudget = 4;
        public double SetupPenalty = 5;

        // 預設資料來源＝CSV；Program 的 inmemory 模式傳 InMemoryDataSource 進來示範換來源
        public Dataload() : this(new CsvDataSource()) { }

        public Dataload(IDataSource source)
        {
            // Set：檔名形式 Set_{X}（CSV → Data/Set_{X}.csv；DateTime/int 由 ParseElement 自動轉型）
            PRODUCT.Load(source, "Set_Product");
            MACHINE.Load(source, "Set_Machine");
            DATE.Load(source, "Set_Date");
            SHIFT.Load(source, "Set_Shift");

            // Parameter：CSV 讀 Data/{檔名}.csv（表頭按名對位；DateTime/int 欄由 InitClassBySets 轉型）
            parameter_UnitProfit = source.LoadParam<Parameter_UnitProfit>("Parameter_UnitProfit");
            parameter_SetupCost = source.LoadParam<Parameter_SetupCost>("Parameter_SetupCost");
            parameter_BatchSize = source.LoadParam<Parameter_BatchSize>("Parameter_BatchSize");
            parameter_MachineHours = source.LoadParam<Parameter_MachineHours>("Parameter_MachineHours");
            parameter_Demand = source.LoadParam<Parameter_Demand>("Parameter_Demand");
            parameter_Capacity = source.LoadParam<Parameter_Capacity>("Parameter_Capacity");
        }

        /// <summary>解出後印出生產計畫（示範讀解 API：GetSetVarValues / GetObjectiveValue，含 DateTime/int key）。</summary>
        public void WriteSolution(OptEngine engine)
        {
            var produce = engine.GetSetVarValues<VariableX_Produce>();
            var setup = engine.GetSetVarValues<VariableB_Setup>();
            var batch = engine.GetSetVarValues<VariableI_Batch>();
            double objective = engine.GetObjectiveValue();

            Logging.Info("===== Tutorial 解（多期多班次生產）=====");
            Logging.Info($"Objective（利潤 − 開線成本 − soft penalty）= {objective:0.##}");

            foreach (var date in DATE)
                foreach (var product in PRODUCT)
                {
                    int batches = (int)Math.Round(batch[$"VariableI_Batch@{product}@{date:yyyy-MM-dd}"]);
                    if (batches == 0) continue;   // 當日沒排這個品就略過
                    foreach (var shift in SHIFT)
                    {
                        double qty = produce[$"VariableX_Produce@{product}@{date:yyyy-MM-dd}@{shift}"];
                        bool opened = setup[$"VariableB_Setup@{product}@{date:yyyy-MM-dd}@{shift}"] > 0.5;
                        if (qty > 1e-6 || opened)
                            Logging.Info($"{date:MM-dd} 班{shift} {product}: {(opened ? "開線" : "     ")} 生產 {qty:0.##} 件");
                    }
                    Logging.Info($"  → {date:MM-dd} {product} 共 {batches} 批");
                }
        }
    }
}
