using System.Diagnostics;

using OptimFoundation.Gurobi;
using OptimFoundation.Core;

using SandBox.Data;
using SandBox.Constraints;
using SandBox.VariablesClass;

namespace SandBox
{
    public class RosteringProblem : IDisposable
    {
        public OptEngine optEngine;
        public Dataload dataload;

        public Stopwatch buildModelTimer = new Stopwatch();
        public Stopwatch totalTimer = new Stopwatch();
        public TimeSpan totalTimeSpan = new TimeSpan();

        private bool _isSuccess;
        private string _projectName => GetType().Name;

        public RosteringProblem()
        {
            dataload = new Dataload();
            _isSuccess = false;
            Logging.SetLogFileName(_projectName);
        }

        public bool Execute()
        {
            totalTimer.Restart();

            GurobiConfig config = new GurobiConfig
            {
                MipGap       = 0.03,
                TimeLimit    = 100,
                Threads      = 10,
                LogToConsole = true,
                ExportSol    = true,
                ExportLp     = true,
                ExportMps    = true
            };

            optEngine = new OptEngine(config);
            optEngine.Build();

            buildModelTimer.Restart();

            new VariableCreate(dataload, optEngine).Build();
            Logging.Info("【建構變數完成】", buildModelTimer);

            new BuildModel(dataload, optEngine).Build();
            Logging.Info("【建構模型完成】", buildModelTimer);

            buildModelTimer.Stop();

            _isSuccess = optEngine.Solve();

            if (_isSuccess)
                dataload.WriteToCSV(optEngine);

            totalTimeSpan = totalTimer.Elapsed;
            totalTimer.Stop();
            return _isSuccess;
        }

        public void Dispose()
        {
            optEngine?.Dispose();
        }
    }
}
