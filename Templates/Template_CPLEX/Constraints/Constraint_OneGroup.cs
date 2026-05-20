
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
    public class Constraint_OneGroup : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_OneGroup(Dataload dataload, OptEngine engine)
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
                dataload.Date.ForEach(d =>
                {
                    dataload.Employee.ForEach(e =>
                    {
                        dataload.Group.ForEach(g =>
                        {
                            optEngine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = g });
                        });
                        optEngine.AddRHS(1); 
                        optEngine.CreateEqual($"{ConstraintName}@{d.ToString("yyyy_MM_dd")}@{e}");
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
