using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ILOG.Concert;
using ILOG.CPLEX;
using OptimFoundation.Core;
using static ILOG.CPLEX.Cplex;

namespace OptimFoundation.Cplex
{
    /// <summary>
    /// 將框架的建模、求解與取解操作轉成 CPLEX 呼叫。
    /// 直接使用時須先 Build()；一般由 OptProject 與 OptExecution（OptProduction / OptExperiment）管理生命週期。
    /// </summary>
    public partial class OptEngine : EngineBase<ILOG.CPLEX.Cplex, INumVar, ILinearNumExpr, IRange>
    {
        /// <summary>
        /// 模型名稱。
        /// </summary>
        private string _modelName { get; set; }


        #region Project Configuration
        private ProjectConfig _projectConfig;
        private bool _exportLp { get { return _projectConfig.ExportLP; } }
        private bool _exportIIs { get { return _projectConfig.ExportIIS; } }
        private bool _exportMps { get { return _projectConfig.ExportMPS; } }
        private bool _exportSol { get { return _projectConfig.ExportSol; } }
        private bool _enableLog { get { return _projectConfig.EnableSolverLog; } }
        # endregion



        private readonly List<IRange> _constraints = new List<IRange>();
        private readonly string _startTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // 暫存 CPLEX 的 log，求解結束後寫入框架 log 檔。
        private MemoryStream _solverLogStream;
        private StreamWriter _solverLogWriter;

        // 基底類別檢查一般限制式是否重複；這個集合另外記錄跨引擎建立的限制式，因為兩者清除的時機不同。
        private readonly HashSet<string> _threadVerifyConstraints = new HashSet<string>();
        // Thread 限制式尚未加入模型，由 MergeModel 加入、ResetThreadConstraint 移除。
        private int _threadRuleCount = 0;
        private readonly List<IRange> _threadConstraints = new List<IRange>();
        private List<string> _conflictConstraints = null;


        /// <summary>保存求解器設定並建立引擎；呼叫 Build() 時才建立 CPLEX 模型。</summary>
        public OptEngine(CplexConfig config) : this(config, new ProjectConfig()) { }

        /// <summary>保存求解器設定並建立引擎；呼叫 Build() 時才建立 CPLEX 模型。</summary>
        public OptEngine(ProjectConfig projectConfig) : this(new CplexConfig(), projectConfig) { }

        /// <summary>保存求解器與專案設定並建立引擎；呼叫 Build() 時才建立 CPLEX 模型。config 為 null 時沿用 CPLEX 預設值，projectConfig 為 null 時使用預設設定。</summary>
        public OptEngine(CplexConfig config, ProjectConfig projectConfig) : base(config ?? new CplexConfig())
        {
            _projectConfig = projectConfig ?? new ProjectConfig();
        }

        /// <summary>使用空的 CplexConfig 與預設 ProjectConfig 建立引擎；求解器參數沿用 CPLEX 預設值。</summary>
        public OptEngine() : this(new CplexConfig(), new ProjectConfig()) { }

        /// <summary>指定 CPLEX 處理 AddMIPStart 起始解的方式；部分解可用 SolveFixed 或 Repair 補齊。</summary>
        public MIPStartEffort MipStartEffort { get; set; } = MIPStartEffort.Auto;

        #region 跑一次 OptModel（OptProduction 與 OptExperiment 共用的唯一執行路徑）
        // 耗時統一用 CPLEX 時鐘（Cplex.GetCplexTime，單位秒）。
        private double _runStartCplexTime;
        private double _modelApplySeconds;

        /// <summary>最近一次 <see cref="RunModel"/> 的建模耗時（CPLEX 時鐘）；讀入模型檔時，包含讀檔與建立查找索引的時間。</summary>
        internal TimeSpan ModelApplyElapsed => TimeSpan.FromSeconds(_modelApplySeconds);

        /// <summary>從最近一次 <see cref="RunModel"/> 建好 CPLEX 模型起，到現在經過的時間（CPLEX 時鐘）。</summary>
        internal TimeSpan ElapsedSinceRunStart => TimeSpan.FromSeconds(Model.GetCplexTime() - _runStartCplexTime);

        /// <summary>建立引擎、套用模型、執行 beforeSolve 後求解，並回傳 Trial。</summary>
        /// <param name="model">要套用的模型定義。</param>
        /// <param name="label">Trial 標籤（AddSolverConfig / AddTrial 給的設定名）。</param>
        /// <param name="captureTrajectory">是否開收斂軌跡；callback 會改變搜尋路徑，正式環境不開。</param>
        /// <param name="beforeSolve">模型建好後、Solve 前要執行的動作；可為 null。</param>
        /// <param name="solved">Solve 是否回報可用解（Optimal 或 Feasible）。</param>
        internal Trial RunModel(OptModel model, string label, bool captureTrajectory,
            Action<OptEngine> beforeSolve, out bool solved)
        {
            Build();
            _runStartCplexTime = Model.GetCplexTime();

            model.ApplyTo(this);
            _modelApplySeconds = Model.GetCplexTime() - _runStartCplexTime;

            beforeSolve?.Invoke(this);

            bool ok = false;
            var trial = Trial.Capture(this, label, () => ok = Solve(), captureTrajectory: captureTrajectory);
            solved = ok;
            trial.Model = model.Name;
            // 建模 + 求解 = 兩段 CPLEX 時鐘量到的時間相加；beforeSolve 與匯出檔案不算在內
            trial.Metrics.BuildAndSolveTimeMs = _modelApplySeconds * 1000.0 + trial.Metrics.SolveTimeMs;
            return trial;
        }
        #endregion

        /// <summary>本引擎記錄的限制式數量；不含另由 CreateXxxThread 建立並保存的限制式。</summary>
        public override int ConstraintCount => _constraints.Count;

        /// <summary>SetModelName 設定的模型名，用於 LP / MPS / Sol / IIS 檔名；未設定時使用 "Model"。</summary>
        public string ModelName => _modelName;

        /// <summary>引擎建立時間（yyyy-MM-dd_HH-mm-ss），用於輸出檔名。</summary>
        public string StartTime => _startTime;

        /// <summary>CreateXxxThread 建立並保存在本引擎的限制式數量；可透過 MergeModel 加入模型。</summary>
        public int ThreadConstraintCount => _threadConstraints.Count;

