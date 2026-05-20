using OptimFoundation.Gurobi;
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

        #region
        /*
        ex:
            this.optEngine.addPool(1, VariableDeclare.ReadVar(new VariableX_Input(machine,product,period)));

        */
        #endregion

        public void Build()
        {
            try
            {
                #region
                dataload.Date.ForEach(d =>
                {
                    dataload.Employee.ForEach(e =>
                    {
                        optEngine.AddLHS(dataload.Penalty_OffOneDay, new VariableB_Off1Day { Date = d, Employee = e });
                        optEngine.AddLHS(dataload.Penalty_SixDay, new VariableB_SixDayWork { Date = d, Employee = e });
                        optEngine.AddLHS(dataload.Penalty_GroupMismatch, new VariableB_GroupMismatch { Date = d, Employee = e });
                        optEngine.AddLHS(dataload.Penalty_NightToDay, new VariableB_NightToDay { Date = d, Employee = e });

                    });
                });

                dataload.Employee.ForEach(e =>
                {
                    optEngine.AddLHS(dataload.Penalty_DoubleOffLT2, new VariableB_DoubleOffLT2 { Employee = e });
                    optEngine.AddLHS(dataload.Penalty_BelowAVG, new VariableX_BelowAVG { Employee = e });
                    optEngine.AddLHS(dataload.Penalty_Weekend4Day, new VariableX_WeekendLT4 { Employee = e });
                });
                #endregion

                this.optEngine.CreateMinimize();
                Logging.Info("摰?");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
