using System;
using System.Collections.Generic;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// Defines a reusable optimization model. It records model-building phases only;
    /// execution belongs to <see cref="OptProject"/> or <see cref="OptExperiment"/>.
    /// </summary>
    public sealed class OptModel
    {
        private readonly List<Action<OptEngine>> _variableSteps = new List<Action<OptEngine>>();
        private readonly List<Action<OptEngine>> _objectiveSteps = new List<Action<OptEngine>>();
        private readonly List<Action<OptEngine>> _constraintSteps = new List<Action<OptEngine>>();

        /// <summary>Gets the stable model name used in experiment trial labels.</summary>
        public string Name { get; }

        /// <summary>Creates an empty model definition.</summary>
        public OptModel(string name = "Model")
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Model" : name;
        }

        /// <summary>Adds a variable-building step.</summary>
        public OptModel AddVariables(Action<OptEngine> build)
        {
            _variableSteps.Add(build ?? throw new ArgumentNullException(nameof(build)));
            return this;
        }

        /// <summary>Adds an objective-building step.</summary>
        public OptModel AddObjective(Action<OptEngine> build)
        {
            _objectiveSteps.Add(build ?? throw new ArgumentNullException(nameof(build)));
            return this;
        }

        /// <summary>Adds a constraint-building step.</summary>
        public OptModel AddConstraints(Action<OptEngine> build)
        {
            _constraintSteps.Add(build ?? throw new ArgumentNullException(nameof(build)));
            return this;
        }

        /// <summary>Applies all recorded phases in variables, objective, constraints order.</summary>
        internal void ApplyTo(OptEngine engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));

            if (_variableSteps.Count == 0 && _objectiveSteps.Count == 0 && _constraintSteps.Count == 0)
                Logging.Warn($"[MODEL_EMPTY] OptModel '{Name}' has no build phases; solving the empty model unchanged");

            foreach (var step in _variableSteps) step(engine);
            foreach (var step in _objectiveSteps) step(engine);
            foreach (var step in _constraintSteps) step(engine);
        }
    }
}
