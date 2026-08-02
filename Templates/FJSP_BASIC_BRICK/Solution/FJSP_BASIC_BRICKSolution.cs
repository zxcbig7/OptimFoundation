using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK
{
    /// <summary>讀回 FJSP 解、逐條把解代回 Model.md 限制式驗證、列印排程並輸出 CSV。</summary>
    public sealed class FJSP_BASIC_BRICKSolution
    {
        private readonly Dataload _data;
        private readonly Dictionary<(string Lot, string Operation), string> _assignedEqp;
        private readonly Dictionary<(string Lot, string Operation), double> _start;
        private readonly Dictionary<(string Lot, string Operation), double> _complete;
        private readonly double _makespan;

        private FJSP_BASIC_BRICKSolution(
            Dataload data,
            Dictionary<(string, string), string> assignedEqp,
            Dictionary<(string, string), double> start,
            Dictionary<(string, string), double> complete,
            double makespan)
        {
            _data = data;
            _assignedEqp = assignedEqp;
            _start = start;
            _complete = complete;
            _makespan = makespan;
        }

        public static FJSP_BASIC_BRICKSolution ReadAndValidate(OptEngine engine, Dataload data)
        {
            Logging.Info($"Status={engine.Status} Obj={engine.GetObjectiveValue():F4}");

            var assignValues = engine.GetSetVarValues<VariableB_Assign>();
            var startValues = engine.GetSetVarValues<VariableX_Start>();
            var completeValues = engine.GetSetVarValues<VariableX_Complete>();
            var makespanValues = engine.GetSetVarValues<VariableX_Makespan>();

            var assignedEqp = new Dictionary<(string, string), string>();
            var start = new Dictionary<(string, string), double>();
            var complete = new Dictionary<(string, string), double>();

            foreach (var lot in data.set_Lot)
            {
                foreach (var op in data.set_Operation)
                {
                    var matched = data.set_Eqp
                        .Where(e => assignValues[$"VariableB_Assign@{lot}@{op}@{e}"] > 0.5)
                        .ToList();
                    if (matched.Count != 1)
                        throw new InvalidOperationException(
                            $"AssignOneEqp 違反 ({lot},{op})：指派了 {matched.Count} 台機台，預期恰好一台。");

                    assignedEqp[(lot, op)] = matched[0];
                    start[(lot, op)] = startValues[$"VariableX_Start@{lot}@{op}"];
                    complete[(lot, op)] = completeValues[$"VariableX_Complete@{lot}@{op}"];
                }
            }

            double makespan = makespanValues["VariableX_Makespan"];

            var solution = new FJSP_BASIC_BRICKSolution(data, assignedEqp, start, complete, makespan);
            solution.ValidateRules();

            FolderDir.Solution.CreateFolder();
            CsvCtrl.WriteSolution<VariableB_Assign>(engine, "FJSP_BASIC_BRICK", "SYSTEM");
            CsvCtrl.WriteSolution<VariableX_Start>(engine, "FJSP_BASIC_BRICK", "SYSTEM");
            CsvCtrl.WriteSolution<VariableX_Complete>(engine, "FJSP_BASIC_BRICK", "SYSTEM");

            return solution;
        }

        /// <summary>逐條把解代回 Model.md 的限制式；不成立就丟例外，NEVER 只記 log 繼續。</summary>
        private void ValidateRules()
        {
            const double eps = 1e-6;
            double floor = _data.parameter_MakespanFloor.Single().QTY;
            double deadline = _data.MakespanDeadline;

            foreach (var lot in _data.set_Lot)
            {
                // RoutePrecedence：下一道 ≥ 前一道完成
                for (int i = 0; i + 1 < _data.set_Operation.Count; i++)
                {
                    var op = _data.set_Operation[i];
                    var nextOp = _data.set_Operation[i + 1];
                    if (_start[(lot, nextOp)] < _complete[(lot, op)] - eps)
                        throw new InvalidOperationException(
                            $"RoutePrecedence 違反 {lot} {op}→{nextOp}：Start {_start[(lot, nextOp)]} < Complete {_complete[(lot, op)]}。");
                }

                // MakespanDef：Makespan ≥ 最後一道完成
                var lastOp = _data.set_Operation[_data.set_Operation.Count - 1];
                if (_makespan < _complete[(lot, lastOp)] - eps)
                    throw new InvalidOperationException(
                        $"MakespanDef 違反 {lot}：Makespan {_makespan} < Complete {_complete[(lot, lastOp)]}。");

                // CompleteDef：Complete = Start + ProcessTime（所指派機台）
                foreach (var op in _data.set_Operation)
                {
                    var procTime = _data.parameter_ProcessTime
                        .FirstOrDefault(p => p.Lot == lot && p.Operation == op && p.Eqp == _assignedEqp[(lot, op)])
                        ?.QTY ?? 0.0;
                    if (Math.Abs(_complete[(lot, op)] - _start[(lot, op)] - procTime) > 1e-4)
                        throw new InvalidOperationException(
                            $"CompleteDef 違反 {lot} {op}：Complete {_complete[(lot, op)]} ≠ Start {_start[(lot, op)]} + ProcessTime {procTime}。");
                }
            }

            // NoOverlap：同機台任兩作業時段不重疊
            var pairs = _assignedEqp.Keys.ToList();
            for (int i = 0; i < pairs.Count; i++)
                for (int j = i + 1; j < pairs.Count; j++)
                {
                    var a = pairs[i];
                    var b = pairs[j];
                    if (_assignedEqp[a] != _assignedEqp[b]) continue;

                    bool separated = _complete[a] <= _start[b] + eps || _complete[b] <= _start[a] + eps;
                    if (!separated)
                        throw new InvalidOperationException(
                            $"NoOverlap 違反 {a.Item1}.{a.Item2} × {b.Item1}.{b.Item2}@{_assignedEqp[a]}：" +
                            $"[{_start[a]},{_complete[a]}] 與 [{_start[b]},{_complete[b]}] 重疊。");
                }

            // MakespanWindow：makespan 必須落在 [floor, deadline]
            if (_makespan < floor - eps || _makespan > deadline + eps)
                throw new InvalidOperationException($"MakespanWindow 違反：{_makespan} 不在 [{floor}, {deadline}]。");

            Logging.Info("[FJSP_BASIC_BRICK] 全部 constraint 代回檢查 PASS。");
        }

        public void Print()
        {
            foreach (var lot in _data.set_Lot)
                foreach (var op in _data.set_Operation)
                    Console.WriteLine(
                        $"{lot} {op} -> {_assignedEqp[(lot, op)]}  [{_start[(lot, op)]:0.##}, {_complete[(lot, op)]:0.##}]");

            Console.WriteLine($"Makespan = {_makespan:0.##}");
        }
    }
}
