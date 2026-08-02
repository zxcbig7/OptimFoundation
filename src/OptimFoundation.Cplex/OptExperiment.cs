using System;
using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>Runs a model/configuration cross-product plus optional explicit cells.</summary>
    public sealed class OptExperiment
    {
        private readonly string _name;
        private readonly string _description;
        private readonly List<OptModel> _models = new List<OptModel>();
        private readonly List<(string Label, CplexConfig Config)> _configs =
            new List<(string, CplexConfig)>();
        private readonly List<(OptModel Model, string Label, CplexConfig Config)> _explicitTrials =
            new List<(OptModel, string, CplexConfig)>();
        private Func<ProjectConfig> _projectConfigFactory = () => new ProjectConfig
        {
            RetentionDays = 0,
            EnableSolverLog = false,
            ExportLP = false,
            ExportMPS = false,
            ExportSol = false,
        };

        /// <summary>Creates an experiment.</summary>
        public OptExperiment(string name, string description)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Experiment name is required.", nameof(name));
            _name = name;
            _description = description ?? string.Empty;
        }

        /// <summary>Uses a fresh project configuration for each experiment cell.</summary>
        public OptExperiment UseConfig(Func<ProjectConfig> configFactory)
        {
            _projectConfigFactory = configFactory ?? throw new ArgumentNullException(nameof(configFactory));
            return this;
        }

        /// <summary>Adds a model to the cross-product.</summary>
        public OptExperiment AddModel(OptModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            _models.Add(model);
            return this;
        }

        /// <summary>Adds a labelled solver configuration to the cross-product.</summary>
        public OptExperiment AddConfig(string label, CplexConfig config)
        {
            ValidateLabel(label);
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (_configs.Any(c => string.Equals(c.Label, label, StringComparison.Ordinal)))
                throw new ArgumentException($"Duplicate experiment configuration label '{label}'.", nameof(label));
            _configs.Add((label, config));
            return this;
        }

        /// <summary>Adds exactly one explicit model/configuration cell.</summary>
        public OptExperiment AddTrial(OptModel model, string label, CplexConfig config)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            ValidateLabel(label);
            if (config == null) throw new ArgumentNullException(nameof(config));
            _explicitTrials.Add((model, label, config));
            return this;
        }

        /// <summary>Runs all cells and saves the resulting experiment.</summary>
        public Experiment Run()
        {
            var cells = new List<(OptModel Model, string Label, CplexConfig Config)>();
            foreach (var model in _models)
                foreach (var config in _configs)
                    cells.Add((model, config.Label, config.Config));
            cells.AddRange(_explicitTrials);

            if (cells.Count == 0)
                throw new InvalidOperationException("An experiment requires at least one model/configuration cell.");

            var finalLabels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cell in cells)
            {
                string finalLabel = $"{cell.Model.Name} | {cell.Label}";
                if (!finalLabels.Add(finalLabel))
                    throw new InvalidOperationException(
                        $"Duplicate final experiment trial label '{finalLabel}'.");
            }

            var experiment = new Experiment(_name, _description);
            foreach (var cell in cells)
            {
                var projectConfig = _projectConfigFactory() ?? new ProjectConfig();
                using var engine = new OptEngine(cell.Config.Clone(), projectConfig);
                engine.SetModelName($"{_name}-{cell.Model.Name}-{cell.Label}");
                engine.Build();
                cell.Model.ApplyTo(engine);
                string trialLabel = $"{cell.Model.Name} | {cell.Label}";
                experiment.AddTrial(Trial.Capture(engine, trialLabel, () => engine.Solve()));
            }

            experiment.Save();
            return experiment;
        }

        private static void ValidateLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("Experiment configuration label is required.", nameof(label));
        }
    }
}
