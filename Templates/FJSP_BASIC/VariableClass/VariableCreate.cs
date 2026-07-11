using FJSP_BASIC.Data;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC.VariableClass
{
    public class VariableCreate
    {
        private readonly OptEngine _engine;
        private readonly Dataload _dataload;

        public VariableCreate(Dataload dataload, OptEngine engine)
        {
            _dataload = dataload;
            _engine = engine;
        }

        public void Build()
        {
            _engine.BuildBVs<VariableB_Assign>(_dataload.Lot, _dataload.Operation, _dataload.Eqp);
            _engine.BuildCVs<VariableX_Start>(_dataload.Lot, _dataload.Operation);
            _engine.BuildCVs<VariableX_Complete>(_dataload.Lot, _dataload.Operation);
            _engine.BuildBVs<VariableB_Precede>(_dataload.Lot, _dataload.Operation, _dataload.Lot, _dataload.Operation);
            _engine.BuildCVs<VariableX_Makespan>(_dataload.Scope);

            Logging.Info($"Variables created: {_engine.varCount}");
        }
    }
}
