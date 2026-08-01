using FJSP_BASIC_BRICK.Data;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK.Constraint
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
            new ObjectiveFunction(_engine).Build();

            new Constraint_AssignOneEqp(_data.LOT, _data.OPERATION, _data.EQP, _engine).Build();
            new Constraint_CompleteDef(_data.LOT, _data.OPERATION, _data.EQP, _data.parameter_ProcessTime, _engine).Build();
            new Constraint_RoutePrecedence(_data.LOT, _data.OPERATION, _engine).Build();
            new Constraint_NoOverlap(_data.LOT, _data.OPERATION, _data.EQP, _data.BigM, _engine).Build();
            new Constraint_MakespanDef(_data.LOT, _data.OPERATION, _engine).Build();
            new Constraint_MakespanWindow(_data.MakespanFloor, _data.MakespanDeadline, _engine).Build();
            new Constraint_MakespanTargetSoft(_data.SoftMakespanTarget, _data.MakespanPenalty, _engine).Build();
        }
    }
}
