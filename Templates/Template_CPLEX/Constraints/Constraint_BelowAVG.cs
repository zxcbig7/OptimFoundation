
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

using OptimFoundation.Core;
using OptimFoundation.Cplex;
using SandBox.Data;
using SandBox.VariableClass;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Data;

namespace SandBox.Constraints
{
    public class Constraint_BelowAVG : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_BelowAVG(Dataload dataload, OptEngine engine)
        {
            this.optEngine = engine;
            this.dataload = dataload;
        }

        public void Build()
        {
            try
            {
                double totalEMP = dataload.Employee.Count; // 人員總數
                double AllShift = dataload.Employee.Count * dataload.Date.Count; // 每個人每天上班總數
                double AllDemand = dataload.parameter_ShiftDemand.Where(w => w.Group != "O").Sum(s => s.QTY); // 總工作量
                double AVGOFF = Math.Floor((AllShift - AllDemand) / totalEMP) - 1;


                dataload.Employee.ForEach(e =>
                {
                    dataload.Date.ForEach(d =>
                    {
                        optEngine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "O" });
                    });
                    optEngine.AddLHS(1, new VariableX_BelowAVG { Employee = e });

                    optEngine.AddRHS(AVGOFF);

                    optEngine.CreateGreatEqual($"{ConstraintName}@{e}");
                    ConstraintCount++;
                });

                Logging.Info($"{ConstraintName} ，共：{ConstraintCount}條");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
