using OptimFoundation.Cplex;

namespace RosteringProblem
{
    /// <summary>min Σ_date Σ_employee (OffOneDayPenalty·Off1Day + SixDayPenalty·SixDayWork
    /// + GroupMismatchPenalty·GroupMismatch + NightToDayPenalty·NightToDay)
    /// + Σ_employee (DoubleOffLT2Penalty·DoubleOffLT2 + BelowAVGPenalty·BelowAVG + Weekend4DayPenalty·WeekendLT4)</summary>
    public sealed class ObjectiveFunction
    {
        private readonly Set_Date dates;
        private readonly Set_Employee employees;
        private readonly double offOneDayPenalty;
        private readonly double sixDayPenalty;
        private readonly double groupMismatchPenalty;
        private readonly double nightToDayPenalty;
        private readonly double doubleOffLT2Penalty;
        private readonly double belowAvgPenalty;
        private readonly double weekend4DayPenalty;

        public ObjectiveFunction(
            Set_Date dates,
            Set_Employee employees,
            double offOneDayPenalty,
            double sixDayPenalty,
            double groupMismatchPenalty,
            double nightToDayPenalty,
            double doubleOffLT2Penalty,
            double belowAvgPenalty,
            double weekend4DayPenalty)
        {
            this.dates = dates;
            this.employees = employees;
            this.offOneDayPenalty = offOneDayPenalty;
            this.sixDayPenalty = sixDayPenalty;
            this.groupMismatchPenalty = groupMismatchPenalty;
            this.nightToDayPenalty = nightToDayPenalty;
            this.doubleOffLT2Penalty = doubleOffLT2Penalty;
            this.belowAvgPenalty = belowAvgPenalty;
            this.weekend4DayPenalty = weekend4DayPenalty;
        }

        public void Build(OptEngine engine)
        {
            foreach (var date in dates)
                foreach (var employee in employees)
                {
                    engine.AddLHS(offOneDayPenalty, new VariableB_Off1Day { Date = date, Employee = employee });
                    engine.AddLHS(sixDayPenalty, new VariableB_SixDayWork { Date = date, Employee = employee });
                    engine.AddLHS(groupMismatchPenalty, new VariableB_GroupMismatch { Date = date, Employee = employee });
                    engine.AddLHS(nightToDayPenalty, new VariableB_NightToDay { Date = date, Employee = employee });
                }

            foreach (var employee in employees)
            {
                engine.AddLHS(doubleOffLT2Penalty, new VariableB_DoubleOffLT2 { Employee = employee });
                engine.AddLHS(belowAvgPenalty, new VariableX_BelowAVG { Employee = employee });
                engine.AddLHS(weekend4DayPenalty, new VariableX_WeekendLT4 { Employee = employee });
            }

            engine.CreateMinimize();
        }
    }
}
