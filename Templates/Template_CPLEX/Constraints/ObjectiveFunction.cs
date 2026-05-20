using OptimFoundation.Cplex;
using OptimFoundation.Core;
using SandBox.Data;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class ObjectiveFunction
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public ObjectiveFunction(Dataload dataload, OptEngine engine)
        {
            this.optEngine = engine;
            this.dataload = dataload;
        }

        #region 程式範例
        /* 
        ex: 
            this.optEngine.addPool([係數], VariableDeclare.ReadVar(new <變數物件>));  //加入LHS
            this.optEngine.addPool(1, VariableDeclare.ReadVar(new VariableX_Input(machine,product,period)));
        
        
            this.optEngine.createMinimize(); // 建立最小化目標式
            this.optEngine.createMaximize(); // 建立最大化目標式
        */
        #endregion

        public void Build()
        {
            try
            {
                #region 建構Expr (用 DataLoad 跌代)
                dataload.Date.ForEach(d =>
                {
                    dataload.Employee.ForEach(e =>
                    {
                        optEngine.AddLHS(dataload.Penalty_OffOneDay, new VariableB_Off1Day { Date = d, Employee = e }); // 單日休息
                        optEngine.AddLHS(dataload.Penalty_SixDay, new VariableB_SixDayWork { Date = d, Employee = e }); // 連續上班六天
                        optEngine.AddLHS(dataload.Penalty_GroupMismatch, new VariableB_GroupMismatch { Date = d, Employee = e }); // 跨組別上班(非Group、Backup)
                        optEngine.AddLHS(dataload.Penalty_NightToDay, new VariableB_NightToDay { Date = d, Employee = e }); // 兩天班別偏好

                    });
                });

                
                dataload.Employee.ForEach(e =>
                {
                    optEngine.AddLHS(dataload.Penalty_DoubleOffLT2, new VariableB_DoubleOffLT2 { Employee = e }); // 一個月內兩次雙休
                    optEngine.AddLHS(dataload.Penalty_BelowAVG, new VariableX_BelowAVG { Employee = e }); // 工作量低於整體平均
                    optEngine.AddLHS(dataload.Penalty_Weekend4Day, new VariableX_WeekendLT4 { Employee = e }); // 一個月內週末休息少於4天
                });
                #endregion

                this.optEngine.CreateMinimize();
                Logging.Info($"目標式建立完成");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
