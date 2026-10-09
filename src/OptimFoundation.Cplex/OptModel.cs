using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
                new ArgumentNullException(nameof(build), "build 不得為 null"),
                "模型定義不合法", null, nameof(AddVariables), Name, "建立動作為空"));
            return this;
        }

        /// <summary>
        /// 加入一個建立變數的步驟，套用時呼叫 <see cref="EngineBase{TModel, TVar, TExpr, TConstr}.BuildVars{TVariable}"/>；
        /// 型別由類別名前綴決定，sets 依 TVariable 的維度順序傳入，零維變數不傳。
        /// </summary>
        public OptModel AddVariables<TVariable>(params object[] sets)
            => AddVariables(engine => engine.BuildVars<TVariable>(sets));

        /// <summary>加入一個建立目標式的步驟。</summary>
        public OptModel AddObjective(Action<OptEngine> build)
        {
            _objectiveSteps.Add(build ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(build), "build 不得為 null"),
                "模型定義不合法", null, nameof(AddObjective), Name, "建立動作為空"));
            return this;
        }

        /// <summary>加入一個建立限制式的步驟。</summary>
        public OptModel AddConstraints(Action<OptEngine> build)
        {
            _constraintSteps.Add(build ?? throw Logging.ErrorOnce(
                new ArgumentNullException(nameof(build), "build 不得為 null"),
                "模型定義不合法", null, nameof(AddConstraints), Name, "建立動作為空"));
            return this;
        }


        /// <summary>
        /// 回傳一行模型定義摘要：模型名，以及變數、目標式、限制式、起始解各註冊了幾個步驟。
        /// 數的是 AddXxx 呼叫次數，不是變數或限制式的實際數量（一個 AddVariables 可能建出上千顆變數）；實際數量在套用後由 CPLEX 提供，見 Trial。
        /// </summary>
        /// <returns>例：<c>模型=Canonical | 變數=3 目標式=1 限制式=4 起始解=0</c>。</returns>
        public string ModelSummary()
        {
            var summary = new List<string>();
            summary.Add($"模型={Name}");
            summary.Add($"變數={_variableSteps.Count} 目標式={_objectiveSteps.Count} 限制式={_constraintSteps.Count} 起始解={_startSteps.Count}");
            return string.Join(" | ", summary);
        }


        /// <summary>
        /// 加入一個建立目標式的步驟：當下以 args 建立 TObjective，套用時呼叫它的 Build(OptEngine)。
        /// args 依建構子參數順序傳入；型別在執行期比對，找不到符合的建構子或 Build(OptEngine) 時當場丟例外。
        /// </summary>
        public OptModel AddObjective<TObjective>(params object[] args)
            => AddObjective(CreateBuildStep(typeof(TObjective), args, nameof(AddObjective)));

        /// <summary>
        /// 加入一個建立限制式的步驟：當下以 args 建立 TConstraint，套用時呼叫它的 Build(OptEngine)。
        /// args 依建構子參數順序傳入；型別在執行期比對，找不到符合的建構子或 Build(OptEngine) 時當場丟例外。
        /// </summary>
        public OptModel AddConstraints<TConstraint>(params object[] args)
            => AddConstraints(CreateBuildStep(typeof(TConstraint), args, nameof(AddConstraints)));

        // 物件在組裝時建立一次，實驗的每個 trial 都對同一個物件呼叫 Build。
        private Action<OptEngine> CreateBuildStep(Type type, object[] args, string caller)
        {
            MethodInfo build = type.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(OptEngine) }, null);
            if (build == null || build.ReturnType != typeof(void))
                throw Logging.ErrorOnce(
                    new MissingMethodException($"{type.Name} 沒有 public void Build(OptEngine engine)"),
                    "模型定義不合法", null, caller, type.Name, "找不到 Build(OptEngine)", $"模型={Name}");

            object instance;
            try
            {
                instance = Activator.CreateInstance(type, args);
            }
            catch (Exception ex) when (ex is MissingMethodException || ex is AmbiguousMatchException)
            {
                string given = args == null ? "" : string.Join(", ", args.Select(a => a == null ? "null" : TypeName(a.GetType())));
                string available = string.Join("；", type.GetConstructors().Select(c =>
                    "(" + string.Join(", ", c.GetParameters().Select(p => $"{TypeName(p.ParameterType)} {p.Name}")) + ")"));
                throw Logging.ErrorOnce(
                    new ArgumentException($"{type.Name} 找不到符合引數的建構子；傳入=({given})；可用建構子={available}", nameof(args), ex),
                    "模型定義不合法", null, caller, type.Name, "建構子引數不符", $"模型={Name} 傳入=({given})");
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                Logging.ErrorOnce(ex.InnerException, "模型定義失敗", null, caller, type.Name, ex.InnerException.Message, $"模型={Name}");
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }

            return (Action<OptEngine>)build.CreateDelegate(typeof(Action<OptEngine>), instance);
        }

        private static string TypeName(Type type)
        {
            if (!type.IsGenericType) return type.Name;
            string name = type.Name.Substring(0, type.Name.IndexOf('`'));
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
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
                    new ArgumentException("模型檔名不得為空白", nameof(fileName)),
                    "模型讀入不合法", null, nameof(ReadModel), fileName, "檔名為空");

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
                    new ArgumentException("解檔檔名不得為空白", nameof(fileName)),
                    "解檔讀入不合法", null, nameof(ReadSolution), Name, "檔名為空");

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
                    new ArgumentNullException(nameof(values), "values 不得為 null"),
                    "模型定義不合法", null, nameof(AddMIPStart), Name, "值建立函式為空");

            _startSteps.Add(engine => engine.AddMIPStart(values(), name));
            return this;
        }
        #endregion


        /// <summary>先讀入模型檔（如有），再依變數、目標式、限制式、起始解的順序執行已保存的步驟。</summary>
        internal void ApplyTo(OptEngine engine)
        {
            if (engine == null)
                throw Logging.ErrorOnce(
                    new ArgumentNullException(nameof(engine), "engine 不得為 null"),
                    "模型套用不合法", null, nameof(ApplyTo), Name, "引擎為空");

            if (_sourceFile != null)
                engine.ReadModel(_sourceFile);
            else if (_variableSteps.Count == 0 && _objectiveSteps.Count == 0 && _constraintSteps.Count == 0)
                Logging.Warn($"[模型為空] 沒有任何建立階段，直接求解空模型 | 名稱={Name} 結果=繼續");

            foreach (var step in _variableSteps) step(engine);
            foreach (var step in _objectiveSteps) step(engine);
            foreach (var step in _constraintSteps) step(engine);
            foreach (var step in _startSteps) step(engine);
        }
    }
}
