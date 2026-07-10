using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

using OptimFoundation.Gurobi;
using OptimFoundation.Core;
using SandBox.Data;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class BuildModel
    {
        private OptEngine engine;
        private Dataload dataload;

        public BuildModel(Dataload dataload, OptEngine engine)
        {
            this.engine = engine;
            this.dataload = dataload;
        }

        public void Build()
        {
            try
            {
                Logging.Info($"【建構目標式】");
                new ObjectiveFunction(dataload, engine).Build();

                Logging.Info($"【建構限制式】");
                // 基本限制式
                new Constraint_FullfillDemand(dataload, engine).Build();
                new Constraint_OneGroup(dataload, engine).Build();
                new Constraint_PreAssign(dataload, engine).Build();

                // 進階限制式
                new Constraint_SixDayWork(dataload, engine).Build();
                new Constraint_NightToDay(dataload, engine).Build();
                new Constraint_OffOneDay(dataload, engine).Build();
                new Constraint_CrossGroup(dataload, engine).Build();
                new Constraint_BelowAVG(dataload, engine).Build();
                new Constraint_WeekendLT4(dataload, engine).Build();
                new Constraint_DoubleOffLT2(dataload, engine).Build();

            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
