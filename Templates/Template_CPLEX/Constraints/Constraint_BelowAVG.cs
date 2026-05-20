
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
                double totalEMP = dataload.Employee.Count;
                double AllShift = dataload.Employee.Count * dataload.Date.Count;
                double AllDemand = dataload.parameter_ShiftDemand.Where(w => w.Group != "O").Sum(s => s.QTY);
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

                Logging.Info($"[{ConstraintName}] {ConstraintCount}");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
