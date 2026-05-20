
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
    public class Constraint_Template : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_Template(Dataload dataload, OptEngine engine)
        {
            this.optEngine = engine;
            this.dataload = dataload;
        }

        #region
        #endregion

        public void Build()
        {
            try
            {
                //dataload.Set1.ForEach(s1 =>
                //{
                //    dataload.Set2.ForEach(s2 =>
                //    {
                //        dataload.Set3.ForEach(s3 =>
                //        {
                //            dataload.Set4.ForEach(s4 =>
                //            {
                //                optEngine.AddLHS(1, new VariableX_Template(s1, s2, s3, s4));
                //            });
                //        });
                //    });
                //    optEngine.AddRHS(10);
                //    optEngine.CreateLessEqual($"{ConstraintName}@{s1}");
                //    ConstraintCount++;
                //});

                Logging.Info($"[{ConstraintName}] {ConstraintCount}");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
