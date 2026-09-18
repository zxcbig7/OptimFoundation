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

        private readonly string _sourceFile;

        /// <summary>Gets the stable model name used in experiment trial labels.</summary>
        public string Name { get; }

        /// <summary>匯入來源檔（<see cref="FromFile"/> 建立時才有值）；以 code 建模時為 null。</summary>
        public string SourceFile => _sourceFile;

        /// <summary>Creates an empty model definition.</summary>
        public OptModel(string name = "Model")
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Model" : name;
        }

        private OptModel(string name, string sourceFile) : this(name)
        {
            _sourceFile = sourceFile;
        }

        /// <summary>
        /// 以既有模型檔（.lp / .mps / .sav）定義模型，取代逐步建模。
        /// 套用時走 <see cref="OptEngine.ImportModel"/>：讀檔後自動 re-index，
        /// 因此 metrics、IIS 分析與以名稱取解都照常運作；型別化取解不適用（匯入的模型沒有 C# 變數類別）。
        /// </summary>
        /// <param name="fileName">檔名或相對路徑，以 Models 資料夾為基準；絕對路徑原樣使用。</param>
        /// <param name="name">實驗紀錄用的模型名；省略時取檔名（不含副檔名）。</param>
        /// <remarks>
        /// 仍可再串 <see cref="AddVariables"/> / <see cref="AddObjective"/> / <see cref="AddConstraints"/>：
        /// 匯入先執行，之後才依序套用這些步驟，用於在既有模型上追加內容。
        /// </remarks>
        public static OptModel FromFile(string fileName, string name = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw Logging.ErrorOnce(
                    new ArgumentException("Model file name is required.", nameof(fileName)),
                    "MODEL_DEFINITION_INVALID", "模型定義不合法", nameof(FromFile), fileName, "file_name_is_empty");

            string modelName = string.IsNullOrWhiteSpace(name)
                ? System.IO.Path.GetFileNameWithoutExtension(fileName)
                : name;

            return new OptModel(modelName, fileName);
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

            if (_sourceFile != null)
                engine.ImportModel(_sourceFile);
            else if (_variableSteps.Count == 0 && _objectiveSteps.Count == 0 && _constraintSteps.Count == 0)
                Logging.Warn($"[MODEL_EMPTY] OptModel '{Name}' has no build phases; solving the empty model unchanged");

            foreach (var step in _variableSteps) step(engine);
            foreach (var step in _objectiveSteps) step(engine);
            foreach (var step in _constraintSteps) step(engine);
        }
    }
}
