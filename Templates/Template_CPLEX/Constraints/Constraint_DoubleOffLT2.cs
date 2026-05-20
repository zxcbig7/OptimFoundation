
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
    public class Constraint_DoubleOffLT2 : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_DoubleOffLT2(Dataload dataload, OptEngine engine)
        {
            this.optEngine = engine;
            this.dataload = dataload;
        }

        public void Build()
        {
            try
            {
                int duration = 3;

                dataload.Date.ForEach(d =>
                {
                    var dates = dataload.Date.Where(sd => d.AddDays(-duration) < sd && sd <= d).ToList();
                    if (dates.Count < 2) return;

                    dataload.Employee.ForEach(e =>
                    {
                        if (dates.Count == 2)
                        {
                            var preD = d.AddDays(-1);

                            optEngine.AddLHS(1, new VariableB_DoubleOffFlag { Date = d, Employee = e });

                            optEngine.AddRHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "O" });
                            optEngine.AddRHS(1, new VariableB_ShiftAssign { Date = preD, Employee = e, Group = "O" });
                            optEngine.AddRHS(-(2 - 1));

                        }
                        else if (dates.Count == 3)
                        {
                            var preD = d.AddDays(-1);
                            var prepreD = d.AddDays(-2);
                            optEngine.AddLHS(1, new VariableB_DoubleOffFlag { Date = d, Employee = e });

                            optEngine.AddRHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "O" });
                            optEngine.AddRHS(1, new VariableB_ShiftAssign { Date = preD, Employee = e, Group = "O" });

                            optEngine.AddRHS(1);
                            optEngine.AddRHS(-1, new VariableB_ShiftAssign { Date = prepreD, Employee = e, Group = "O" });

                            optEngine.AddRHS(-(3 - 1));
                        }

                        optEngine.CreateGreatEqual($"{ConstraintName}_a@{d.ToString("yyyy_MM_dd")}@{e}");
                        ConstraintCount++;
                    });


                });

                dataload.Employee.ForEach(e =>
                {
                    dataload.Date.ForEach(d =>
                    {
                        optEngine.AddLHS(1, new VariableB_DoubleOffFlag { Date = d, Employee = e });
                    });
                    optEngine.AddLHS(2, new VariableB_DoubleOffLT2 { Employee = e });
                    optEngine.AddRHS(2);
                    optEngine.CreateGreatEqual($"{ConstraintName}_b@{e}");
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
