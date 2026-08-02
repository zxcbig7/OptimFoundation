using System;
using System.Collections.Generic;
using System.Diagnostics;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>Runs one <see cref="OptModel"/> with one solver configuration.</summary>
    public sealed class OptProject : IDisposable
    {
        private readonly OptModel _model;
        private readonly string _projectName;
        private readonly int _retentionDays;
        private readonly List<Action<OptEngine>> _solvedHandlers = new List<Action<OptEngine>>();
        private Func<CplexConfig> _configFactory = () => new CplexConfig();
        private Func<ProjectConfig> _projectConfigFactory = () => new ProjectConfig();
        private bool _isSuccess;

        /// <summary>Creates a runner for a single model.</summary>
        public OptProject(OptModel model, string projectName = null, int retentionDays = 30)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _projectName = projectName;
            _retentionDays = retentionDays;
        }

        /// <summary>Uses a project configuration factory.</summary>
        public OptProject UseConfig(Func<ProjectConfig> configFactory)
        {
            _projectConfigFactory = configFactory ?? throw new ArgumentNullException(nameof(configFactory));
            return this;
        }

        /// <summary>Uses a CPLEX configuration factory.</summary>
        public OptProject UseConfig(Func<CplexConfig> configFactory)
        {
            _configFactory = configFactory ?? throw new ArgumentNullException(nameof(configFactory));
            return this;
        }

        /// <summary>Adds a handler invoked only after a successful solve.</summary>
        public OptProject OnSolved(Action<OptEngine> handler)
        {
            _solvedHandlers.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
            return this;
        }

        /// <summary>Gets the engine created by the latest execution.</summary>
        public OptEngine optEngine { get; private set; }

        /// <summary>Gets whether the latest execution found a usable solution.</summary>
        public bool IsSuccess => _isSuccess;

        /// <summary>Gets the total duration of the latest execution.</summary>
        public TimeSpan totalTimeSpan { get; private set; }

        /// <summary>Measures model construction for the latest execution.</summary>
        public Stopwatch buildModelTimer { get; } = new Stopwatch();

        /// <summary>Measures the complete latest execution.</summary>
        public Stopwatch totalTimer { get; } = new Stopwatch();

        /// <summary>Builds and solves the model.</summary>
        public bool Execute()
        {
            totalTimer.Restart();
            var projectConfig = _projectConfigFactory() ?? new ProjectConfig();
            var solverConfig = _configFactory() ?? new CplexConfig();

            bool nameFromCtor = !string.IsNullOrWhiteSpace(_projectName);
            bool nameFromConfig = !nameFromCtor && !string.IsNullOrWhiteSpace(projectConfig.ProjectName);
            string effectiveProjectName = nameFromCtor
                ? _projectName
                : nameFromConfig ? projectConfig.ProjectName : _model.Name;
            string projectNameSource = nameFromCtor ? "ctor" : nameFromConfig ? "cfg" : "model";
            int effectiveRetentionDays = _retentionDays != 30
                ? _retentionDays
                : projectConfig.RetentionDays ?? 30;

            Logging.SetLogFileName(effectiveProjectName);
            int purged = FolderDir.PurgeOutputs(effectiveRetentionDays);
            if (purged > 0)
                Logging.Info($"[Housekeeping] Purged {purged} files older than {effectiveRetentionDays} days");

            string retentionMarker = effectiveRetentionDays == 30 ? "" : "*";
            string solverLogMarker = projectConfig.EnableSolverLog ? "" : "*";
            string exportLpMarker = projectConfig.ExportLP ? "*" : "";
            string exportMpsMarker = projectConfig.ExportMPS ? "*" : "";
            string exportSolMarker = projectConfig.ExportSol ? "*" : "";
            Logging.Info(
                $"[EffectiveConfig] ProjectName={effectiveProjectName}({projectNameSource}) " +
                $"RetentionDays={effectiveRetentionDays}{retentionMarker} " +
                $"SolverLog={(projectConfig.EnableSolverLog ? "ON" : "OFF")}{solverLogMarker} " +
                $"ExportLP={(projectConfig.ExportLP ? "ON" : "OFF")}{exportLpMarker} " +
                $"ExportMPS={(projectConfig.ExportMPS ? "ON" : "OFF")}{exportMpsMarker} " +
                $"ExportSol={(projectConfig.ExportSol ? "ON" : "OFF")}{exportSolMarker}");

            optEngine?.Dispose();
            optEngine = new OptEngine(solverConfig, projectConfig);
            optEngine.SetModelName(effectiveProjectName);
            optEngine.Build();

            buildModelTimer.Restart();
            _model.ApplyTo(optEngine);
            buildModelTimer.Stop();

            _isSuccess = optEngine.Solve();
            if (_isSuccess)
                foreach (var handler in _solvedHandlers) handler(optEngine);

            totalTimer.Stop();
            totalTimeSpan = totalTimer.Elapsed;
            return _isSuccess;
        }

        /// <summary>Releases the latest native solver engine.</summary>
        public void Dispose() => optEngine?.Dispose();
    }
}
