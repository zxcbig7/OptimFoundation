using System;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// 正式求解：只允許一組模型與設定，engine 保留給專案取解；預設輸出 solver log、不記錄收斂軌跡，紀錄不寫 -summary.csv。
    /// 由 <c>project.Production()</c> 建立。
    /// </summary>
    public sealed class OptProduction : OptExecution
    {
        private readonly ProductionResult _result;

        internal OptProduction(string projectName, string name, string description, ProductionResult result)
            : base(projectName, name, description, logName: projectName,
                projectConfig: new ProjectConfig(), captureTrajectory: false)
        {
            _result = result;
        }

        private protected override string FailureEventName => "求解執行失敗";

        private protected override Experiment RunCore()
        {
            // 一進來就清掉上一次的結果，設定不合法丟例外時才不會留下舊值。
            _result.IsSuccess = false;
            _result.Trial = null;

            var cells = ExpandTrials();
            // 正式環境只跑一組：多組會讓 project.Engine / Trial 不知道該留哪一組，比較多組請用 Experiment。
            if (cells.Count > 1)
                throw Logging.ErrorOnce(
                    new InvalidOperationException($"正式環境只能有一組模型與設定，目前有 {cells.Count} 組；比較多組請改用 project.Experiment(name)"),
                    "求解設定不合法", null, nameof(Run), Name, "超過一組模型與設定", $"組數={cells.Count}");
            var cell = cells[0];

            var record = StartRecord("正式環境開始", $"正式環境紀錄：{cell.Model.Name}", writeSummary: false,
                trialCount: 1, multiModel: false);
            string runId = NextRunId();

            var engine = CreateEngine(cell.Config);
            // 先保存 engine 再建模，失敗時仍可讀取診斷資訊；前一次的 engine 在這裡釋放。
            _result.Engine?.Dispose();
            _result.Engine = engine;
            try
            {
                _result.Trial = RunTrial(engine, cell, ProjectName, record, runId, out bool solved);
                _result.IsSuccess = solved;
                if (solved) InvokeOnSolved(engine);
            }
            catch
            {
                SaveCompleted(record, 1);
                throw;
            }

            record.Save();
            _result.TotalElapsed = engine.ElapsedSinceRunStart;
            return record;
        }
    }

    /// <summary>最近一次正式求解的結果；OptProject 持有一份、跨多次 Production 共用，OptProduction 執行時寫入。</summary>
    internal sealed class ProductionResult
    {
        public OptEngine Engine { get; set; }
        public bool IsSuccess { get; set; }
        public Trial Trial { get; set; }
        public TimeSpan TotalElapsed { get; set; }
    }
}
