using OptimFoundation.Core;
using OptimFoundation.Gurobi;
using SandBox.Data;
using SandBox.VariableClass;

namespace SandBox.Constraints
{
    public class Constraint_OffOneDay : ConstraintBase
    {
        private OptEngine optEngine;
        private Dataload dataload;

        public Constraint_OffOneDay(Dataload dataload, OptEngine engine)
        {
            this.optEngine = engine;
            this.dataload = dataload;
        }

        /// <summary>
        /// 做休做
        /// </summary>
        public void Build()
        {
            try
            {
                int duration = 3;

                dataload.Date.ForEach(d =>
                {
                    dataload.Employee.ForEach(e =>
                    {
                        var dates = dataload.Date.Where(sd => d.AddDays(-duration) < sd && sd <= d).ToList();

                        if (dates.Count < duration) return; // 不足3天，不處理

                        var preD = d.AddDays(-1); // 昨天
                        var prepreD = d.AddDays(-2); // 前天

                        optEngine.AddLHS(1, new VariableB_Off1Day { Date = d, Employee = e });

                        optEngine.AddRHS(1);
                        optEngine.AddRHS(-1, new VariableB_ShiftAssign { Date = d, Employee = e, Group = "O" });

                        optEngine.AddRHS(1, new VariableB_ShiftAssign { Date = preD, Employee = e, Group = "O" });

                        optEngine.AddRHS(1);
                        optEngine.AddRHS(-1, new VariableB_ShiftAssign { Date = prepreD, Employee = e, Group = "O" });

                        optEngine.AddRHS(-(duration - 1));

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
