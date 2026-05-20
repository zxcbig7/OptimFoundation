using OptimFoundation.Gurobi;
using OptimFoundation.Core;

using SandBox.Data;
using SandBox.VariableClass;

namespace SandBox.VariablesClass
{
    public class VariableCreate
    {
        private OptEngine optEngine;
        private Dataload dataload;
        private int varCount { get { return optEngine.varCount; } }

        public VariableCreate(Dataload dataload, OptEngine engine)
        {
            optEngine = engine;
            this.dataload = dataload;
        }

        #region
        /* 
        ex: 
            

        */
        #endregion

        public void Build()
        {
            try
            {
                optEngine.BuildBVs<VariableB_ShiftAssign>(dataload.Date, dataload.Employee, dataload.Group);
                optEngine.BuildBVs<VariableB_GroupMismatch>(dataload.Date, dataload.Employee);
                optEngine.BuildBVs<VariableB_NightToDay>(dataload.Date, dataload.Employee);
                optEngine.BuildBVs<VariableB_DoubleOffFlag>(dataload.Date, dataload.Employee);
                optEngine.BuildBVs<VariableB_DoubleOffLT2>(dataload.Employee);
                optEngine.BuildBVs<VariableB_Off1Day>(dataload.Date, dataload.Employee);
                optEngine.BuildBVs<VariableB_SixDayWork>(dataload.Date, dataload.Employee);

                optEngine.BuildCVs<VariableX_BelowAVG>(dataload.Employee);
                optEngine.BuildCVs<VariableX_WeekendLT4>(dataload.Employee);

                Logging.Info($"Variables created: {varCount}");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
