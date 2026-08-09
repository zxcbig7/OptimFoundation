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
        private readonly Stopwatch _buildModelTimer = new Stopwatch();
        private readonly Stopwatch _totalTimer = new Stopwatch();

        /// <summary>Creates a runner for a single model.</summary>
        public OptProject(OptModel model, string projectName = null, int retentionDays = 30)
        {
            _model = model ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(model)),
                "PROJECT_INVALID", "專案設定不合法", nameof(OptProject), null, "model_is_null");
            _projectName = projectName;
            _retentionDays = retentionDays;
        }

        /// <summary>Uses a project configuration factory.</summary>
        public OptProject UseConfig(Func<ProjectConfig> configFactory)
        {
            _projectConfigFactory = configFactory ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(configFactory)),
                "PROJECT_INVALID", "專案設定不合法", nameof(UseConfig), _model.Name, "config_factory_is_null");
            return this;
        }

        /// <summary>Uses a CPLEX configuration factory.</summary>
        public OptProject UseConfig(Func<CplexConfig> configFactory)
        {
            _configFactory = configFactory ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(configFactory)),
                "PROJECT_INVALID", "專案設定不合法", nameof(UseConfig), _model.Name, "config_factory_is_null");
            return this;
        }

        /// <summary>Adds a handler invoked only after a successful solve.</summary>
        public OptProject OnSolved(Action<OptEngine> handler)
        {
            _solvedHandlers.Add(handler ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(handler)),
                "PROJECT_INVALID", "專案設定不合法", nameof(OnSolved), _model.Name, "handler_is_null"));
            return this;
        }

        /// <summary>Gets the engine created by the latest execution.</summary>
        public OptEngine Engine { get; private set; }

        /// <summary>Gets whether the latest execution found a usable solution.</summary>
        public bool IsSuccess => _isSuccess;

        /// <summary>Gets the total duration of the latest execution.</summary>
        public TimeSpan TotalElapsed { get; private set; }

        /// <summary>Gets the model-construction duration of the latest execution.</summary>
        public TimeSpan BuildModelElapsed => _buildModelTimer.Elapsed;

        /// <summary>Builds and solves the model.</summary>
        public bool Execute()
        {
            try
            {
                return ExecuteCore();
            }
            catch (Exception ex)
            {
                Logging.ErrorOnce(ex, "PROJECT_EXECUTION_FAILED", "公開 API 執行失敗", nameof(Execute), _model.Name,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        private bool ExecuteCore()
        {
            _totalTimer.Restart();
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

            Engine?.Dispose();
            Engine = new OptEngine(solverConfig, projectConfig);
            Engine.SetModelName(effectiveProjectName);
            Engine.Build();

            _buildModelTimer.Restart();
            _model.ApplyTo(Engine);
            _buildModelTimer.Stop();

            _isSuccess = Engine.Solve();
            if (_isSuccess)
                foreach (var handler in _solvedHandlers) handler(Engine);

            _totalTimer.Stop();
            TotalElapsed = _totalTimer.Elapsed;
            return _isSuccess;
        }

        /// <summary>Releases the latest native solver engine.</summary>
        public void Dispose() => Engine?.Dispose();
    }
}
