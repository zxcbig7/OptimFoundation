using System.Diagnostics;

using OptimFoundation.Cplex;
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

            CplexConfig config = new CplexConfig
            {
                epGap           = 0.03,
                timeLimit       = 100,
                workThreads     = 10,
                enableLog       = true,
                exportSol       = true,
                exportLP        = true,
                exportMPS       = true
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