        #region ITrajectorySource（CPLEX 支援逐點軌跡）
        private bool _captureTrajectory;
        /// <summary>CPLEX 以 MIPInfoCallback 支援收斂軌跡，恆為 true。</summary>
        public override bool SupportsTrajectory => true;

        /// <summary>開始記錄目標值、best bound 與 MIP gap；須在 Solve 前呼叫，callback 可能增加求解時間。</summary>
        public override void EnableTrajectory() => _captureTrajectory = true;

        /// <summary>CPLEX 的 branch-and-bound callback；限制取樣頻率與筆數，避免記錄過多。</summary>
        private sealed class TrajectoryCallback : ILOG.CPLEX.Cplex.MIPInfoCallback
        {
            private const double MinIntervalMs = 200.0;
            private const int MaxPoints = 2000;

            private readonly object _lock = new object();
            /// <summary>已記錄的求解進度；SolveCore 在求解結束後存入 LastMetrics.Convergence。</summary>
            public readonly List<ConvergencePoint> Points = new List<ConvergencePoint>();
            private double _lastObj = double.NaN;
            private double _lastTimeMs = double.NegativeInfinity;

            /// <summary>
            /// 符合記錄條件時新增一筆求解進度；ElapsedMs 取 CPLEX 提供的 GetCplexTime − GetStartTime（這次求解開始後經過的時間）。
            /// CPLEX 可能從多個執行緒呼叫，因此用 lock 保護共用清單。
            /// </summary>
            public override void Main()
            {
                double nowMs = (GetCplexTime() - GetStartTime()) * 1000.0;
                bool hasInc = HasIncumbent();
                double inc = hasInc ? GetIncumbentObjValue() : double.NaN;
                double bound = GetBestObjValue();
                double gap = hasInc ? GetMIPRelativeGap() : double.NaN;

                lock (_lock)
                {
                    bool improved = hasInc && inc != _lastObj;
                    bool intervalHit = (nowMs - _lastTimeMs) >= MinIntervalMs;
                    if ((!improved && !intervalHit) || Points.Count >= MaxPoints) return;

                    Points.Add(new ConvergencePoint
                    {
                        ElapsedMs = nowMs,
                        ObjectiveValue = inc,
                        BestBound = bound,
                        Gap = gap
                    });
                    _lastObj = inc;
                    _lastTimeMs = nowMs;
                }
            }
        }
        #endregion

        #region 模型名稱

        /// <summary>設定 LP / MPS / Sol / IIS 輸出檔的名稱前綴；OptProduction 與 OptExperiment 會在求解前自動設定。</summary>
        /// <exception cref="ArgumentException">name 為 null 或空白。</exception>
        public void SetModelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw Logging.ErrorOnce(
                    new ArgumentException("模型名稱不得為空白", nameof(name)),
                    "模型名稱不合法", null, nameof(SetModelName), name, "名稱為空");
            _modelName = name;
        }

        #endregion

        #region 模型匯入 / 匯出（.lp / .mps / .sav）

        /// <summary>
        /// 立即匯出目前模型，檔名由呼叫端指定。
        /// ProjectConfig 的 ExportLP / ExportMPS 則在 Solve() 時自動匯出，檔名帶時間戳。
        /// </summary>
        /// <param name="fileName">
        /// 檔名或相對路徑，以 FolderDir.Model 為基準；絕對路徑原樣使用。
        /// 副檔名決定格式：.lp / .mps / .sav，以及各自的 .gz / .bz2（大小寫不拘），其餘副檔名直接拒絕。
        /// 需精確保存係數時用 .sav；.lp / .mps 的文字係數可能被截斷。
        /// </param>
        /// <exception cref="InvalidOperationException">尚未呼叫 Build()。</exception>
        /// <exception cref="ArgumentException">fileName 為 null、空白，或副檔名不是支援的模型格式。</exception>
        public void ExportModel(string fileName)
        {
            if (Model == null)
                throw Logging.ErrorOnce(
                    new InvalidOperationException("ExportModel 必須在 Build() 之後呼叫"),
                    "模型匯出失敗", null, nameof(ExportModel), fileName, "引擎尚未建立");

            if (string.IsNullOrWhiteSpace(fileName))
                throw Logging.ErrorOnce(
                    new ArgumentException("匯出檔名不得為空白", nameof(fileName)),
                    "模型匯出不合法", null, nameof(ExportModel), fileName, "檔名為空");

            string format = ResolveModelFileFormat(fileName, "模型匯出失敗", nameof(ExportModel));

            string path;
            if (Path.IsPathRooted(fileName))
            {
                path = fileName;
            }
            else
            {
                FolderDir.Model.CreateFolder();
                path = FolderDir.Model.GetPathFile(fileName);
            }

            try
            {
                Model.ExportModel(path);
            }
            catch (System.Exception ex)
            {
                throw Logging.ErrorOnce(
                    ex, "模型匯出失敗", null, nameof(ExportModel), path,
                    ex.GetBaseException().Message, $"例外型別={ex.GetType().FullName}");
            }

            Logging.Info($"[模型匯出完成] 路徑={path} 格式={format}");
        }

