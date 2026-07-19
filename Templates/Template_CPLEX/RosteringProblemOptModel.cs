using OptimFoundation.Core;
using OptimFoundation.Cplex;

using SandBox.Data;
using SandBox.Constraints;
using SandBox.VariablesClass;

namespace SandBox
{
    /// <summary>
    /// RosteringProblem 的 OptModel（Fluent 管線）版本。
    /// 與手寫 RosteringProblem 同一組 config / 變數 / 模型 / 輸出，改以 OptModel 註冊式管線組裝，
    /// 供「手寫 orchestration」與「Fluent 管線」兩種建模風格對照。
    /// 用法：using (var m = RosteringProblemOptModel.Build()) m.Execute();
    /// </summary>
    public static class RosteringProblemOptModel
    {
        public static OptModel Build()
        {
            var dataload = OptData.Load(() => new Dataload());

            return new OptModel("RosteringProblem")
                .UseConfig(() => new CplexConfig
                {
                    epGap = 0.03,
                    timeLimit = 100,
                    workThreads = 10,
                    enableLog = true,
                    exportSol = true,
                    exportLP = true,
                    exportMPS = true
                })
                .AddVariables(e => new VariableCreate(dataload, e).Build())
                .AddModel(e => new BuildModel(dataload, e).Build())
                .OnSolved(e => dataload.WriteToCSV(e));
        }
    }
}
