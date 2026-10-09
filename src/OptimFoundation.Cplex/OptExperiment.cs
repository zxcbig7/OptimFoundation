using System;
using System.Linq;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// 實驗：每個模型搭配每組求解器設定（m×n，另可 AddTrial），每組跑完就釋放 engine；
    /// 預設關閉 solver log 與匯出、記錄收斂軌跡，紀錄寫成 {專案}-{實驗}- 四個檔。由 <c>project.Experiment(name)</c> 建立。
    /// </summary>
    public sealed class OptExperiment : OptExecution
    {
        internal OptExperiment(string projectName, string name, string description)
            : base(projectName, name, description, logName: $"{projectName}-{name}_exp",
                projectConfig: ProjectConfig.Quiet(), captureTrajectory: true)
        {
        }

        private protected override string FailureEventName => "實驗執行失敗";

        private protected override Experiment RunCore()
        {
            var cells = ExpandTrials();
            // 多模型時加入模型名，避免輸出互相覆寫。
            bool multiModel = cells.Select(c => c.Model.Name).Distinct(StringComparer.Ordinal).Count() > 1;

            var record = StartRecord("實驗開始", string.Empty, writeSummary: true, cells.Count, multiModel);
            string runId = NextRunId();

            try
            {
                foreach (var cell in cells)
                {
                    string runName = multiModel
                        ? $"{ProjectName}-{cell.Model.Name}-{cell.Label}"
                        : $"{ProjectName}-{cell.Label}";
                    using var engine = CreateEngine(cell.Config);
                    RunTrial(engine, cell, runName, record, runId, out bool solved);
                    if (solved) InvokeOnSolved(engine);
                }
            }
            catch
            {
                SaveCompleted(record, cells.Count);
                throw;
            }

            record.Save();
            return record;
        }
    }
}
