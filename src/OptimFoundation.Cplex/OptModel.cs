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
            _variableSteps.Add(build ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(build)),
                "MODEL_DEFINITION_INVALID", "模型定義不合法", nameof(AddVariables), Name, "build_action_is_null"));
            return this;
        }

        /// <summary>Adds an objective-building step.</summary>
        public OptModel AddObjective(Action<OptEngine> build)
        {
            _objectiveSteps.Add(build ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(build)),
                "MODEL_DEFINITION_INVALID", "模型定義不合法", nameof(AddObjective), Name, "build_action_is_null"));
            return this;
        }

        /// <summary>Adds a constraint-building step.</summary>
        public OptModel AddConstraints(Action<OptEngine> build)
        {
            _constraintSteps.Add(build ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(build)),
                "MODEL_DEFINITION_INVALID", "模型定義不合法", nameof(AddConstraints), Name, "build_action_is_null"));
            return this;
        }

        /// <summary>Applies all recorded phases in variables, objective, constraints order.</summary>
        internal void ApplyTo(OptEngine engine)
        {
            if (engine == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(engine)),
                    "MODEL_APPLY_INVALID", "模型套用失敗", nameof(ApplyTo), Name, "engine_is_null");

            if (_variableSteps.Count == 0 && _objectiveSteps.Count == 0 && _constraintSteps.Count == 0)
                Logging.Warn($"[MODEL_EMPTY] OptModel '{Name}' has no build phases; solving the empty model unchanged");

            foreach (var step in _variableSteps) step(engine);
            foreach (var step in _objectiveSteps) step(engine);
            foreach (var step in _constraintSteps) step(engine);
        }
    }
}
