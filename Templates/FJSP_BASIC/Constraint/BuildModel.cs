using FJSP_BASIC.Data;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC.Constraint
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
            new ObjectiveFunction(_data.Scope, _engine).Build();

            Logging.Info("【建構限制式】");
            new Constraint_AssignOneEqp(_data.LotSet, _data.OperationSet, _data.EqpSet, _engine).Build();
            new Constraint_CompleteDef(_data.LotSet, _data.OperationSet, _data.EqpSet, _data.parameter_ProcessTime, _engine).Build();
            new Constraint_RoutePrecedence(_data.LotSet, _data.OperationSet, _engine).Build();
            new Constraint_NoOverlap(_data.LotSet, _data.OperationSet, _data.EqpSet, _data.BigM, _engine).Build();
            new Constraint_MakespanDef(_data.LotSet, _data.OperationSet, _data.Scope, _engine).Build();
            new Constraint_MakespanWindow(_data.Scope, _data.MakespanFloor, _data.MakespanDeadline, _engine).Build();
            new Constraint_MakespanTargetSoft(_data.Scope, _data.SoftMakespanTarget, _data.MakespanPenalty, _engine).Build();
        }
    }
}
