using OptimFoundation.Core;
using OptimFoundation.Core.IO;

namespace TSP_MultiDimSet
{
    /// <summary>從已備妥的標準 CSV 載入 TSP 集合與成本資料，不在這裡產生資料或補缺值。</summary>
    public sealed partial class Dataload : DataContext
    {
        public const string InstanceName = "TSP_MultiDimSet";

        public List<Set_Node> set_Node = new();
        public List<Set_Depot> set_Depot = new();
        public List<Set_Customer> set_Customer = new();
        public List<Set_Arc> set_Arc = new();
        public List<Parameter_ArcCost> parameter_ArcCost = new();

        public Dataload() : this(new CsvDataSource()) { }

        /// <summary>逐一載入各集合與成本資料清單，不在載入時推算其他值。</summary>
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
