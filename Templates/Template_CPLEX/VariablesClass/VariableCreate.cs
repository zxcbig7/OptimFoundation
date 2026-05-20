using OptimFoundation.Cplex;
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

        #region 程式範例
        /* 
        ex: 
            // 設立變數
            optEngine.BuildBVs<物件>(維度1, 維度2, ...);
            optEngine.BuildIVs<物件>(維度1, 維度2, ...);
            optEngine.BuildCVs<物件>(維度1, 維度2, ...);
            
            // 設立變數同時設立界限
            optEngine.BuildIVs<物件>(LB, UB, 維度1, 維度2, ...);
            optEngine.BuildCVs<物件>(LB, UB, 維度1, 維度2, ...);

            // 設立界限
            optEngine.SetVarRange(變數, LB, UB);
        */
        #endregion

        public void Build()
        {
            try
            {
                // 建立變數 Pools
                optEngine.BuildBVs<VariableB_ShiftAssign>(dataload.Date, dataload.Employee, dataload.Group);
                optEngine.BuildBVs<VariableB_GroupMismatch>(dataload.Date, dataload.Employee);
                optEngine.BuildBVs<VariableB_NightToDay>(dataload.Date, dataload.Employee);
                optEngine.BuildBVs<VariableB_DoubleOffFlag>(dataload.Date, dataload.Employee);
                optEngine.BuildBVs<VariableB_DoubleOffLT2>(dataload.Employee);
                optEngine.BuildBVs<VariableB_Off1Day>(dataload.Date, dataload.Employee);
                optEngine.BuildBVs<VariableB_SixDayWork>(dataload.Date, dataload.Employee);

                //休0天扣4倍、超過4天不扣分
                optEngine.BuildCVs<VariableX_BelowAVG>(dataload.Employee);
                optEngine.BuildCVs<VariableX_WeekendLT4>(dataload.Employee);

                Logging.Info($"【建立變數】 共建立 {varCount} 個變數");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
