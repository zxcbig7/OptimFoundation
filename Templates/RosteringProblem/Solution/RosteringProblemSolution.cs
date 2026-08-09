using OptimFoundation.Core;
using OptimFoundation.Core.IO;
using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>讀取、驗證並顯示排班解；ValidateRules 逐條把解值代回 Model.md 的每一條限制式。</summary>
    public sealed class RosteringProblemSolution
    {
        private const double Tolerance = 1e-6;

        private readonly Dataload data;
        private readonly Dictionary<string, double> assign;

        private RosteringProblemSolution(Dataload data, Dictionary<string, double> assign)
        {
            this.data = data;
            this.assign = assign;
        }

        public static RosteringProblemSolution ReadAndValidate(OptEngine engine, Dataload data)
        {
            Logging.Info($"Status={engine.Status} Obj={engine.GetObjectiveValue():F4} " +
                         $"BestBound={engine.BestObjValue:F4} MIPGap={engine.MIPGap:P2}");

            var assign = engine.GetSetVarValues<VariableB_ShiftAssign>();
            var groupMismatch = engine.GetSetVarValues<VariableB_GroupMismatch>();
            var nightToDay = engine.GetSetVarValues<VariableB_NightToDay>();
            var doubleOffFlag = engine.GetSetVarValues<VariableB_DoubleOffFlag>();
            var doubleOffLT2 = engine.GetSetVarValues<VariableB_DoubleOffLT2>();
            var off1Day = engine.GetSetVarValues<VariableB_Off1Day>();
            var sixDayWork = engine.GetSetVarValues<VariableB_SixDayWork>();
            var belowAvg = engine.GetSetVarValues<VariableC_BelowAVG>();
            var weekendLT4 = engine.GetSetVarValues<VariableC_WeekendLT4>();

            ValidateRules(data, assign, groupMismatch, nightToDay, doubleOffFlag, doubleOffLT2, off1Day, sixDayWork, belowAvg, weekendLT4);

            FolderDir.Solution.CreateFolder();
            CsvCtrl.WriteSolution<VariableB_ShiftAssign>(engine, "RosteringProblem", "SYSTEM");

            Logging.Info("[RosteringProblem solution] 十條限制式全數驗證通過。");
            return new RosteringProblemSolution(data, assign);
        }

        private static double Assign(Dictionary<string, double> assign, DateTime date, string employee, string group)
            => assign.TryGetValue(new VariableB_ShiftAssign { Date = date, Employee = employee, Group = group }.ToString(), out var v) ? v : 0.0;

        /// <summary>逐條把解代回 Model.md 的限制式；不成立就丟例外，NEVER 只記 log 繼續。</summary>
        private static void ValidateRules(
            Dataload data,
            Dictionary<string, double> assign,
            Dictionary<string, double> groupMismatch,
            Dictionary<string, double> nightToDay,
            Dictionary<string, double> doubleOffFlag,
            Dictionary<string, double> doubleOffLT2,
            Dictionary<string, double> off1Day,
            Dictionary<string, double> sixDayWork,
            Dictionary<string, double> belowAvg,
            Dictionary<string, double> weekendLT4)
        {
            double one = data.parameter_One.Single().QTY;
            double sixDayWindow = data.parameter_SixDayWindow.Single().QTY;
            double nightToDayWindow = data.parameter_NightToDayWindow.Single().QTY;
            double offOneDayWindow = data.parameter_OffOneDayWindow.Single().QTY;
            double doubleOffWindow = data.parameter_DoubleOffWindow.Single().QTY;
            double doubleOffThreshold = data.parameter_DoubleOffThreshold.Single().QTY;
            double weekendOffThreshold = data.parameter_WeekendOffThreshold.Single().QTY;

            var dates = data.set_Date.Select(row => row.Date).ToList();
            var employees = data.set_Employee.Select(row => row.Employee).ToList();
            var shiftGroups = data.set_Group.Select(row => row.Group).ToList();

            // 1. FullfillDemand
            foreach (var date in dates)
                foreach (var group in shiftGroups)
                {
                    if (group == "O") continue;
                    double total = employees.Sum(e => Assign(assign, date, e, group));
                    double demand = data.parameter_ShiftDemand.FindParameterOrLog(
                        x => x.Date == date && x.Group == group,
                        date, group)?.QTY ?? 0.0;
                    if (Math.Abs(total - demand) > Tolerance)
                        throw new InvalidOperationException($"FullfillDemand 違反：{date:yyyy-MM-dd}/{group} 實際 {total}，需求 {demand}。");
                }

            // 2. OneGroup
            foreach (var date in dates)
                foreach (var employee in employees)
                {
                    double total = shiftGroups.Sum(g => Assign(assign, date, employee, g));
                    if (Math.Abs(total - one) > Tolerance)
                        throw new InvalidOperationException($"OneGroup 違反：{employee}@{date:yyyy-MM-dd} 共排入 {total} 個班別，預期恰好 1 個。");
                }

            // 3. PreAssign
            foreach (var p in data.parameter_PreAssign)
                if (Assign(assign, p.Date, p.Employee, p.Group) < one - Tolerance)
                    throw new InvalidOperationException($"PreAssign 違反：{p.Employee}@{p.Date:yyyy-MM-dd} 未依預排班指派為 {p.Group}。");

            // 4. CrossGroup
            foreach (var date in dates)
                foreach (var employee in employees)
                {
                    double mismatch = groupMismatch.TryGetValue(new VariableB_GroupMismatch { Date = date, Employee = employee }.ToString(), out var gm) ? gm : 0.0;
                    foreach (var rule in data.parameter_CrossGroup.Where(p => p.Employee == employee))
                        if (Assign(assign, date, employee, rule.Group) > mismatch + Tolerance)
                            throw new InvalidOperationException($"CrossGroup 違反：{employee}@{date:yyyy-MM-dd} 排入跨組別 {rule.Group}，但 GroupMismatch 未同步為 1。");
                }

            // 5. SixDayWork
            foreach (var date in dates)
                foreach (var employee in employees)
                {
                    var window = dates.Where(sd => date.AddDays(-sixDayWindow) < sd && sd <= date).ToList();
                    if (window.Count < sixDayWindow) continue;

                    double flag = sixDayWork.TryGetValue(new VariableB_SixDayWork { Date = date, Employee = employee }.ToString(), out var sw) ? sw : 0.0;
                    foreach (var sd in window)
                        if (flag > one - Assign(assign, sd, employee, "O") + Tolerance)
                            throw new InvalidOperationException($"SixDayWork 違反（上界）：{employee}@{date:yyyy-MM-dd}。");

                    double offSum = window.Sum(sd => Assign(assign, sd, employee, "O"));
                    if (flag < one - offSum - Tolerance)
                        throw new InvalidOperationException($"SixDayWork 違反（下界）：{employee}@{date:yyyy-MM-dd}。");
                }

            // 6. NightToDay
            foreach (var date in dates)
                foreach (var employee in employees)
                {
                    var window = dates.Where(sd => date.AddDays(-nightToDayWindow) < sd && sd <= date).ToList();
                    if (window.Count < nightToDayWindow) continue;

                    var preDate = date.AddDays(-1);
                    double flag = nightToDay.TryGetValue(new VariableB_NightToDay { Date = date, Employee = employee }.ToString(), out var nd) ? nd : 0.0;

                    foreach (var rule in data.parameter_NightToDay)
                    {
                        double rhs = Assign(assign, preDate, employee, rule.PreGroup) + Assign(assign, date, employee, rule.Group) - one;
                        if (flag < rhs - Tolerance)
                            throw new InvalidOperationException($"NightToDay 違反：{employee}@{date:yyyy-MM-dd}（規則 {rule.PreGroup}->{rule.Group}）。");
                    }
                }

            // 7. OffOneDay（做休做）
            foreach (var date in dates)
                foreach (var employee in employees)
                {
                    var window = dates.Where(sd => date.AddDays(-offOneDayWindow) < sd && sd <= date).ToList();
                    if (window.Count < offOneDayWindow) continue;

                    var preDate = date.AddDays(-1);
                    var prePreDate = date.AddDays(-2);
                    double flag = off1Day.TryGetValue(new VariableB_Off1Day { Date = date, Employee = employee }.ToString(), out var od) ? od : 0.0;

                    double rhs = one - Assign(assign, date, employee, "O") + Assign(assign, preDate, employee, "O")
                        + one - Assign(assign, prePreDate, employee, "O") - (offOneDayWindow - one);
                    if (flag < rhs - Tolerance)
                        throw new InvalidOperationException($"OffOneDay 違反：{employee}@{date:yyyy-MM-dd}。");
                }

            // 8. BelowAVG
            double allShift = employees.Count * dates.Count;
            double allDemand = data.parameter_ShiftDemand.Where(w => w.Group != "O").Sum(s => s.QTY);
            double avgOff = Math.Floor((allShift - allDemand) / employees.Count) - 1;
            foreach (var employee in employees)
            {
                double offCount = dates.Sum(d => Assign(assign, d, employee, "O"));
                double gap = belowAvg.TryGetValue(new VariableC_BelowAVG { Employee = employee }.ToString(), out var ba) ? ba : 0.0;
                if (offCount + gap < avgOff - Tolerance)
                    throw new InvalidOperationException($"BelowAVG 違反：{employee} off={offCount} gap={gap} avgOff={avgOff}。");
            }

            // 9. WeekendLT4
            var weekends = dates.Where(d => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday).ToList();
            foreach (var employee in employees)
            {
                double weekendOff = weekends.Sum(d => Assign(assign, d, employee, "O"));
                double gap = weekendLT4.TryGetValue(new VariableC_WeekendLT4 { Employee = employee }.ToString(), out var wl) ? wl : 0.0;
                if (gap + weekendOff < weekendOffThreshold - Tolerance)
                    throw new InvalidOperationException($"WeekendLT4 違反：{employee} weekendOff={weekendOff} gap={gap} threshold={weekendOffThreshold}。");
            }

            // 10. DoubleOffLT2
            foreach (var date in dates)
            {
                var window = dates.Where(sd => date.AddDays(-doubleOffWindow) < sd && sd <= date).ToList();
                if (window.Count < 2) continue;

                foreach (var employee in employees)
                {
                    double flag = doubleOffFlag.TryGetValue(new VariableB_DoubleOffFlag { Date = date, Employee = employee }.ToString(), out var df) ? df : 0.0;
                    double rhs;

                    if (window.Count == 2)
                    {
                        var preDate = date.AddDays(-1);
                        rhs = Assign(assign, date, employee, "O") + Assign(assign, preDate, employee, "O") - (window.Count - one);
                    }
                    else
                    {
                        var preDate = date.AddDays(-1);
                        var prePreDate = date.AddDays(-2);
                        rhs = Assign(assign, date, employee, "O") + Assign(assign, preDate, employee, "O")
                            + one - Assign(assign, prePreDate, employee, "O") - (window.Count - one);
                    }

                    if (flag < rhs - Tolerance)
                        throw new InvalidOperationException($"DoubleOffLT2（單日子式）違反：{employee}@{date:yyyy-MM-dd}。");
                }
            }

            foreach (var employee in employees)
            {
                double flagSum = dates.Sum(d => doubleOffFlag.TryGetValue(new VariableB_DoubleOffFlag { Date = d, Employee = employee }.ToString(), out var df) ? df : 0.0);
                double lt2 = doubleOffLT2.TryGetValue(new VariableB_DoubleOffLT2 { Employee = employee }.ToString(), out var l2) ? l2 : 0.0;
                if (flagSum + doubleOffThreshold * lt2 < doubleOffThreshold - Tolerance)
                    throw new InvalidOperationException($"DoubleOffLT2（彙總式）違反：{employee} flagSum={flagSum} lt2={lt2}。");
            }
        }

        /// <summary>依日期列印每位員工的班別（O = 休假）。</summary>
        public void Print()
        {
            var dates = data.set_Date.Select(row => row.Date).ToList();
            var employees = data.set_Employee.Select(row => row.Employee).ToList();
            var shiftGroups = data.set_Group.Select(row => row.Group).ToList();

            foreach (var employee in employees)
            {
                var groups = dates.Select(date =>
                    shiftGroups.FirstOrDefault(g => Assign(assign, date, employee, g) > 0.5) ?? "?");
                Console.WriteLine($"{employee}: {string.Join(" ", groups)}");
            }
        }
    }
}
