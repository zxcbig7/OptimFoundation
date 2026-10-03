using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace Tutorial
{
    // 更換 IDataSource 即可切換來源；型別化 DbDataSource 用法見 developer-guide。
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

        // 單班單品產量上界：BigM = max Capacity / min{正的 MachineHours}。
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

            // 參數也用 Load<T> 載入；產生的參數類別固定有 QTY 欄位，用來存放參數值。
            parameter_UnitProfit = source.Load<Parameter_UnitProfit>("Parameter_UnitProfit");
            parameter_SetupCost = source.Load<Parameter_SetupCost>("Parameter_SetupCost");
            parameter_BatchSize = source.Load<Parameter_BatchSize>("Parameter_BatchSize");
            parameter_MachineHours = source.Load<Parameter_MachineHours>("Parameter_MachineHours");
            parameter_Demand = source.Load<Parameter_Demand>("Parameter_Demand");
            parameter_Capacity = source.Load<Parameter_Capacity>("Parameter_Capacity");
        }
    }
}
