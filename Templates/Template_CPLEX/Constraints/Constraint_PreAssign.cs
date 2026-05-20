
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
    public class Constraint_PreAssign : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_PreAssign(Dataload dataload, OptEngine engine)
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

                dataload.parameter_PreAssign.ToList().ForEach(p =>
                {
                    optEngine.AddLHS(1, new VariableB_ShiftAssign { Date = p.Date, Employee = p.Employee, Group = p.Group });
                    optEngine.AddRHS(1);
                    optEngine.CreateEqual($"{ConstraintName}@{p.Date.ToString("yyyy_MM_dd")}@{p.Employee}@{p.Group}");
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
