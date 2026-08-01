using FJSP_BASIC_BRICK.Data;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace FJSP_BASIC_BRICK.VariableClass
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
            // BuildVars：型別由類別名前綴決定（B_/X_/I_）；直接傳 Set 積木（SetBase 是 IEnumerable，零轉換）
            _engine.BuildVars<VariableB_Assign>(_dataload.LOT, _dataload.OPERATION, _dataload.EQP);
            _engine.BuildVars<VariableX_Start>(_dataload.LOT, _dataload.OPERATION);
            _engine.BuildVars<VariableX_Complete>(_dataload.LOT, _dataload.OPERATION);
            // Precede：同 set 多維度，LOT/OPERATION 各傳兩次（對應 OptDim LotA/OperationA/LotB/OperationB）
            _engine.BuildVars<VariableB_Precede>(_dataload.LOT, _dataload.OPERATION, _dataload.LOT, _dataload.OPERATION);
            _engine.BuildVars<VariableX_Makespan>();   // scalar：0 維，無 set

        }
    }
}
