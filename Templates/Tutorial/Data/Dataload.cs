using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace Tutorial
{
    // 資料層唯一入口：ctor 就是「寫讀檔的家」——每行一句、顯式讀檔。
    // 換來源：CSV↔InMemory 只換傳入的 IDataSource（DB query-only 用型別化 DbDataSource，見 developer-guide）。
    public sealed partial class Dataload : DataContext
    {
        // Set 資料列：維度型別包含 string / DateTime / int
        public List<Set_Product> set_Product = new();
        public List<Set_Machine> set_Machine = new();
        public List<Set_Date> set_Date = new();
        public List<Set_Shift> set_Shift = new();

        public List<Parameter_UnitProfit> parameter_UnitProfit = new();
        public List<Parameter_SetupCost> parameter_SetupCost = new();
        public List<Parameter_BatchSize> parameter_BatchSize = new();
        public List<Parameter_MachineHours> parameter_MachineHours = new();
        public List<Parameter_Demand> parameter_Demand = new();
        public List<Parameter_Capacity> parameter_Capacity = new();

        // BigM = max Capacity / min{正的 MachineHours}：單班單品產量上界，由數據推導、NEVER 寫死
        public double BigM => parameter_Capacity.Max(c => c.QTY) /
            parameter_MachineHours.Where(h => h.QTY > 0).Min(h => h.QTY);

        public Dataload() : this(new CsvDataSource()) { }

        public Dataload(IDataSource source)
        {
            // Set：Load<T> 依表頭映射 public property，並把 DateTime/int 轉成宣告型別
            set_Product = source.Load<Set_Product>("Set_Product");
            set_Machine = source.Load<Set_Machine>("Set_Machine");
            set_Date = source.Load<Set_Date>("Set_Date");
            set_Shift = source.Load<Set_Shift>("Set_Shift");

            // Parameter：同樣走 Load<T>；差別只在生成型別最後固定多一個 QTY
            parameter_UnitProfit = source.Load<Parameter_UnitProfit>("Parameter_UnitProfit");
            parameter_SetupCost = source.Load<Parameter_SetupCost>("Parameter_SetupCost");
            parameter_BatchSize = source.Load<Parameter_BatchSize>("Parameter_BatchSize");
            parameter_MachineHours = source.Load<Parameter_MachineHours>("Parameter_MachineHours");
            parameter_Demand = source.Load<Parameter_Demand>("Parameter_Demand");
            parameter_Capacity = source.Load<Parameter_Capacity>("Parameter_Capacity");
        }
    }
}
