using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class ObjectiveFunction
    {
        private readonly OptEngine _engine;
        private readonly List<DateTime> _dates;
        private readonly List<string> _employees;
        private readonly double _penaltyOffOneDay;
        private readonly double _penaltySixDay;
        private readonly double _penaltyGroupMismatch;
        private readonly double _penaltyNightToDay;
        private readonly double _penaltyDoubleOffLT2;
        private readonly double _penaltyBelowAVG;
        private readonly double _penaltyWeekend4Day;

        public ObjectiveFunction(
            List<DateTime> dates,
            List<string> employees,
            double penaltyOffOneDay,
            double penaltySixDay,
            double penaltyGroupMismatch,
            double penaltyNightToDay,
            double penaltyDoubleOffLT2,
            double penaltyBelowAVG,
            double penaltyWeekend4Day,
            OptEngine engine)
        {
            _dates = dates;
            _employees = employees;
            _penaltyOffOneDay = penaltyOffOneDay;
            _penaltySixDay = penaltySixDay;
            _penaltyGroupMismatch = penaltyGroupMismatch;
            _penaltyNightToDay = penaltyNightToDay;
            _penaltyDoubleOffLT2 = penaltyDoubleOffLT2;
            _penaltyBelowAVG = penaltyBelowAVG;
            _penaltyWeekend4Day = penaltyWeekend4Day;
            _engine = engine;
        }

        public void Build()
        {
            _dates.ForEach(d =>
            {
                _employees.ForEach(e =>
                {
                    _engine.AddLHS(_penaltyOffOneDay, new VariableB_Off1Day { Date = d, Employee = e });
                    _engine.AddLHS(_penaltySixDay, new VariableB_SixDayWork { Date = d, Employee = e });
                    _engine.AddLHS(_penaltyGroupMismatch, new VariableB_GroupMismatch { Date = d, Employee = e });
                    _engine.AddLHS(_penaltyNightToDay, new VariableB_NightToDay { Date = d, Employee = e });
                });
            });

            _employees.ForEach(e =>
            {
                _engine.AddLHS(_penaltyDoubleOffLT2, new VariableB_DoubleOffLT2 { Employee = e });
                _engine.AddLHS(_penaltyBelowAVG, new VariableX_BelowAVG { Employee = e });
                _engine.AddLHS(_penaltyWeekend4Day, new VariableX_WeekendLT4 { Employee = e });
            });

            _engine.CreateMinimize();
            Logging.Info("完成");
        }
    }
}