        /// <summary>從檔案讀入模型；只替換模型內容，不會重設 solver 參數。</summary>
        /// <remarks>
        /// 讀入後會重建變數與限制式索引；型別化取解須符合 TypeName@… 命名。
        /// </remarks>
        /// <param name="fileName">
        /// 檔名或相對路徑，以 FolderDir.Model 為基準；絕對路徑原樣使用。
        /// 副檔名決定格式：.lp / .mps / .sav，以及各自的 .gz / .bz2（大小寫不拘），其餘副檔名直接拒絕。
        /// 需精確保存係數時用 .sav；.lp / .mps 的文字係數可能被截斷。
        /// </param>
        /// <returns>重新建立查找索引後得到的變數與限制式數量。</returns>
        /// <exception cref="InvalidOperationException">尚未呼叫 Build()。</exception>
        /// <exception cref="ArgumentException">fileName 為 null、空白，或副檔名不是支援的模型格式。</exception>
        /// <exception cref="FileNotFoundException">檔案不存在。</exception>
        public (int VarCount, int ConstraintCount) ReadModel(string fileName)
        {
            if (Model == null)
                throw Logging.ErrorOnce(
                    new InvalidOperationException("ReadModel 必須在 Build() 之後呼叫"),
                    "模型讀入失敗", null, nameof(ReadModel), fileName, "引擎尚未建立");

            if (string.IsNullOrWhiteSpace(fileName))
                throw Logging.ErrorOnce(
                    new ArgumentException("讀入檔名不得為空白", nameof(fileName)),
                    "模型讀入不合法", null, nameof(ReadModel), fileName, "檔名為空");

            string format = ResolveModelFileFormat(fileName, "模型讀入失敗", nameof(ReadModel));

            string path = Path.IsPathRooted(fileName)
                ? fileName
                : FolderDir.Model.GetPathFile(fileName);

            if (!File.Exists(path))
                throw Logging.ErrorOnce(
                    new FileNotFoundException($"找不到模型檔：{path}", path),
                    "模型讀入失敗", null, nameof(ReadModel), path, "找不到檔案");

            if (Variables.Count > 0 || _constraints.Count > 0)
                Logging.Warn($"[模型讀入覆寫] 讀入的模型將覆寫既有建模內容 | 變數數量={Variables.Count} 限制式數量={_constraints.Count} 結果=覆寫");

            try
            {
                Model.ImportModel(path);
            }
            catch (System.Exception ex)
            {
                throw Logging.ErrorOnce(
                    ex, "模型讀入失敗", null, nameof(ReadModel), path,
                    ex.GetBaseException().Message, $"例外型別={ex.GetType().FullName}");
            }

            var counts = ReindexFromModel();
            Logging.Info($"[模型讀入完成] 路徑={path} 格式={format} 變數數量={counts.VarCount} 限制式數量={counts.ConstraintCount}");
            return counts;
        }

        // CPLEX 依副檔名判格式且大小寫不拘；.NET API 文件（ILOG.CPLEX.xml）只列 .lp / .mps / .sav 與其 .gz / .bz2
        private static readonly string[] ModelFileExtensions = { ".lp", ".mps", ".sav" };
        private static readonly string[] ModelFileCompressions = { ".gz", ".bz2" };

        /// <summary>
        /// 檢查模型副檔名；提前拒絕不支援的格式，讓錯誤附上支援清單。
        /// </summary>
        private static string ResolveModelFileFormat(string fileName, string eventName, string context)
        {
            string ext = Path.GetExtension(fileName);
            string compression = ModelFileCompressions.FirstOrDefault(c => c.Equals(ext, StringComparison.OrdinalIgnoreCase));
            string formatExt = compression == null ? ext : Path.GetExtension(Path.GetFileNameWithoutExtension(fileName));
            string format = ModelFileExtensions.FirstOrDefault(f => f.Equals(formatExt, StringComparison.OrdinalIgnoreCase));

            if (format == null)
                throw Logging.ErrorOnce(
                    new ArgumentException(
                        $"不支援的模型檔副檔名：{fileName}，請用 .lp / .mps / .sav，可再接 .gz / .bz2",
                        nameof(fileName)),
                    eventName, null, context, fileName, "不支援的副檔名", "支援格式=.lp|.mps|.sav[.gz|.bz2]");

            return (format + compression).TrimStart('.').ToUpperInvariant();
        }

        /// <summary>
        /// ImportModel 不會登記框架索引，須從 ILPMatrix 補登變數與限制式。
        /// </summary>
        private (int VarCount, int ConstraintCount) ReindexFromModel()
        {
            Variables.Clear();
            _constraints.Clear();
            _conflictConstraints = null;
            ResetVerifyConstraints();
            _objective = Model.GetObjective();

            var enumerator = Model.GetLPMatrixEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Current is not ILPMatrix matrix) continue;

                INumVar[] vars = matrix.NumVars;
                for (int i = 0; i < vars.Length; i++)
                    Variables[ResolveImportedName(vars[i].Name, "x", i, Variables.Count)] = vars[i];

