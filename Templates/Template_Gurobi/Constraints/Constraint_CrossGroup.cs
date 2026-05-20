
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
    public class Constraint_CrossGroup : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_CrossGroup(Dataload dataload, OptEngine engine)
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
                    dataload.Employee.ForEach(e =>
                    {
                        // 取得員工 e 違反組別的資料
                        var inhi = dataload.parameter_CrossGroup.Where(p => p.Employee == e).ToList();

                        foreach (var g in inhi)
                        {
                            optEngine.AddLHS(1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = g.Group });
                            optEngine.AddRHS(1, new VariableB_GroupMismatch { Date = d, Employee = e });
                            optEngine.CreateLessEqual($"{ConstraintName}@{d.ToString("yyyy_MM_dd")}@{e}s");
                            ConstraintCount++;
                        }
                    });
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
