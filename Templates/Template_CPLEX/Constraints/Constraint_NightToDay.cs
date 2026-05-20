
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

namespace SandBox.Constraints
{
    public class Constraint_NightToDay : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_NightToDay(Dataload dataload, OptEngine engine)
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
                int duration = 2;
                dataload.Date.ForEach(d =>
                {
                    dataload.Employee.ForEach(e =>
                    {
                        var dates = dataload.Date.Where(sd => d.AddDays(-duration) < sd && sd <= d).ToList();

                        if (dates.Count < duration) return;

                        var preD = d.AddDays(-1);

                        dataload.parameter_NightToDay.ToList().ForEach(rule =>
                        {
                            optEngine.AddLHS(1, new VariableB_NightToDay { Date = d, Employee = e });
                            optEngine.AddRHS(1, new VariableB_ShiftAssign { Date = preD, Employee = e, Group = rule.PreGroup });
                            optEngine.AddRHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = rule.Group });
                            optEngine.AddRHS(-1);
                            optEngine.CreateGreatEqual($"{ConstraintName}@{d.ToString("yyyy_MM_dd")}@{e}");
                            ConstraintCount++;
                        });
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