                IRange[] rows = matrix.Ranges;
                for (int i = 0; i < rows.Length; i++)
                {
                    if (string.IsNullOrEmpty(rows[i].Name))
                        rows[i].Name = $"c{_constraints.Count}";
                    _constraints.Add(rows[i]);
                }
            }

            RecordImportedModel(_objective == null ? (Core.ObjectiveSense?)null : ToCoreSense(_objective.Sense));

            return (Variables.Count, _constraints.Count);
        }

        // 匯入的名稱不受框架命名規則約束：可能為空、也可能重複。空的補流水號，重複的加後綴保住索引的唯一性。
        private string ResolveImportedName(string rawName, string fallbackPrefix, int index, int registered)
        {
            string name = string.IsNullOrEmpty(rawName) ? $"{fallbackPrefix}{index}" : rawName;
            if (!Variables.ContainsKey(name)) return name;

            string unique = $"{name}#{registered}";
            while (Variables.ContainsKey(unique)) unique += "#";
            Logging.Warn($"[讀入變數重複] 名稱={name} 新名稱={unique} 結果=改名");
            return unique;
        }

        #endregion

        #region 解匯入 / 匯出（.sol / .mst，MIP start pipeline）

        /// <summary>讀入解檔作為起始解，不會取代目前模型。</summary>
        /// <remarks>
        /// 請在建模完成、Solve 前呼叫；找不到對應變數的項目會被忽略。
        /// .mst 只適用於 MIP；AdvancedStart = 0 時 CPLEX 不會採用起始解。
        /// </remarks>
        /// <param name="fileName">檔名或相對路徑，以 FolderDir.Solution 為基準；絕對路徑原樣使用。</param>
        /// <returns>讀入後模型持有的 MIP start 數；LP 為 0。</returns>
        /// <exception cref="InvalidOperationException">尚未呼叫 Build()。</exception>
        /// <exception cref="ArgumentException">fileName 為 null 或空白。</exception>
        /// <exception cref="FileNotFoundException">檔案不存在。</exception>
        public int ReadSolution(string fileName)
        {
            if (Model == null)
                throw Logging.ErrorOnce(
                    new InvalidOperationException("ReadSolution 必須在 Build() 之後呼叫"),
                    "解檔讀入失敗", null, nameof(ReadSolution), fileName, "引擎尚未建立");

            if (string.IsNullOrWhiteSpace(fileName))
                throw Logging.ErrorOnce(
                    new ArgumentException("解檔檔名不得為空白", nameof(fileName)),
                    "解檔讀入不合法", null, nameof(ReadSolution), fileName, "檔名為空");

            string path = Path.IsPathRooted(fileName)
                ? fileName
                : FolderDir.Solution.GetPathFile(fileName);

            if (!File.Exists(path))
                throw Logging.ErrorOnce(
                    new FileNotFoundException($"找不到解檔：{path}", path),
                    "解檔讀入失敗", null, nameof(ReadSolution), path, "找不到檔案");

            bool isMst = path.EndsWith(".mst", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".mst.gz", StringComparison.OrdinalIgnoreCase);
            bool isMip = Model.IsMIP();

            if (isMst && !isMip)
            {
                Logging.Warn($"[起始解略過] 起始解檔 | 路徑={path} 模型類型={ModelType} 原因=線性規劃模型 結果=略過");
                return 0;
            }

            if (SolverConfig is CplexConfig { AdvancedStart: 0 })
                Logging.Warn($"[起始解已停用] CPLEX 不會使用起始解 | AdvancedStart=0 路徑={path} 結果=繼續");

            try
            {
                if (isMst) Model.ReadMIPStarts(path);
                else Model.ReadStartInfo(path);
            }
            catch (System.Exception ex)
            {
                throw Logging.ErrorOnce(
                    ex, "解檔讀入失敗", null, nameof(ReadSolution), path,
                    ex.GetBaseException().Message, $"例外型別={ex.GetType().FullName}");
            }

            int starts = isMip ? Model.GetNMIPStarts() : 0;
            Logging.Info($"[解檔讀入完成] 路徑={path} 格式={(isMst ? "mst" : "sol")} 模型類型={ModelType} 起始解數量={starts}");
            return starts;
        }

        /// <summary>
        /// 將目前的解寫入指定檔案，可在下次求解前用 <see cref="ReadSolution"/> 讀回。
        /// 與 <see cref="ExportModel"/> 一樣，檔名由呼叫端指定；ProjectConfig.ExportSol 則在 Solve() 時自動匯出，檔名帶時間戳。
        /// </summary>
        /// <param name="fileName">檔名或相對路徑，以 FolderDir.Solution 為基準；絕對路徑原樣使用。</param>
        /// <returns>實際寫出的完整路徑。</returns>
        /// <exception cref="InvalidOperationException">尚未 Build()，或最近一次求解沒有可用解。</exception>
        /// <exception cref="ArgumentException">fileName 為 null 或空白。</exception>
        public string ExportSolution(string fileName)
        {
            if (Model == null || !(Status == SolveStatus.Optimal || Status == SolveStatus.Feasible))
                throw Logging.ErrorOnce(
                    new InvalidOperationException("ExportSolution 必須在 Solve() 取得可用解之後呼叫"),
                    "解檔匯出失敗", null, nameof(ExportSolution), fileName, "沒有可用的解",
                    $"狀態={Status}");

            if (string.IsNullOrWhiteSpace(fileName))
                throw Logging.ErrorOnce(
                    new ArgumentException("解檔檔名不得為空白", nameof(fileName)),
                    "解檔匯出不合法", null, nameof(ExportSolution), fileName, "檔名為空");

            string path;
            if (Path.IsPathRooted(fileName))
            {
                path = fileName;
            }
            else
            {
                FolderDir.Solution.CreateFolder();
                path = FolderDir.Solution.GetPathFile(fileName);
            }

            try
            {
                Model.WriteSolution(path);
            }
            catch (System.Exception ex)
            {
                throw Logging.ErrorOnce(
                    ex, "解檔匯出失敗", null, nameof(ExportSolution), path,
                    ex.GetBaseException().Message, $"例外型別={ex.GetType().FullName}");
            }

            Logging.Info($"[解檔匯出完成] 路徑={path}");
            return path;
        }

        #endregion

        #region EngineBase 抽象方法實作

        /// <summary>建立單一 CPLEX 變數並登記到 Variables；VarType 對應 NumVarType（Binary→Bool、Integer→Int、其餘→Float）。</summary>
        protected override INumVar AddVariable(string name, double lb, double ub, VarType type)
        {
            NumVarType cplexType = type switch
            {
                VarType.Integer => NumVarType.Int,
                VarType.Binary => NumVarType.Bool,
                _ => NumVarType.Float
            };
            var var = Model.NumVar(lb, ub, cplexType, name);
            Variables[name] = var;
            return var;
        }

        /// <summary>
        /// 用 CPLEX NumVarArray 一次建立整批變數，減少 .NET 與 CPLEX 之間逐筆呼叫的成本。
        /// </summary>
        protected override void AddVariables(IReadOnlyList<string> names, double lb, double ub, VarType type)
        {
            int n = names.Count;
            var lbs = new double[n];
            var ubs = new double[n];
            var types = new NumVarType[n];
            var nameArr = new string[n];

            NumVarType cplexType = type switch
            {
                VarType.Integer => NumVarType.Int,
                VarType.Binary => NumVarType.Bool,
                _ => NumVarType.Float
            };

            for (int i = 0; i < n; i++)
            {
                lbs[i] = lb;
                ubs[i] = ub;
                types[i] = cplexType;
                nameArr[i] = names[i];
            }

            INumVar[] vars = Model.NumVarArray(n, lbs, ubs, types, nameArr);
            for (int i = 0; i < n; i++)
                Variables[nameArr[i]] = vars[i];
        }

        /// <summary>把 (係數, 變數) 序列組成 CPLEX 線性表達式；terms 只迭代一次，不會保留參考。</summary>
        protected override ILinearNumExpr LinearExpr(IEnumerable<(double coef, INumVar var)> terms)
        {
            var expr = Model.LinearNumExpr();
            foreach (var (coef, v) in terms)
                expr.AddTerm(coef, v);
            return expr;
        }

        /// <summary>新增限制式到 CPLEX 模型、設定名稱，並登記進 _constraints（供 ConstraintCount 與 IIS 分析使用）。</summary>
        /// <exception cref="ArgumentOutOfRangeException">sense 不是 ≤ / = / ≥。</exception>
        protected override IRange AddConstraint(string name, ILinearNumExpr lhs, ConstraintSense sense, double rhs)
        {
            IRange r = sense switch
            {
                ConstraintSense.LessEqual => Model.AddLe(lhs, rhs),
                ConstraintSense.Equal => Model.AddEq(lhs, rhs),
                ConstraintSense.GreaterEqual => Model.AddGe(lhs, rhs),
                _ => throw Logging.ErrorOnce(
                    new ArgumentOutOfRangeException(nameof(sense), "不支援的限制式方向"),
                    "限制式方向不合法", null, nameof(AddConstraint), sense,
                    "不支援的限制式方向")
            };
            r.Name = name;
            _constraints.Add(r);
            return r;
        }

        /// <summary>新增範圍限制式 lb ≤ expr ≤ ub，並登記進 _constraints。</summary>
        protected override IRange AddRangeConstraint(string name, ILinearNumExpr expr, double lb, double ub)
        {
            var r = Model.AddRange(lb, expr, ub);
            r.Name = name;
            _constraints.Add(r);
            return r;
        }

        private IObjective _objective;

        /// <summary>
        /// 先移除舊目標式再新增，以支援軟限制懲罰項更新；constant 累加至 expr.Constant。
        /// </summary>
        protected override void SetObjective(ILinearNumExpr expr, double constant, Core.ObjectiveSense sense)
        {
            if (constant != 0) expr.Constant += constant;
            if (_objective != null) Model.Remove(_objective);
            _objective = sense == Core.ObjectiveSense.Minimize
                ? Model.AddMinimize(expr)
                : Model.AddMaximize(expr);
        }

        /// <summary>就地改 CPLEX 變數的界限；null 表示該側不動。已建好的模型可直接改，不需重建。</summary>
        protected override void SetVariableBounds(INumVar variable, double? lb, double? ub)
        {
            if (lb.HasValue) variable.LB = lb.Value;
            if (ub.HasValue) variable.UB = ub.Value;
        }

        /// <summary>
        /// 送出 MIP start；effort 取 <see cref="MipStartEffort"/>。CPLEX 以 name 區分多組 start，null 交由 CPLEX 自動命名。
        /// </summary>
        protected override void AddMIPStartCore(IReadOnlyList<(INumVar var, double value)> entries, string name)
        {
            var vars = new INumVar[entries.Count];
            var vals = new double[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                vars[i] = entries[i].var;
                vals[i] = entries[i].value;
            }
            if (name == null) Model.AddMIPStart(vars, vals, MipStartEffort);
            else Model.AddMIPStart(vars, vals, MipStartEffort, name);
        }

        /// <summary>讀取 CPLEX 實際採用的模型規模與是否含離散結構。</summary>
        protected override (int Continuous, int Integer, int Binary, bool HasDiscreteStructure) ReadModelComposition()
        {
            if (Model == null) return (0, 0, 0, false);
            int binary = Model.NbinVars;
            int integer = Model.NintVars;
            return (Math.Max(0, Model.Ncols - binary - integer), integer, binary, Model.IsMIP());
        }

        private static Core.ObjectiveSense ToCoreSense(ILOG.Concert.ObjectiveSense sense)
            => sense == ILOG.Concert.ObjectiveSense.Maximize ? Core.ObjectiveSense.Maximize : Core.ObjectiveSense.Minimize;

        #endregion

        #region ISolverEngine 實作

        /// <summary>
        /// 初始化 CPLEX 模型並套用 Config 參數。
        /// </summary>
        protected override void BuildCore() => LoadConfig(SolverConfig);

        /// <summary>
        /// 求解並保存狀態與統計；依設定匯出模型、解與軌跡，Infeasible 時自動分析 IIS。
        /// </summary>
        /// <returns>true = Optimal 或 Feasible。逾時但有可行解也算 true；逾時無解為 TimeLimit → false。</returns>
        /// <exception cref="System.Exception">CPLEX 求解丟出的例外會照原樣 rethrow（先寫「CPLEX 求解失敗」log）。</exception>
        protected override bool SolveCore()
        {
            string proj = _modelName ?? "Model";

            if (_exportLp)
                Model.ExportModel(FolderDir.Model.GetPathFile($"{proj}_LP_{_startTime}.lp"));

            if (_exportMps)
                Model.ExportModel(FolderDir.Model.GetPathFile($"{proj}_MPS_{_startTime}.mps"));

            TrajectoryCallback trajCb = null;
            if (_captureTrajectory)
            {
                trajCb = new TrajectoryCallback();
                Model.Use(trajCb); // 會改變搜尋路徑、通常變慢，所以只有明確開啟軌跡記錄時才掛
            }

            double solveStart = Model.GetCplexTime();
            double solveSeconds = 0;
            try
            {
                Model.Solve();
            }
            catch (System.Exception ex)
            {
                Logging.ErrorOnce(
                    ex, "CPLEX 求解失敗", null, nameof(SolveCore), "CPLEX",
                    ex.GetBaseException().Message, $"例外型別={ex.GetType().FullName}");
                throw;
            }
            finally
            {
                solveSeconds = Model.GetCplexTime() - solveStart;
                if (trajCb != null) Model.ClearCallbacks();
                FlushSolverLog();
            }

            var s = Model.GetStatus();
            if (s == ILOG.CPLEX.Cplex.Status.Optimal) Status = SolveStatus.Optimal;
            else if (s == ILOG.CPLEX.Cplex.Status.Feasible) Status = SolveStatus.Feasible;
            else if (s == ILOG.CPLEX.Cplex.Status.Infeasible ||
                     s == ILOG.CPLEX.Cplex.Status.InfeasibleOrUnbounded) Status = SolveStatus.Infeasible;
            else if (s == ILOG.CPLEX.Cplex.Status.Unbounded) Status = SolveStatus.Unbounded;
            else if (s == ILOG.CPLEX.Cplex.Status.Unknown) Status = SolveStatus.TimeLimit;
            else Status = SolveStatus.Error;

            bool ok = Status == SolveStatus.Optimal || Status == SolveStatus.Feasible;

            if (ok)
                ReadBoundAndGap();

            // 模型結構一律向 CPLEX 模型取值：沒被引用的變數 CPLEX 不收，匯入模型也只有 CPLEX 知道全貌
            var composition = ReadModelComposition();
            IObjective objective = Model.GetObjective();

            LastMetrics = new SolveMetrics
            {
                Status = Status,
                ObjectiveValue = ok ? Model.GetObjValue() : double.NaN,
                BestBound = ok ? BestObjValue : double.NaN,
                Gap = ok ? MIPGap : double.NaN,
                SolveTimeMs = solveSeconds * 1000.0,
                // 不同 CPLEX 版本可能提供屬性或方法；先找 get_Xxx，再試 GetXxx。
                NodeCount = TryInvokeLong(Model, "get_Nnodes64", "get_Nnodes", "GetNnodes64", "GetNnodes"),
                IterationCount = TryInvokeLong(Model, "get_Niterations64", "get_Niterations", "GetNiterations64", "GetNiterations"),
                Seed = ReadRandomSeed(),
                ModelType = ModelType,
                ObjectiveSense = objective == null ? (Core.ObjectiveSense?)null : ToCoreSense(objective.Sense),
                VarCount = Model.Ncols,
                BinaryVarCount = composition.Binary,
                IntegerVarCount = composition.Integer,
                ContinuousVarCount = composition.Continuous - Model.NsemiContVars - Model.NsemiIntVars,
                SemiContinuousVarCount = Model.NsemiContVars,
                SemiIntegerVarCount = Model.NsemiIntVars,
                ConstraintCount = Model.Nrows,
                QuadraticConstraintCount = Model.NQCs,
                IndicatorConstraintCount = Model.Nindicators,
                SosCount = Model.NSOSs,
                LazyConstraintCount = Model.NLCs,
                UserCutCount = Model.NUCs,
                TrajectoryEnabled = trajCb != null,
                Convergence = trajCb != null ? trajCb.Points : new List<ConvergencePoint>()
            };

            if (ok && _exportSol)
                Model.WriteSolution(FolderDir.Solution.GetPathFile($"{proj}_Solution_{_startTime}.sol"));

            if (Status == SolveStatus.Infeasible && _constraints.Count > 0)
                _conflictConstraints = RunConflictAnalysis();

            if (ok)
                Logging.Info($"[求解完成] 狀態={Status} 目標值={Model.GetObjValue()} BestBound={BestObjValue} MIPGap={MIPGap}");
            else
                Logging.Info($"[求解完成] 狀態={Status}");

            return ok;
        }

        // 沒明設 Seed 時 CPLEX 用自己的預設種子；直接問 CPLEX，實驗紀錄才寫得出這次實際用的種子。
        private int? ReadRandomSeed()
        {
            try
            {
                return Model.GetParam(Param.RandomSeed);
            }
            catch (System.Exception ex)
            {
                Logging.Warn($"[CPLEX 種子取得失敗] 實驗紀錄的種子欄將寫 n/a | 原因={ex.GetBaseException().Message} 結果=繼續");
                return null;
            }
        }

        // BestBound / MIP gap 只對 MIP 有意義：LP 不向 CPLEX 讀 gap（GetMIPRelativeGap 限 MIP），bound 即目標值、gap = 0。
        // 只在有解時呼叫——Infeasible / Unbounded 時 CPLEX 讀值會 throw。
        private void ReadBoundAndGap()
        {
            if (Model.IsMIP())
            {
                BestObjValue = Model.GetBestObjValue();
                MIPGap = Model.GetMIPRelativeGap();
                Logging.Info($"[Bound And Gap] 模型類型={ModelType} BestBound={BestObjValue} MIPGap={MIPGap}");
                return;
            }

            BestObjValue = Model.GetObjValue();
            MIPGap = 0;
            Logging.Info($"[Bound And Gap] 模型類型={ModelType} BestBound={BestObjValue} MIPGap=NA 原因=非MILP");
        }

        /// <summary>
        /// 將 CPLEX solver log 從 MemoryStream 寫入 framework log。
        /// EnableSolverLog 只控制 Console；檔案診斷資料一律保留。
        /// </summary>
        private void FlushSolverLog()
        {
            if (_solverLogWriter == null || _solverLogStream == null) return;

            _solverLogWriter.Flush();
            _solverLogStream.Position = 0;

            string log = System.Text.Encoding.UTF8.GetString(
                _solverLogStream.GetBuffer(), 0, (int)_solverLogStream.Length);
            if (!string.IsNullOrWhiteSpace(log))
                Logging.WriteToFile($"[CPLEX 求解日誌]{Environment.NewLine}{log}");
            _solverLogStream.SetLength(0);
            _solverLogStream.Position = 0;
        }

        /// <summary>取得目標值；必須先讓 Solve() 回傳 true，沒有可用解時 CPLEX 會拋出例外。</summary>
        public override double GetObjectiveValue()
            => ReadSolverValue(nameof(GetObjectiveValue), _modelName, () => Model.GetObjValue());

        /// <summary>依變數全名取解值。名稱不存在會 KeyNotFoundException；未求解會由 CPLEX 丟例外。</summary>
        public override double GetVariableValue(string name)
            => ReadSolverValue(nameof(GetVariableValue), name, () => Model.GetValue(Variables[name]));

        private static double ReadSolverValue(string context, object value, Func<double> read)
        {
            try
            {
                return read();
            }
            catch (System.Exception ex)
            {
                Logging.ErrorOnce(ex, "取得解失敗", null, context, value,
                    ex.GetBaseException().Message);
                throw;
            }
        }

        /// <summary>結束 CPLEX 模型（釋放 native 記憶體）並關閉 solver log 的暫存串流。Dispose 後本引擎不可再用。</summary>
        public override void Dispose()
        {
            try
            {
                Model?.End();
                Model = null;
                _solverLogWriter?.Dispose();
                _solverLogStream?.Dispose();
            }
            catch (System.Exception ex)
            {
                Logging.ErrorOnce(ex, "CPLEX 釋放失敗", null, nameof(Dispose), "CPLEX",
                    ex.GetBaseException().Message);
                throw;
            }
        }

        #endregion

        #region 便捷方法（公開給子類別）

        // 子類別的直接建模 API；不使用 AddLHS / AddRHS 暫存項目。

        /// <summary>建立單一變數的簡寫。預設為 [0, <see cref="OptBounds.Infinity"/>] 的連續變數。</summary>
        protected INumVar CreateVar(string name, double lb = 0, double ub = OptBounds.Infinity,
            VarType type = VarType.Continuous)
        {
            var variable = AddVariable(name, lb, ub, type);
            RecordDirectVariable(name);
            return variable;
        }

        /// <summary>組線性表達式的簡寫。</summary>
        protected ILinearNumExpr Expr(IEnumerable<(double coef, INumVar var)> terms)
            => LinearExpr(terms);

        /// <summary>用傳入的表達式建立 lhs ≤ rhs 限制式，不讀取 AddLHS / AddRHS 暫存項目。</summary>
        protected IRange AddLE(string name, ILinearNumExpr lhs, double rhs)
            => AddDirectConstraint(name, lhs, ConstraintSense.LessEqual, rhs);

        /// <summary>用傳入的表達式建立 lhs ≥ rhs 限制式，不讀取 AddLHS / AddRHS 暫存項目。</summary>
        protected IRange AddGE(string name, ILinearNumExpr lhs, double rhs)
            => AddDirectConstraint(name, lhs, ConstraintSense.GreaterEqual, rhs);

        /// <summary>用傳入的表達式建立 lhs = rhs 限制式，不讀取 AddLHS / AddRHS 暫存項目。</summary>
        protected IRange AddEQ(string name, ILinearNumExpr lhs, double rhs)
            => AddDirectConstraint(name, lhs, ConstraintSense.Equal, rhs);

        /// <summary>設定最小化目標式（覆寫既有目標式）。</summary>
        protected void Minimize(ILinearNumExpr expr) => SetDirectObjective(expr, Core.ObjectiveSense.Minimize);

        /// <summary>設定最大化目標式（覆寫既有目標式）。</summary>
        protected void Maximize(ILinearNumExpr expr) => SetDirectObjective(expr, Core.ObjectiveSense.Maximize);

        private IRange AddDirectConstraint(string name, ILinearNumExpr lhs, ConstraintSense sense, double rhs)
        {
            var range = AddConstraint(name, lhs, sense, rhs);
            RecordDirectConstraint(name);
            return range;
        }

        private void SetDirectObjective(ILinearNumExpr expr, Core.ObjectiveSense sense)
        {
            SetObjective(expr, 0, sense);
            RecordDirectObjective(sense);
        }

        #endregion

        #region 限制式重置

        /// <summary>
        /// 清空目標式與所有限制式，保留變數，供下一輪 Benders 迭代使用。
        /// </summary>
        public void ResetConstraint()
        {
            var obj = Model.GetObjective();
            if (obj != null)
            {
                Model.End(obj);
                Model.Remove(obj);
            }
            _objective = null;
            ResetObjectiveTracking();
            ResetReferencedVariables();
            if (_constraints.Count > 0)
            {
                var arr = _constraints.ToArray();
                Model.End(arr);
                Model.Remove(arr);
                _constraints.Clear();
            }
            _threadVerifyConstraints.Clear();
            _threadConstraints.Clear();
            _threadRuleCount = 0;
            ClearPool();
            _conflictConstraints = null;
            ResetVerifyConstraints();
        }

        #endregion

        #region 多線程限制式同步

        private bool CreateThreadConstraint(
            double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine,
            Func<double, ILinearNumExpr, IRange> factory)
        {
            var targetTerms = targetEngine.PoolLhsTerms.ToList();
            if (targetTerms.Count == 0) return false;

            string verify = string.Join(",", targetTerms.OrderBy(t => t.var.Name).Select(t => t.var.Name));
            if (!_threadVerifyConstraints.Contains(verify))
            {
                var expr = targetEngine.Model.LinearNumExpr();
                foreach (var (coef, v) in targetTerms)
                    expr.AddTerm(coef, v);
                var c = factory(value, expr);  // Le/Ge/Eq — 未加入任何 model，存入 _threadConstraints
                c.Name = $"{ruleName}_{_threadRuleCount}";
                _threadConstraints.Add(c);
                _threadVerifyConstraints.Add(verify);
                _threadRuleCount++;
            }

            targetEngine.ClearPool();
            return true;
        }

        /// <summary>跨模型建立 &gt;= 限制式（value ≤ Σlhs）。使用 targetEngine 暫存的左側項目，完成後清空其暫存項目。</summary>
        public bool CreateGreaterEqualThread(double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine)
            => CreateThreadConstraint(value, ruleName, targetEngine, sourceEngine,
                (v, expr) => sourceEngine.Model.Le(v, expr));

        /// <summary>跨模型建立 &lt;= 限制式（Σlhs ≤ value）。使用 targetEngine 暫存的左側項目，完成後清空其暫存項目。</summary>
        public bool CreateLessEqualThread(double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine)
            => CreateThreadConstraint(value, ruleName, targetEngine, sourceEngine,
                (v, expr) => sourceEngine.Model.Ge(v, expr));

        /// <summary>跨模型建立 = 限制式（Σlhs = value）。使用 targetEngine 暫存的左側項目，完成後清空其暫存項目。</summary>
        public bool CreateEqualThread(double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine)
            => CreateThreadConstraint(value, ruleName, targetEngine, sourceEngine,
                (v, expr) => sourceEngine.Model.Eq(v, expr));

        /// <summary>
        /// 從 this 的 CPLEX 模型中移除兩個子引擎的 thread 限制式，並清空各自的 _threadConstraints。
        /// 用於下一輪 Benders 迭代前的清理。
        /// </summary>
        public void ResetThreadConstraint(OptEngine threadEngine1, OptEngine threadEngine2)
        {
            RemoveThreadConstraints(threadEngine1);
            RemoveThreadConstraints(threadEngine2);
            _threadConstraints.Clear();
        }

        private void RemoveThreadConstraints(OptEngine engine)
        {
            if (engine._threadConstraints.Count == 0) return;
            var arr = engine._threadConstraints.ToArray();
            Model.End(arr);
            Model.Remove(arr);
            engine._threadConstraints.Clear();
        }

        #endregion

        #region 模型複製與合併

        /// <summary>
        /// 複製 sourceEngine 的目標式與第一個變數至新的 OptEngine 實例。
        /// 用於 Benders 平行求解的初始模型分發。
        /// </summary>
        public OptEngine CopyModel(OptEngine sourceEngine)
        {
            var targetEngine = new OptEngine();
            targetEngine.LoadConfig(targetEngine.SolverConfig);

            var cloneManager = new SimpleCloneManager(sourceEngine.Model);

            var sourceObj = sourceEngine.Model.GetObjective();
            if (sourceObj != null)
            {
                var objective = (IObjective)sourceObj.MakeClone(cloneManager);
                targetEngine.Model.Add(objective);
            }

            // 僅複製 Variables 的第一個變數。
            if (sourceEngine.Variables.Count > 0)
            {
                var firstVar = sourceEngine.Variables.Values.First();
                var temp = (INumVar)firstVar.MakeClone(cloneManager);
                targetEngine.Model.Add(temp);
            }

            return targetEngine;
        }

        /// <summary>
        /// 將 sourceEngine 透過 CreateXxxThread 建立的限制式加入 targetEngine 模型。
        /// 用於 Benders 子問題 cut 合併回主問題。
        /// </summary>
        public OptEngine MergeModel(OptEngine sourceEngine, OptEngine targetEngine)
        {
            // 只合併 _threadConstraints（用 Le/Ge/Eq 建立、尚未屬於任何 model）
            // _constraints 的元素已在 sourceEngine.Model 內，不能跨 model 使用
            if (sourceEngine._threadConstraints.Count > 0)
                targetEngine.Model.Add(sourceEngine._threadConstraints.ToArray());
            return targetEngine;
        }

        /// <summary>
        /// 將指定的變數集合加入 targetEngine 的 CPLEX 模型。
        /// </summary>
        public OptEngine MergeVariables(OptEngine targetEngine, HashSet<INumVar> variables)
        {
            foreach (var variable in variables)
                targetEngine.Model.Add(variable);
            return targetEngine;
        }

        #endregion

        #region IIS 衝突分析
        private List<string> RunConflictAnalysis()
        {
            var constraintArr = _constraints.ToArray();
            // 所有限制式的偏好值都設為 1.0，讓 CPLEX 搜尋衝突時不特別偏好任何一條。
            var prefs = Enumerable.Repeat(1.0, constraintArr.Length).ToArray();
            var conflictNames = new List<string>();

            if (!Model.RefineConflict(constraintArr, prefs))
            {
                return conflictNames;
            }

            if (_exportIIs)
            {
                FolderDir.IIS.CreateFolder();  // 即使未設定 exportLP/Sol，IIS 資料夾也必須存在才能寫入
                string iisPath = FolderDir.IIS.GetPathFile($"{this._modelName}_IIS_{_startTime}.ilp");
                Model.WriteConflict(iisPath);
                Logging.Info($"[衝突檔匯出完成] 路徑={iisPath}");
            }

            var statuses = Model.GetConflict(constraintArr);
            for (int i = 0; i < constraintArr.Length; i++)
            {
                if (statuses[i] == ConflictStatus.Member ||
                    statuses[i] == ConflictStatus.PossibleMember)
                {
                    if (!string.IsNullOrEmpty(constraintArr[i].Name))
                        conflictNames.Add(constraintArr[i].Name);
                }
            }

            if (conflictNames.Count > 0)
                Logging.Info($"[衝突限制式摘要] 數量={conflictNames.Count} 名稱={string.Join("|", conflictNames)}");

            return conflictNames;
        }

        /// <summary>
        /// 回傳 RefineConflict 識別出的衝突限制式名稱清單。
        /// Solve() 遇到 Infeasible 時會自動執行並快取結果；手動呼叫也可觸發。
        /// </summary>
        public List<string> GetConflictConstraints()
        {
            if (_conflictConstraints != null) return _conflictConstraints; // Solve() 已執行過則直接回傳，RefineConflict 很耗時不重跑
            if (Status != SolveStatus.Infeasible || _constraints.Count == 0)
                return new List<string>();
            _conflictConstraints = RunConflictAnalysis();
            return _conflictConstraints;
        }

        #endregion

        #region 分類型別解答

        // 用 Model.GetValues(INumVar[]) 一次讀取同類型變數的解值，減少逐筆呼叫 CPLEX 的成本。
        private IReadOnlyDictionary<string, double> GetSolutionByType(NumVarType varType)
        {
            var matching = Variables.Where(kv => kv.Value.Type == varType).ToList();
            if (matching.Count == 0) return new Dictionary<string, double>();
            double[] values = Model.GetValues(matching.Select(kv => kv.Value).ToArray());
            return matching.Select((kv, i) => (kv.Key, values[i]))
                           .ToDictionary(t => t.Key, t => t.Item2);
        }

        /// <summary>取出所有連續變數（Float）的解值。</summary>
        public IReadOnlyDictionary<string, double> GetCVSolution() => GetSolutionByType(NumVarType.Float);

        /// <summary>取出所有整數變數（Int）的解值。</summary>
        public IReadOnlyDictionary<string, double> GetIVSolution() => GetSolutionByType(NumVarType.Int);

        /// <summary>取出所有二元變數（Bool）的解值。</summary>
        public IReadOnlyDictionary<string, double> GetBVSolution() => GetSolutionByType(NumVarType.Bool);

        #endregion

        // 軟性限制式由 EngineBase 建立；更新懲罰項時，再透過本類別的 SetObjective 更新 CPLEX 目標式。

        /// <summary>
        /// 同步輸出至 Console 與記憶體，供求解後寫入 log 檔。
        /// </summary>
        private sealed class TeeWriter : TextWriter
        {
            private readonly TextWriter _primary;   // Console.Out（即時顯示）
            private readonly TextWriter _secondary; // StreamWriter → MemoryStream（捕捉）

            /// <summary>保留 primary（Console.Out），只負責釋放 secondary。</summary>
            public TeeWriter(TextWriter primary, TextWriter secondary)
            {
                _primary = primary;
                _secondary = secondary;
            }

            public override System.Text.Encoding Encoding => _primary.Encoding;

            public override void Write(char value)
            {
                _primary.Write(value);
                _secondary.Write(value);
            }

            public override void Write(string value)
            {
                _primary.Write(value);
                _secondary.Write(value);
            }

            public override void WriteLine(string value)
            {
                _primary.WriteLine(value);
                _secondary.WriteLine(value);
            }

            public override void Flush()
            {
                _primary.Flush();
                _secondary.Flush();
            }

            /// <summary>只釋放 secondary，避免關閉整個 process 共用的 Console.Out。</summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing) _secondary?.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
