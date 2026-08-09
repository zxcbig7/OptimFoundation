using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace TSP_MultiDimSet
{
    /// <summary>讀取解、以題目規則驗證巡迴路線、列印並輸出 CSV。</summary>
    public sealed class TSP_MultiDimSetSolution
    {
        private readonly Dataload _data;

        /// <summary>被選用的弧，依巡迴順序排列。</summary>
        public IReadOnlyList<(string From, string To)> Tour { get; }

        public double TotalCost { get; }

        private TSP_MultiDimSetSolution(
            List<(string From, string To)> tour,
            double totalCost,
            Dataload data)
        {
            Tour = tour;
            TotalCost = totalCost;
            _data = data;
        }

        public static TSP_MultiDimSetSolution ReadAndValidate(OptEngine engine, Dataload data)
        {
            var selected = data.set_Arc
                .Where(arc => engine.GetVariableValue(
                    new VariableB_UseArc { From = arc.From, To = arc.To }.ToString()) > 0.5)
                .Select(arc => (arc.From, arc.To))
                .ToList();

            ValidateRules(selected, data);

            var tour = OrderTour(selected, data);
            double totalCost = selected.Sum(arc => data.parameter_ArcCost.FindParameterOrLog(
                parameter => parameter.From == arc.From && parameter.To == arc.To,
                arc.From, arc.To)?.QTY ?? 0.0);

            Logging.Info(
                $"[TSP solution] instance={Dataload.InstanceName} status={engine.Status} " +
                $"arcs={selected.Count} cost={totalCost:F4} validated=true");

            return new TSP_MultiDimSetSolution(tour, totalCost, data);
        }

        /// <summary>逐條把解代回 Model.md 的限制式；不成立就丟例外，NEVER 只記 log 繼續。</summary>
        private static void ValidateRules(
            IReadOnlyList<(string From, string To)> selected,
            Dataload data)
        {
            // [C1] 每個 customer 恰好一條入弧
            foreach (string customer in data.set_Customer)
            {
                int inDegree = selected.Count(arc => arc.To == customer);
                if (inDegree != 1)
                    throw new InvalidOperationException(
                        $"[C1] customer {customer} 的入弧數為 {inDegree}，預期 1。");
            }

            // [C2] 每個 customer 恰好一條出弧
            foreach (string customer in data.set_Customer)
            {
                int outDegree = selected.Count(arc => arc.From == customer);
                if (outDegree != 1)
                    throw new InvalidOperationException(
                        $"[C2] customer {customer} 的出弧數為 {outDegree}，預期 1。");
            }

            // [C3][C4] depot 恰好一出一入
            foreach (string depot in data.set_Depot)
            {
                int outDegree = selected.Count(arc => arc.From == depot);
                int inDegree = selected.Count(arc => arc.To == depot);
                if (outDegree != 1)
                    throw new InvalidOperationException(
                        $"[C3] depot {depot} 的出弧數為 {outDegree}，預期 1。");
                if (inDegree != 1)
                    throw new InvalidOperationException(
                        $"[C4] depot {depot} 的入弧數為 {inDegree}，預期 1。");
            }

            // [C5] 子迴圈消除的實質檢查：單一封閉巡迴必須涵蓋所有節點
            var tour = OrderTour(selected, data);
            if (tour.Count != data.set_Node.Count)
                throw new InvalidOperationException(
                    $"[C5] 巡迴只涵蓋 {tour.Count} 個節點，預期 {data.set_Node.Count}（存在子迴圈）。");

            // 選用的弧都必須是 ARC 的成員
            var arcSet = data.set_Arc.Select(arc => (arc.From, arc.To)).ToHashSet();
            foreach (var arc in selected)
                if (!arcSet.Contains(arc))
                    throw new InvalidOperationException(
                        $"解使用了不存在於 ARC 的弧 ({arc.From},{arc.To})。");
        }

        /// <summary>從 depot 出發沿選用弧走一圈，回傳造訪順序；遇到斷裂即停止。</summary>
        private static List<(string From, string To)> OrderTour(
            IReadOnlyList<(string From, string To)> selected,
            Dataload data)
        {
            var nextOf = selected.ToDictionary(arc => arc.From, arc => arc.To);
            string start = data.set_Depot.First().Node;

            var tour = new List<(string From, string To)>();
            string current = start;
            while (nextOf.TryGetValue(current, out string? next))
            {
                tour.Add((current, next));
                current = next;
                if (current == start)
                    break;
                if (tour.Count > selected.Count)
                    break;
            }

            return tour;
        }

        public void Print()
        {
            Console.WriteLine($"Total cost = {TotalCost:F4}");
            Console.WriteLine("Tour: " + string.Join(" -> ", Tour.Select(arc => arc.From).Append(Tour.Count > 0 ? Tour[^1].To : "-")));
        }
    }
}
