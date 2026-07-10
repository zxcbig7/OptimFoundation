using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.Data;

namespace SandBox.Constraints
{
    public class BuildModel
    {
        private readonly OptEngine _engine;
        private readonly Dataload _data;

        public BuildModel(Dataload data, OptEngine engine)
        {
            _data = data;
            _engine = engine;
        }

        public void Build()
        {
            Logging.Info("【建構目標式】");
            new ObjectiveFunction(
                _data.Date, _data.Employee,
                _data.Penalty_OffOneDay,
                _data.Penalty_SixDay,
                _data.Penalty_GroupMismatch,
                _data.Penalty_NightToDay,
                _data.Penalty_DoubleOffLT2,
                _data.Penalty_BelowAVG,
                _data.Penalty_Weekend4Day,
                _engine).Build();

            Logging.Info("【建構限制式】");
            new Constraint_FullfillDemand(_data.Date, _data.Employee, _data.Group, _data.parameter_ShiftDemand, _engine).Build();
            new Constraint_OneGroup(_data.Date, _data.Employee, _data.Group, _engine).Build();
            new Constraint_PreAssign(_data.parameter_PreAssign, _engine).Build();
            new Constraint_SixDayWork(_data.Date, _data.Employee, _engine).Build();
            new Constraint_NightToDay(_data.Date, _data.Employee, _data.parameter_NightToDay, _engine).Build();
            new Constraint_OffOneDay(_data.Date, _data.Employee, _engine).Build();
            new Constraint_CrossGroup(_data.Date, _data.Employee, _data.parameter_CrossGroup, _engine).Build();
            new Constraint_BelowAVG(_data.Date, _data.Employee, _data.parameter_ShiftDemand, _engine).Build();
            new Constraint_WeekendLT4(_data.Date, _data.Employee, _engine).Build();
            new Constraint_DoubleOffLT2(_data.Date, _data.Employee, _engine).Build();
        }
    }
}
