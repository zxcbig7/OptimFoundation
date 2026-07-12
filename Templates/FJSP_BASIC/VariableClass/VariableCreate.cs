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
            // BuildVars：變數型別由類別名前綴決定（命名天條 B_/X_/I_）；顯式指定型別的寫法見 Program.BuildModelB
            _engine.BuildVars<VariableB_Assign>(_dataload.LotSet, _dataload.OperationSet, _dataload.EqpSet);
            _engine.BuildVars<VariableX_Start>(_dataload.LotSet, _dataload.OperationSet);
            _engine.BuildVars<VariableX_Complete>(_dataload.LotSet, _dataload.OperationSet);
            _engine.BuildVars<VariableB_Precede>(_dataload.LotSet, _dataload.OperationSet, _dataload.LotSet, _dataload.OperationSet);
            _engine.BuildVars<VariableX_Makespan>(_dataload.Scope);

            Logging.Info($"Variables created: {_engine.varCount}");
        }
    }
}
