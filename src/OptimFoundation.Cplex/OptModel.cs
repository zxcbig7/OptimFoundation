using System;
using System.Collections.Generic;
using OptimFoundation.Core;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// 保存建立變數、目標式與限制式的步驟，由 ApplyTo() 依序套用到 OptEngine。
    /// 本身不執行求解；由 <see cref="OptProject"/> 或 <see cref="OptExperiment"/> 建立引擎並執行這些步驟。
    /// 也可先讀入 .lp / .mps / .sav 模型檔，再執行額外的建模步驟。
    /// </summary>
    public sealed class OptModel
    {
        /// <summary>模型名稱，用來區分求解與實驗紀錄。</summary>
        public string Name { get; }
        private readonly string _sourceFile;

        #region  Build Model Steps
        private readonly List<Action<OptEngine>> _variableSteps = new List<Action<OptEngine>>();
        private readonly List<Action<OptEngine>> _objectiveSteps = new List<Action<OptEngine>>();
        private readonly List<Action<OptEngine>> _constraintSteps = new List<Action<OptEngine>>();
        private readonly List<Action<OptEngine>> _startSteps = new List<Action<OptEngine>>();

        /// <summary>建立尚未加入任何建模步驟的模型定義；名稱空白時使用 Model。</summary>
        public OptModel(string name = "Model")
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Model" : name;
        }

        /// <summary>加入一個建立變數的步驟。</summary>
        public OptModel AddVariables(Action<OptEngine> build)
        {
            _variableSteps.Add(build ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(build)),
                "MODEL_DEFINITION_INVALID", "模型定義不合法", nameof(AddVariables), Name, "build_action_is_null"));
            return this;
        }

        /// <summary>加入一個建立目標式的步驟。</summary>
        public OptModel AddObjective(Action<OptEngine> build)
        {
            _objectiveSteps.Add(build ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(build)),
                "MODEL_DEFINITION_INVALID", "模型定義不合法", nameof(AddObjective), Name, "build_action_is_null"));
            return this;
        }

        /// <summary>加入一個建立限制式的步驟。</summary>
        public OptModel AddConstraints(Action<OptEngine> build)
        {
            _constraintSteps.Add(build ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(build)),
                "MODEL_DEFINITION_INVALID", "模型定義不合法", nameof(AddConstraints), Name, "build_action_is_null"));
            return this;
        }

        #endregion


        #region Read Existing Model
        /// <summary>匯入來源檔（<see cref="ReadModel"/> 建立時才有值）；以 code 建模時為 null。</summary>
        public string SourceFile => _sourceFile;

        /// <summary>
        /// 指定既有模型檔（.lp / .mps / .sav），套用模型時才呼叫 <see cref="OptEngine.ReadModel"/>。
        /// </summary>
        /// <param name="fileName">檔名或相對路徑，以 FolderDir.Model 為基準；絕對路徑原樣使用。</param>
        /// <param name="name">實驗紀錄用的模型名；省略時取檔名（不含副檔名）。</param>
        /// <remarks>
        /// 先匯入，再執行追加的建模步驟；型別化取解的命名限制見 <see cref="OptEngine.ReadModel"/>。
        /// </remarks>
        public static OptModel ReadModel(string fileName, string name = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw Logging.ErrorOnce(
                    new ArgumentException("Model file name is required.", nameof(fileName)),
                    "MODEL_DEFINITION_INVALID", "模型定義不合法", nameof(ReadModel), fileName, "file_name_is_empty");

            string modelName = string.IsNullOrWhiteSpace(name)
                ? System.IO.Path.GetFileNameWithoutExtension(fileName)
                : name;

            return new OptModel(modelName, fileName);
        }
        private OptModel(string name, string sourceFile) : this(name)
        {
            _sourceFile = sourceFile;
        }
        #endregion


        #region Read Existing Solution (MIP start)
        /// <summary>
        /// 指定求解前要讀入的起始解檔（.sol / .mst），供 CPLEX 從已有的解開始搜尋。
        /// 套用時走 <see cref="OptEngine.ReadSolution"/>，在所有建模步驟之後執行（變數要先存在）。
        /// </summary>
        /// <param name="fileName">檔名或相對路徑，以 FolderDir.Solution 為基準；絕對路徑原樣使用。</param>
        public OptModel ReadSolution(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw Logging.ErrorOnce(
                    new ArgumentException("Solution file name is required.", nameof(fileName)),
                    "MODEL_DEFINITION_INVALID", "模型定義不合法", nameof(ReadSolution), Name, "file_name_is_empty");

            _startSteps.Add(engine => engine.ReadSolution(fileName));
            return this;
        }

        /// <summary>
        /// 指定一組起始解（MIP start），內容為「變數全名 → 值」；建模完成後、求解前才加入引擎。
        /// values 延後至套用模型時呼叫，可讀取前一次求解結果。
        /// </summary>
        /// <param name="values">套用時呼叫，回傳 MIP start 內容；回傳 null 會在套用時丟例外。</param>
        /// <param name="name">MIP start 名稱；null 由 solver 自動命名。</param>
        public OptModel AddMIPStart(Func<IReadOnlyDictionary<string, double>> values, string name = null)
        {
            if (values == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(values)),
                    "MODEL_DEFINITION_INVALID", "模型定義不合法", nameof(AddMIPStart), Name, "values_factory_is_null");

            _startSteps.Add(engine => engine.AddMIPStart(values(), name));
            return this;
        }
        #endregion


        /// <summary>先讀入模型檔（如有），再依變數、目標式、限制式、起始解的順序執行已保存的步驟。</summary>
        internal void ApplyTo(OptEngine engine)
        {
            if (engine == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(engine)),
                    "MODEL_APPLY_INVALID", "模型套用失敗", nameof(ApplyTo), Name, "engine_is_null");

            if (_sourceFile != null)
                engine.ReadModel(_sourceFile);
            else if (_variableSteps.Count == 0 && _objectiveSteps.Count == 0 && _constraintSteps.Count == 0)
                Logging.Warn($"[MODEL_EMPTY] OptModel '{Name}' has no build phases; solving the empty model unchanged");

            foreach (var step in _variableSteps) step(engine);
            foreach (var step in _objectiveSteps) step(engine);
            foreach (var step in _constraintSteps) step(engine);
            foreach (var step in _startSteps) step(engine);
        }
    }
}
