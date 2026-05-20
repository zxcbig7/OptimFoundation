
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
        /// 連續工作六天
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
                        // 往前6天的日期加總看是否有連續上班6天的情況，第6天懲處
                        // 如果連續六天有任一天是O
                        var dates = dataload.Date.Where(sd => d.AddDays(-duration) < sd && sd <= d).ToList();

                        if (dates.Count < duration) return; // 不足六天，不處理

                        // 上限: 如果連續六天有任一天是O，SixDayWork就一定是0
                        dates.ForEach(sd =>
                        {
                            optEngine.AddLHS(1, new VariableB_SixDayWork { Date = d, Employee = e });
                            optEngine.AddRHS(1);
                            optEngine.AddRHS(-1, new VariableB_ShiftAssign { Date = sd, Employee = e, Group = "O" });
                            optEngine.CreateLessEqual($"{ConstraintName}@{d.ToString("yyyy_MM_dd")}@{e}");
                            ConstraintCount++;
                        });

                        // 下限: 如果連續六天任一不是O，SixDayWork就一定是1
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

                Logging.Info($"{ConstraintName} ，共：{ConstraintCount}條");
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
