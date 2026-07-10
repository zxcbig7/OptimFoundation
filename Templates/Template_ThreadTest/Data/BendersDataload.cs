namespace ThreadTest.Data
{
    /// <summary>
    /// Benders Decomposition 專用資料。
    /// 需求刻意設小（D1=3, D2=5），確保單一貨源即可滿足全部需求，
    /// 使子問題永遠可行（不需處理 feasibility cut）。
    ///
    /// 最佳解分析（手算驗證用）：
    ///   只開 A：固定成本 5  + 運輸 2*3+3*5=21  = 26  ← 全局最佳
    ///   只開 B：固定成本 6  + 運輸 1*3+4*5=23  = 29
    ///   都開  ：固定成本 11 + 運輸 1*3+3*5=18  = 29
    /// 預期 Benders 結果：Y[A]=1, Y[B]=0, ObjVal = 26，2 輪收斂
    /// </summary>
    public class BendersDataload
    {
        public List<string> Sources = new List<string>();
        public List<string> Dests = new List<string>();

        public Dictionary<string, double> Supply = new Dictionary<string, double>();
        public Dictionary<string, double> FixedCost = new Dictionary<string, double>();
        public Dictionary<string, double> Demand = new Dictionary<string, double>();

        public List<Parameter_Cost> parameter_Cost = new List<Parameter_Cost>();

        public BendersDataload()
        {
            Sources.AddRange(new[] { "A", "B" });
            Dests.AddRange(new[] { "D1", "D2" });

            Supply["A"] = 10;
            Supply["B"] = 8;

            FixedCost["A"] = 5;
            FixedCost["B"] = 6;

            Demand["D1"] = 3;
            Demand["D2"] = 5;

            parameter_Cost.Add(new Parameter_Cost("A", "D1", 2.0));
            parameter_Cost.Add(new Parameter_Cost("A", "D2", 3.0));
            parameter_Cost.Add(new Parameter_Cost("B", "D1", 1.0));
            parameter_Cost.Add(new Parameter_Cost("B", "D2", 4.0));
        }
    }
}
