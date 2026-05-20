
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

using OptimFoundation.Core;

using OptimFoundation.Gurobi;
using SandBox.Data;
using SandBox.VariableClass;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SandBox.Constraints
{
    public class Constraint_SixDayWork : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_SixDayWork(Dataload dataload, OptEngine engine)
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
                int duration = 6;
                dataload.Date.ForEach(d =>
                {
                    dataload.Employee.ForEach(e =>
                    {
                        var dates = dataload.Date.Where(sd => d.AddDays(-duration) < sd && sd <= d).ToList();

                        if (dates.Count < duration) return;

                        dates.ForEach(sd =>
                        {
                            optEngine.AddLHS(1, new VariableB_SixDayWork { Date = d, Employee = e });
                            optEngine.AddRHS(1);
                            optEngine.AddRHS(-1, new VariableB_ShiftAssign { Date = sd, Employee = e, Group = "O" });
                            optEngine.CreateLessEqual($"{ConstraintName}@{d.ToString("yyyy_MM_dd")}@{e}");
                            ConstraintCount++;
                        });

                        optEngine.AddLHS(1, new VariableB_SixDayWork { Date = d, Employee = e });
                        optEngine.AddRHS(1);
                        dates.ForEach(sd =>
                        {
                            optEngine.AddRHS(-1, new VariableB_ShiftAssign { Date = sd, Employee = e, Group = "O" });
                        });
                        optEngine.CreateGreatEqual($"{ConstraintName}@{d.ToString("yyyy_MM_dd")}@{e}");
                        ConstraintCount++;
                    });
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
