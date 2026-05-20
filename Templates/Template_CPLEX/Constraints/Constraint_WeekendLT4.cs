
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

namespace SandBox.Constraints
{
    public class Constraint_WeekendLT4 : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_WeekendLT4(Dataload dataload, OptEngine engine)
        {
            this.optEngine = engine;
            this.dataload = dataload;
        }

        /// <summary>
        /// </summary>
        public void Build()
        {
            try
            {

                dataload.Employee.ForEach(e =>
                {
                    optEngine.AddLHS(1, new VariableX_WeekendLT4 { Employee = e });

                    optEngine.AddRHS(4);
                    dataload.Date.Where(w => w.DayOfWeek == DayOfWeek.Saturday || w.DayOfWeek == DayOfWeek.Sunday)
                    .ToList().ForEach(d =>
                    {
                        optEngine.AddRHS(-1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "O" });
                    });

                    optEngine.CreateGreatEqual($"{ConstraintName}@{e}");
                    ConstraintCount++;

                    //optEngine.AddPool(1, new VariableC_WeekendLT4(e));
                    //optEngine.AddPoolRHS(0);
                    //optEngine.CreateGreatEqual($"{ConstraintName}@{e}");
                    //ConstraintCount++;
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
