using System.Diagnostics;

using OptimFoundation.Solver;
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

            SolverConfig config = new SolverConfig
            {
                MipGap       = 0.03,
                TimeLimit    = 100,
                Threads      = 10,
                LogToConsole = true,
                exportSol    = true,
                exportLP     = true,
                exportMPS    = true
            };

            optEngine = new OptEngine(config);
            optEngine.SetModelName(_projectName);
            optEngine.Build();

            buildModelTimer.Restart();

            new VariableCreate(dataload, optEngine).Build();
            Logging.Info("Build variables complete", buildModelTimer);

            new BuildModel(dataload, optEngine).Build();
            Logging.Info("Build model complete", buildModelTimer);

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
