
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
    public class Constraint_FullfillDemand : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_FullfillDemand(Dataload dataload, OptEngine engine)
        {
            this.optEngine = engine;
            this.dataload = dataload;
        }

        public void Build()
        {
            try
            {
                dataload.Date.ForEach(d =>
                {
                    dataload.Group.Where(w=>w!="O").ToList().ForEach(g =>
                    {
                        dataload.Employee.ForEach(e =>
                        {
                            optEngine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = g });
                        });

                        double demand = dataload.parameter_ShiftDemand.FirstOrDefault(x => x.Date == d && x.Group == g)?.QTY ?? 0;
                        optEngine.AddRHS(demand);
                        optEngine.CreateEqual($"{ConstraintName}@{d.ToString("yyyy_MM_dd")}@{g}");
                        ConstraintCount++;
                    });
                });

                Logging.Info($"{ConstraintName} ¡A¦@¡G{ConstraintCount}±ø");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
