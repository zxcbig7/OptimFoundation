using OptimFoundation.Core;
using OptimFoundation.Core.IO;

namespace TSP_MultiDimSet
{
    /// <summary>TSP 資料唯一入口；只讀已就位的 Template CSV，不生成也不補值。</summary>
    public sealed partial class Dataload : DataContext
    {
        public const string InstanceName = "TSP_MultiDimSet";

        public List<Set_Node> set_Node = new();
        public List<Set_Depot> set_Depot = new();
        public List<Set_Customer> set_Customer = new();
        public List<Set_Arc> set_Arc = new();
        public List<Parameter_ArcCost> parameter_ArcCost = new();

        public Dataload() : this(new CsvDataSource()) { }

        /// <summary>標準接口：一行載一份 row list，只讀不算。</summary>
        public Dataload(IDataSource source)
        {
            set_Node = source.Load<Set_Node>("Set_Node");
            set_Depot = source.Load<Set_Depot>("Set_Depot");
            set_Customer = source.Load<Set_Customer>("Set_Customer");
            set_Arc = source.Load<Set_Arc>("Set_Arc");
            parameter_ArcCost = source.Load<Parameter_ArcCost>("Parameter_ArcCost");
        }
    }
}
