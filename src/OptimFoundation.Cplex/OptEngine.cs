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
    /// CPLEX 求解器引擎。
    /// 使用方式：繼承本類別並覆寫 Build()，在 Build() 內呼叫 base.Build() 初始化模型，
    /// 再呼叫 AddVariable / LinearExpr / AddConstraint / SetObjective 定義模型。
    /// 批次建立變數使用 BuildCVs / BuildIVs / BuildBVs，透過 ReadVar 存取。
    /// </summary>
    public class OptEngine : EngineBase<ILOG.CPLEX.Cplex, INumVar, ILinearNumExpr, IRange>
    {
        private string _modelName;
        private bool _exportLp;
        private bool _exportMps;
        private bool _exportSol;
        private bool _enableLog;
        private readonly ProjectConfig _projectConfig;
        private readonly List<IRange> _constraints = new List<IRange>();
        private readonly string _startTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        private MemoryStream _solverLogStream;
        private StreamWriter _solverLogWriter;

        // base 的 _verifyConstraints 管單次 Build 內的正常約束去重；這裡管跨 engine 的 thread 約束去重，生命週期不同
        private readonly HashSet<string> _threadVerifyConstraints = new HashSet<string>();
        // Thread constraints：由 CreateXxxThread 建立，尚未屬於任何 model（用 Le/Ge/Eq 而非 AddLe/AddGe/AddEq）
        // 可被 MergeModel 加入另一個 model，或被 ResetThreadConstraint 從 master model 移除
        private int _threadRuleCount = 0;
        private readonly List<IRange> _threadConstraints = new List<IRange>();
        private List<string> _conflictConstraints = null;

        /// <summary>以指定組態建立引擎。此時還沒碰 CPLEX，模型物件要等 Build() 才生出來。</summary>
        public OptEngine(CplexConfig config) : this(config, new ProjectConfig()) { }

        /// <summary>以指定求解器組態與專案組態建立引擎。此時還沒碰 CPLEX，模型物件要等 Build() 才生出來。</summary>
        public OptEngine(CplexConfig config, ProjectConfig project) : base(config)
        {
            _projectConfig = project;
        }

        /// <summary>以 CplexConfig 預設值建立引擎（32 threads、2GB WorkMem、MIPGap 1e-4、無時限）。</summary>
        public OptEngine() : this(new CplexConfig(), new ProjectConfig()) { }

        /// <summary>本引擎已建立的限制式條數（不含尚未併入 model 的 thread 約束）。</summary>
        public override int ConstraintCount => _constraints.Count;

        /// <summary>模型名稱（`SetModelName` 設定）——LP / MPS / Sol / IIS 輸出檔以它命名。未設定時輸出端用 "Model"。</summary>
        public string ModelName => _modelName;

        /// <summary>本引擎建立的時刻（`yyyy-MM-dd_HH-mm-ss`）——輸出檔名的時間戳來源。</summary>
        public string StartTime => _startTime;

        /// <summary>尚未併入任何 model 的 thread 約束數（`CreateXxxThread` 建立、可被 `MergeModel` 併入）。</summary>
        public int ThreadConstraintCount => _threadConstraints.Count;

        #region ITrajectorySource（CPLEX 支援逐點軌跡）
        private bool _captureTrajectory;
        /// <summary>CPLEX 以 MIPInfoCallback 支援收斂軌跡，恆為 true。</summary>
        public override bool SupportsTrajectory => true;

        /// <summary>
        /// 開啟收斂軌跡記錄，MUST 在 Solve() 之前呼叫。
        /// 代價：掛 callback 會讓 CPLEX 關閉 dynamic search，求解時間可能變長，所以設計成 opt-in。
        /// </summary>
        public override void EnableTrajectory() => _captureTrajectory = true;

        /// <summary>
        /// MIPInfoCallback：B&amp;B 過程中週期觸發，收集收斂軌跡。
        /// 取樣：incumbent 改善 或 距上點 ≥ MinIntervalMs 即記一點；上限 MaxPoints 防爆；多執行緒求解以 lock 保護。
        /// </summary>
        private sealed class TrajectoryCallback : ILOG.CPLEX.Cplex.MIPInfoCallback
        {
            private const double MinIntervalMs = 200.0;
            private const int MaxPoints = 2000;

            private readonly System.Diagnostics.Stopwatch _sw;
            private readonly object _lock = new object();
            /// <summary>收集到的軌跡點，求解結束後由 SolveCore 取走塞進 LastMetrics.Convergence。</summary>
            public readonly List<ConvergencePoint> Points = new List<ConvergencePoint>();
            private double _lastObj = double.NaN;
            private double _lastTimeMs = double.NegativeInfinity;

            /// <summary>用 SolveCore 的計時器當時間軸，讓軌跡的 TimeMs 與求解耗時同一個原點。</summary>
            public TrajectoryCallback(System.Diagnostics.Stopwatch sw) => _sw = sw;

            /// <summary>CPLEX 在 B&amp;B 過程中週期回呼；符合取樣條件就記一點。CPLEX 可能多執行緒呼叫，故整段 lock。</summary>
            public override void Main()
            {
                double nowMs = _sw.Elapsed.TotalMilliseconds;
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
                        TimeMs = nowMs,
                        Objective = inc,
                        Bound = bound,
                        Gap = gap
                    });
                    _lastObj = inc;
                    _lastTimeMs = nowMs;
                }
            }
        }
        #endregion

        #region Configuration

        /// <summary>
        /// 建立 CPLEX 模型物件並逐項套用組態，每套用一項就寫一行 [Solver Setting] log。
        /// 重複呼叫等同重新開一個空模型：限制式清單、目標式、conflict 結果、限制式名稱去重集合全部重置（變數索引不動）。
        /// </summary>
        /// <param name="cfg">MUST 為 <see cref="CplexConfig"/>，傳其他型別會在後續存取時 NullReferenceException。</param>
        public override void Configuration(ISolverConfig cfg)
        {

            Model = new ILOG.CPLEX.Cplex();
            _constraints.Clear();
            _objective = null;
            _conflictConstraints = null;
            ResetVerifyConstraints();

            CplexConfig config = cfg as CplexConfig;

            // Solver log 路由：無論是否顯示 Console，都會捕捉並保存到 framework log。
            //   EnableSolverLog = true  → Console + framework log
            //   EnableSolverLog = false → framework log only
            _solverLogStream?.Dispose();
            _solverLogWriter?.Dispose();
            _solverLogStream = new MemoryStream();
            _solverLogWriter = new StreamWriter(_solverLogStream) { AutoFlush = true };

            if (_projectConfig.EnableSolverLog)
            {
                _enableLog = true;
                // TeeWriter：即時寫 Console + 同時捕捉到 MemoryStream（供事後存 log 檔）
                var tee = new TeeWriter(Console.Out, _solverLogWriter);
                Model.SetOut(tee);
                Model.SetWarning(tee);
            }
            else
            {
                // 不顯示在 Console，但保留診斷資料
                Model.SetOut(_solverLogWriter);
                Model.SetWarning(_solverLogWriter);
            }

            #region ProjectConfig — 輸出與 log 路由（與「solver 怎麼解」無關，先印成獨立一群）
            Logging.Info(_enableLog
                ? "[Project Setting] CPLEX Log → Console (real-time)"
                : "[Project Setting] CPLEX Log → framework log file only");

            if (_projectConfig.ExportLP)
            {
                _exportLp = true;
                Logging.Info("[Project Setting] Enabled LP File (.lp) Output");
            }

            if (_projectConfig.ExportMPS)
            {
                _exportMps = true;
                Logging.Info("[Project Setting] Enabled Model File (.mps) Output");
            }

            if (_projectConfig.ExportSol)
            {
                _exportSol = true;
                Logging.Info("[Project Setting] Enabled Solution File (.sol) Output");
            }
            #endregion

            #region 設定執行緒上限
            // CPLEX求解設定 - 工作執行緒上限 (預設: 32)
            if (config.Threads.HasValue)
            {
                Model.SetParam(Param.Threads, config.Threads.Value);
                Logging.Info($"[Solver Setting] Threads={config.Threads.Value}");
            }
            #endregion

            #region 設定限制式上限
            // CPLEX求解設定 - 限制式上限 (預設: 30,000)
            if (config.RowRead.HasValue)
            {
                Model.SetParam(Param.Read.Constraints, config.RowRead.Value);
                Logging.Info($"[Solver Setting] RowReadLim={config.RowRead.Value}");
            }
            #endregion

            #region 設定工作記憶體上限
            // CPLEX求解設定 - 工作記憶體上限: 2 GB (預設)
            // (double)(workMemory.Value / 1024), 1) GB
            if (config.MemoryLimitMb.HasValue)
            {
                Model.SetParam(IntParam.WorkMem, config.MemoryLimitMb.Value);
                Model.SetParam(Param.MIP.Strategy.File, 0);
                Logging.Info($"[Solver Setting] WorkMem={config.MemoryLimitMb.Value} MB (NodeFileInd=0)");
            }
            #endregion

            #region 設定求解下限
            // CPLEX求解設定 - 求解下限: epGap.Value * 100 % (預設: 1e-4 %)
            if (config.MipGap.HasValue)
            {
                Model.SetParam(Param.MIP.Tolerances.MIPGap, config.MipGap.Value);
                Logging.Info($"[Solver Setting] EpGap={config.MipGap.Value}");
            }
            #endregion

            #region node 選擇策略
            // CPLEX求解設定 - node選擇策略: (預設)
            if (config.NodeSelect.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.NodeSelect, config.NodeSelect.Value);

                string nodeSelectDescription = config.NodeSelect.Value switch
                {
                    0 => "深度優先搜尋",
                    2 => "最佳估計值搜尋",
                    3 => "交替最佳估計值搜尋",
                    _ => "最佳界限搜尋 (預設)"
                };
                Logging.Info($"[Solver Setting] NodeSelect={config.NodeSelect.Value} ({nodeSelectDescription})");
            }
            #endregion

            #region 設定 Random Seed
            // CPLEX求解設定 - 隨機種子 (預設: 0)
            if (config.Seed.HasValue)
            {
                Model.SetParam(Param.RandomSeed, config.Seed.Value);
                Logging.Info($"[Solver Setting] RandomSeed={config.Seed.Value}");
            }
            #endregion

            if (_exportLp || _exportSol)
            {
                FolderDir.Model.CreateFolder();
                FolderDir.Sol.CreateFolder();
                FolderDir.IIS.CreateFolder();
            }
            if (_exportMps)
                FolderDir.Model.CreateFolder();

            #region 設定容忍區間(Optimality tolerance)
            // "CPLEX求解設定 - Optimality tolerance (預設: 1e-06 )
            if (config.OptimalityTol.HasValue)
            {
                Model.SetParam(Param.Simplex.Tolerances.Optimality, config.OptimalityTol.Value);
                Logging.Info($"[Solver Setting] EpOpt={config.OptimalityTol.Value}");
            }
            #endregion

            #region 設定容忍區間(Feasibility tolerance)
            // CPLEX求解設定 - Feasibility tolerance (預設: 1e-06)
            if (config.FeasibilityTol.HasValue)
            {
                Model.SetParam(Param.Simplex.Tolerances.Feasibility, config.FeasibilityTol.Value);
                Logging.Info($"[Solver Setting] EpRHS={config.FeasibilityTol.Value}");
            }
            #endregion

            #region 設定逾時秒數
            // CPLEX求解設定 - 逾時秒數: (預設: 無限制)
            if (config.TimeLimit.HasValue)
            {
                Model.SetParam(Param.TimeLimit, config.TimeLimit.Value);
                Logging.Info($"[Solver Setting] TiLim={config.TimeLimit.Value} 秒");
            }
            #endregion

            #region 設定 Solution Polishing 秒數
            // CPLEX求解設定 - Solution Polishing秒數: (預設: 無)
            if (config.PolishAfterTime.HasValue)
            {
                Model.SetParam(Param.MIP.PolishAfter.Time, config.PolishAfterTime.Value);
                Logging.Info($"[Solver Setting] PolishAfterTime={config.PolishAfterTime.Value} 秒");
            }
            #endregion

            #region 設定解析模式
            // CPLEX求解設定 - 解析模式: 平衡最佳可行解 (預設)
            if (config.Emphasis.HasValue)
            {
                Model.SetParam(Param.Emphasis.MIP, config.Emphasis.Value);

                string mipEmphasisDescription = config.Emphasis.Value switch
                {
                    1 => "強調可行解優於最佳解",
                    2 => "強調最佳解優於可行解",
                    3 => "強調路徑的最佳解",
                    4 => "強調尋找隱藏可行解",
                    _ => "平衡最佳可行解 (預設)"
                };
                Logging.Info($"[Solver Setting] MIPEmphasis={config.Emphasis.Value} ({mipEmphasisDescription})");
            }
            #endregion

            #region 設定分支模式
            // CPLEX求解設定 - 分支模式: 自動選擇變數分支 (預設)
            if (config.VariableSelect.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.VariableSelect, config.VariableSelect.Value);

                string varSelDescription = config.VariableSelect.Value switch
                {
                    -1 => "以最小可行解選擇變數分支",
                    1 => "以最大可行解選擇變數分支",
                    2 => "以假定成本選擇分支",
                    3 => "強分支",
                    4 => "以假定降低成本選擇分支",
                    _ => "自動選擇變數分支 (預設)"
                };
                Logging.Info($"[Solver Setting] VarSel={config.VariableSelect.Value} ({varSelDescription})");
            }
            #endregion

            #region 設定演算法
            // CPLEX求解設定 - 演算法: 自動選擇 (預設)
            if (config.RootAlgorithm.HasValue)
            {
                Model.SetParam(IntParam.RootAlgorithm, config.RootAlgorithm.Value);

                string algorithmDescription = string.Empty;
                switch (config.RootAlgorithm.Value)
                {
                    case 1:
                        algorithmDescription = "基本演算法";
                        break;
                    case 2:
                        algorithmDescription = "對偶演算法";
                        break;
                    case 3:
                        algorithmDescription = "網路演算法";
                        break;
                    case 4:
                        algorithmDescription = "屏障演算法";
                        break;
                    case 5:
                        algorithmDescription = "過濾演算法";
                        break;
                    case 6:
                        algorithmDescription = "混合式演算法";
                        break;
                    default:
                        algorithmDescription = "自動選擇 (預設)";
                        break;
                }
                Logging.Info($"[Solver Setting] RootAlgorithm={config.RootAlgorithm.Value} ({algorithmDescription})");
            }
            #endregion

            #region 設定節點資訊儲存模式
            // CPLEX求解設定 - 節點資訊: 節點資訊壓縮存放於記憶體 (預設)
            if (config.NodeFileStrategy.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.File, config.NodeFileStrategy.Value);

                string nodeFileIndDescription = string.Empty;
                switch (config.NodeFileStrategy.Value)
                {
                    case 0:
                        nodeFileIndDescription = "不儲存節點資訊";
                        break;
                    case 2:
                        nodeFileIndDescription = "節點資訊存放於磁碟機";
                        break;
                    case 3:
                        nodeFileIndDescription = "節點資訊壓縮存放於磁碟機";
                        break;
                    default:
                        nodeFileIndDescription = "節點資訊壓縮存放於記憶體 (預設)";
                        break;
                }
                Logging.Info($"[Solver Setting] NodeFileInd={config.NodeFileStrategy.Value} ({nodeFileIndDescription})");
            }
            #endregion

            #region 前處理 / Presolve
            // 先前已宣告於 CplexConfig（ITunableConfig.Presolve ↔ PreIndicator）卻未套用 → 此處接線
            if (config.PreIndicator.HasValue)
            {
                Model.SetParam(Param.Preprocessing.Presolve, config.PreIndicator.Value);
                Logging.Info($"[Solver Setting] Presolve={(config.PreIndicator.Value ? "on" : "off")}");
            }

            if (config.Symmetry.HasValue)
            {
                Model.SetParam(Param.Preprocessing.Symmetry, config.Symmetry.Value);
                Logging.Info($"[Solver Setting] Symmetry={config.Symmetry.Value}");
            }
            #endregion

            #region 子問題（節點）演算法
            // 先前已宣告於 CplexConfig（NodeAlgorithm）卻未套用 → 此處接線
            if (config.NodeAlgorithm.HasValue)
            {
                Model.SetParam(IntParam.NodeAlg, config.NodeAlgorithm.Value);  // 此版本無 Param.* 對應，沿用 flat（obsolete warning 可接受）
                Logging.Info($"[Solver Setting] NodeAlg={config.NodeAlgorithm.Value}");
            }
            #endregion

            #region 啟發式投入程度（HeuristicEffort）
            // 先前已宣告於 CplexConfig（ITunableConfig.HeuristicEffort）卻未套用 → 此處接線
            if (config.HeuristicEffort.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.HeuristicEffort, config.HeuristicEffort.Value);
                Logging.Info($"[Solver Setting] HeuristicEffort={config.HeuristicEffort.Value}");
            }
            #endregion

            #region 決定論 / 計時
            // 平行模式：-1 機會式, 0 自動, 1 決定論（實驗可重現首選）
            if (config.ParallelMode.HasValue)
            {
                Model.SetParam(Param.Parallel, config.ParallelMode.Value);
                string parallelDescription = config.ParallelMode.Value switch
                {
                    -1 => "機會式",
                    1 => "決定論",
                    _ => "自動 (預設)"
                };
                Logging.Info($"[Solver Setting] Parallel={config.ParallelMode.Value} ({parallelDescription})");
            }
            // 決定論時間上限（ticks）
            if (config.DeterministicTimeLimit.HasValue)
            {
                Model.SetParam(Param.DetTimeLimit, config.DeterministicTimeLimit.Value);
                Logging.Info($"[Solver Setting] DetTimeLimit={config.DeterministicTimeLimit.Value} ticks");
            }
            // 計時方式：1 CPU, 2 wall-clock
            if (config.ClockType.HasValue)
            {
                Model.SetParam(Param.ClockType, config.ClockType.Value);
                string clockDescription = config.ClockType.Value == 1 ? "CPU 時間" : "wall-clock 時間";
                Logging.Info($"[Solver Setting] ClockType={config.ClockType.Value} ({clockDescription})");
            }
            // 數值穩定優先
            if (config.NumericalEmphasis.HasValue)
            {
                Model.SetParam(Param.Emphasis.Numerical, config.NumericalEmphasis.Value);
                Logging.Info($"[Solver Setting] NumericalEmphasis={config.NumericalEmphasis.Value}");
            }
            #endregion

            #region MIP 容差延伸
            if (config.IntegralityTolerance.HasValue)
            {
                Model.SetParam(Param.MIP.Tolerances.Integrality, config.IntegralityTolerance.Value);
                Logging.Info($"[Solver Setting] Integrality={config.IntegralityTolerance.Value}");
            }
            if (config.AbsoluteMipGap.HasValue)
            {
                Model.SetParam(Param.MIP.Tolerances.AbsMIPGap, config.AbsoluteMipGap.Value);
                Logging.Info($"[Solver Setting] AbsMIPGap={config.AbsoluteMipGap.Value}");
            }
            #endregion

            #region MIP limits 延伸
            if (config.NodeLimit.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.Nodes, config.NodeLimit.Value);
                Logging.Info($"[Solver Setting] Nodes={config.NodeLimit.Value}");
            }
            if (config.TreeMemoryLimitMb.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.TreeMemory, config.TreeMemoryLimitMb.Value);
                Logging.Info($"[Solver Setting] TreeMemory={config.TreeMemoryLimitMb.Value} MB");
            }
            if (config.IntegerSolutionLimit.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.Solutions, config.IntegerSolutionLimit.Value);
                Logging.Info($"[Solver Setting] Solutions={config.IntegerSolutionLimit.Value}");
            }
            #endregion

            #region MIP 搜尋策略延伸
            if (config.Probe.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.Probe, config.Probe.Value);
                Logging.Info($"[Solver Setting] Probe={config.Probe.Value}");
            }
            if (config.RinsHeuristicFrequency.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.RINSHeur, config.RinsHeuristicFrequency.Value);
                Logging.Info($"[Solver Setting] RINSHeur={config.RinsHeuristicFrequency.Value}");
            }
            if (config.MipSearch.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.Search, config.MipSearch.Value);
                Logging.Info($"[Solver Setting] Search={config.MipSearch.Value}");
            }
            if (config.DiveType.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.Dive, config.DiveType.Value);
                Logging.Info($"[Solver Setting] Dive={config.DiveType.Value}");
            }
            if (config.BranchDirection.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.Branch, config.BranchDirection.Value);
                Logging.Info($"[Solver Setting] Branch={config.BranchDirection.Value}");
            }
            #endregion

            #region MIP cuts
            if (config.CutsFactor.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.CutsFactor, config.CutsFactor.Value);
                Logging.Info($"[Solver Setting] CutsFactor={config.CutsFactor.Value}");
            }
            if (config.CutPasses.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.CutPasses, config.CutPasses.Value);
                Logging.Info($"[Solver Setting] CutPasses={config.CutPasses.Value}");
            }
            if (config.GomoryCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.Gomory, config.GomoryCuts.Value);
                Logging.Info($"[Solver Setting] GomoryCuts={config.GomoryCuts.Value}");
            }
            if (config.CoverCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.Covers, config.CoverCuts.Value);
                Logging.Info($"[Solver Setting] CoverCuts={config.CoverCuts.Value}");
            }
            if (config.CliqueCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.Cliques, config.CliqueCuts.Value);
                Logging.Info($"[Solver Setting] CliqueCuts={config.CliqueCuts.Value}");
            }
            if (config.MirCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.MIRCut, config.MirCuts.Value);
                Logging.Info($"[Solver Setting] MIRCuts={config.MirCuts.Value}");
            }
            if (config.FlowCoverCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.FlowCovers, config.FlowCoverCuts.Value);
                Logging.Info($"[Solver Setting] FlowCoverCuts={config.FlowCoverCuts.Value}");
            }
            #endregion

            #region 純 LP（Simplex / Barrier）
            if (config.SimplexIterationLimit.HasValue)
            {
                Model.SetParam(Param.Simplex.Limits.Iterations, config.SimplexIterationLimit.Value);
                Logging.Info($"[Solver Setting] SimplexIterations={config.SimplexIterationLimit.Value}");
            }
            if (config.BarrierAlgorithm.HasValue)
            {
                Model.SetParam(Param.Barrier.Algorithm, config.BarrierAlgorithm.Value);
                Logging.Info($"[Solver Setting] BarrierAlgorithm={config.BarrierAlgorithm.Value}");
            }
            #endregion

        }


        /// <summary>設定模型名稱——LP / MPS / Sol / IIS 輸出檔都以它當檔名前綴。OptModel 會用 projectName 自動代呼叫。</summary>
        /// <exception cref="ArgumentException">name 為 null 或空白。</exception>
        public void SetModelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw Logging.ErrorOnce(
                    new ArgumentException("Model name cannot be null or whitespace.", nameof(name)),
                    "MODEL_NAME_INVALID", "模型名稱驗證失敗", nameof(SetModelName), name, "name_is_empty");
            _modelName = name;
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
            var v = Model.NumVar(lb, ub, cplexType, name);
            Variables[name] = v;
            return v;
        }

        /// <summary>
        /// 批次建立變數：改用 CPLEX 原生 NumVarArray 一次送出，取代逐筆 AddVariable。
        /// 大模型的效能關鍵——.NET ↔ native interop 從 N 次降為 1 次。
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
                    new ArgumentOutOfRangeException(nameof(sense)),
                    "CONSTRAINT_SENSE_INVALID", "限制式方向不合法", nameof(AddConstraint), sense,
                    "unsupported_constraint_sense")
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
        /// 設定目標式（覆寫語意）。
        /// CPLEX 的 AddMinimize / AddMaximize 本身是「新增」語意（重複呼叫會產生多個目標式），
        /// 故這裡先移除既有目標式再新增。如此 base 的軟性 penalty（每加一項就重設一次目標式）才能正確運作。
        /// </summary>
        protected override void SetObjective(ILinearNumExpr expr, Core.ObjectiveSense sense)
        {
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

        #endregion

        #region ISolverEngine 實作

        /// <summary>
        /// 初始化 CPLEX 模型並套用 Config 參數。
        /// 由 EngineBase.Build() 這個 template method 呼叫；消費端 NEVER override Build()（已非 virtual）。
        /// </summary>
        protected override void BuildCore() => Configuration(Config);

        /// <summary>
        /// 執行求解，並把整趟結果收斂成框架的統一狀態。流程：
        /// 依設定匯出 LP / MPS → （選用）掛軌跡 callback → Model.Solve() → CPLEX 狀態轉成 <see cref="SolveStatus"/> →
        /// 回填 BestObjValue / MIPGap / LastMetrics → 有解且設定要匯出就寫 .sol → infeasible 時自動跑 conflict(IIS) 分析。
        /// </summary>
        /// <returns>true = Optimal 或 Feasible。逾時但有可行解也算 true；逾時無解為 TimeLimit → false。</returns>
        /// <exception cref="System.Exception">CPLEX 求解丟出的例外會照原樣 rethrow（先寫 SOLVER_EXCEPTION log）。</exception>
        protected override bool SolveCore()
        {
            string proj = _modelName ?? "Model";

            if (_exportLp)
                Model.ExportModel(FolderDir.Model.GetFilePath($"{proj}_LP_{_startTime}.lp"));

            if (_exportMps)
                Model.ExportModel(FolderDir.Model.GetFilePath($"{proj}_MPS_{_startTime}.mps"));

            var solveTimer = System.Diagnostics.Stopwatch.StartNew();
            TrajectoryCallback trajCb = null;
            if (_captureTrajectory)
            {
                trajCb = new TrajectoryCallback(solveTimer);
                Model.Use(trajCb);   // 注意：掛 callback 會關閉 CPLEX dynamic search，可能影響求解時間（故為 opt-in）
            }
            try
            {
                Model.Solve();
            }
            catch (System.Exception ex)
            {
                Logging.ErrorOnce(
                    ex, "SOLVER_EXCEPTION", "求解器執行失敗", nameof(SolveCore), "CPLEX",
                    ex.GetBaseException().Message, $"exception={ex.GetType().FullName}");
                throw;
            }
            finally
            {
                solveTimer.Stop();
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
            {
                // Infeasible / Unbounded 時 CPLEX 會 throw，只在有解時才讀
                BestObjValue = Model.GetBestObjValue();
                MIPGap = Model.GetMIPRelativeGap();
            }

            LastMetrics = new SolveMetrics
            {
                Status = Status,
                ObjectiveValue = ok ? Model.GetObjValue() : double.NaN,
                BestBound = ok ? BestObjValue : double.NaN,
                MipGap = ok ? MIPGap : double.NaN,
                RunTimeMs = solveTimer.Elapsed.TotalMilliseconds,
                NodeCount = TryInvokeLong(Model, "GetNnodes64", "GetNnodes", "Getnnodes"),
                IterationCount = TryInvokeLong(Model, "GetNiterations64", "GetNiterations", "Getniterations"),
                VarCount = VariableCount,
                ConstraintCount = _constraints.Count,
                Convergence = trajCb != null ? trajCb.Points : new List<ConvergencePoint>()
            };

            if (ok && _exportSol)
                Model.WriteSolution(FolderDir.Sol.GetFilePath($"{proj}_Solution_{_startTime}.sol"));

            if (Status == SolveStatus.Infeasible && _constraints.Count > 0)
                _conflictConstraints = RunConflictAnalysis(proj);

            if (ok)
                Logging.Info($"[OptEngine] Status={Status}  ObjVal={Model.GetObjValue()}  BestBound={BestObjValue}  MIPGap={MIPGap}");
            else
                Logging.Info($"[OptEngine] Status={Status}");

            return ok;
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
                Logging.WriteToFile($"[CPLEX Log]{Environment.NewLine}{log}");
            // 清空 stream 供下次 Solve() 使用（Benders 多輪迭代）
            _solverLogStream.SetLength(0);
            _solverLogStream.Position = 0;
        }

        /// <summary>目標式解值。MUST 在 Solve() 回傳 true 之後呼叫，無解時 CPLEX 會丟例外。</summary>
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
                Logging.ErrorOnce(ex, "SOLUTION_READ_FAILED", "公開 API 執行失敗", context, value,
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
                Logging.ErrorOnce(ex, "SOLVER_DISPOSE_FAILED", "公開 API 執行失敗", nameof(Dispose), "CPLEX",
                    ex.GetBaseException().Message);
                throw;
            }
        }

        #endregion

        #region 便捷方法（公開給子類別）

        // 以下是給「繼承 OptEngine 自己寫建模流程」的子類別用的短名稱包裝；
        // 一般專案走 Pool API（AddLHS / AddRHS / Create*），不需要碰這一區。

        /// <summary>建立單一變數的簡寫。預設為 [0, double.MaxValue] 的連續變數。</summary>
        protected INumVar CreateVar(string name, double lb = 0, double ub = double.MaxValue,
            VarType type = VarType.Continuous)
            => AddVariable(name, lb, ub, type);

        /// <summary>組線性表達式的簡寫。</summary>
        protected ILinearNumExpr Expr(IEnumerable<(double coef, INumVar var)> terms)
            => LinearExpr(terms);

        /// <summary>直接建立 lhs ≤ rhs 限制式（不經 pool）。</summary>
        protected IRange AddLE(string name, ILinearNumExpr lhs, double rhs)
            => AddConstraint(name, lhs, ConstraintSense.LessEqual, rhs);

        /// <summary>直接建立 lhs ≥ rhs 限制式（不經 pool）。</summary>
        protected IRange AddGE(string name, ILinearNumExpr lhs, double rhs)
            => AddConstraint(name, lhs, ConstraintSense.GreaterEqual, rhs);

        /// <summary>直接建立 lhs = rhs 限制式（不經 pool）。</summary>
        protected IRange AddEQ(string name, ILinearNumExpr lhs, double rhs)
            => AddConstraint(name, lhs, ConstraintSense.Equal, rhs);

        /// <summary>設定最小化目標式（覆寫既有目標式）。</summary>
        protected void Minimize(ILinearNumExpr expr) => SetObjective(expr, Core.ObjectiveSense.Minimize);

        /// <summary>設定最大化目標式（覆寫既有目標式）。</summary>
        protected void Maximize(ILinearNumExpr expr) => SetObjective(expr, Core.ObjectiveSense.Maximize);

        #endregion

        #region 限制式重置

        /// <summary>
        /// 清空目標式與所有限制式，保留變數，可在 Benders 迭代換輪次間呼叫。
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

        // 三個公開方法共用的邏輯，差異只在最後建立限制式物件的方式（Le/Ge/Eq）
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

        /// <summary>跨模型建立 &gt;= 限制式（value ≤ Σlhs）。呼叫後清空 targetEngine pool。</summary>
        public bool CreateGreatEqualThread(double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine)
            => CreateThreadConstraint(value, ruleName, targetEngine, sourceEngine,
                (v, expr) => sourceEngine.Model.Le(v, expr));

        /// <summary>跨模型建立 &lt;= 限制式（Σlhs ≤ value）。呼叫後清空 targetEngine pool。</summary>
        public bool CreateLessEqualThread(double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine)
            => CreateThreadConstraint(value, ruleName, targetEngine, sourceEngine,
                (v, expr) => sourceEngine.Model.Ge(v, expr));

        /// <summary>跨模型建立 = 限制式（Σlhs = value）。呼叫後清空 targetEngine pool。</summary>
        public bool CreateEqualThread(double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine)
            => CreateThreadConstraint(value, ruleName, targetEngine, sourceEngine,
                (v, expr) => sourceEngine.Model.Eq(v, expr));

        /// <summary>
        /// 從 this 的 CPLEX 模型中移除兩個子引擎的 thread 限制式，並清空各自的 _threadConstraints。
        /// 用於 Benders 迭代換輪次前的清理。
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
            targetEngine.Configuration(targetEngine.Config);

            var cloneManager = new SimpleCloneManager(sourceEngine.Model);

            // Clone objective
            var sourceObj = sourceEngine.Model.GetObjective();
            if (sourceObj != null)
            {
                var objective = (IObjective)sourceObj.MakeClone(cloneManager);
                targetEngine.Model.Add(objective);
            }

            // OptEngine 變數存在 Variables dict（不走 lpMatrix）
            // 對應 CplexEngine copyModel 只 clone 第一個變數的行為
            if (sourceEngine.Variables.Count > 0)
            {
                var firstVar = sourceEngine.Variables.Values.First();
                var temp = (INumVar)firstVar.MakeClone(cloneManager);
                targetEngine.Model.Add(temp);
            }

            return targetEngine;
        }

        /// <summary>
        /// 將 sourceEngine 的限制式加入 targetEngine 的 CPLEX 模型。
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
        public OptEngine VariableMerge(OptEngine targetEngine, HashSet<INumVar> variables)
        {
            foreach (var variable in variables)
                targetEngine.Model.Add(variable);
            return targetEngine;
        }

        #endregion

        #region IIS 衝突分析

        private List<string> RunConflictAnalysis(string proj)
        {
            var constraintArr = _constraints.ToArray();
            // 全 1.0 表示等權重：CPLEX Elastic Filtering 會自由選最小衝突子集，不偏向保留任何一條
            var prefs = Enumerable.Repeat(1.0, constraintArr.Length).ToArray();
            var conflictNames = new List<string>();

            if (!Model.RefineConflict(constraintArr, prefs))
                return conflictNames;

            FolderDir.IIS.CreateFolder();  // 即使未設定 exportLP/Sol，IIS 資料夾也必須存在才能寫入
            string iisPath = FolderDir.IIS.GetFilePath($"{proj}_IIS_{_startTime}.ilp");
            Model.WriteConflict(iisPath);
            Logging.Info($"[OptEngine] IIS written: {iisPath}");

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
                Logging.Info($"[OptEngine] Conflict constraints ({conflictNames.Count}): {string.Join(", ", conflictNames)}");

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
            _conflictConstraints = RunConflictAnalysis(_modelName ?? "Model");
            return _conflictConstraints;
        }

        #endregion

        #region 分類型別解答

        // 批次取解值：Model.GetValues(INumVar[]) 一次 interop，取代逐筆 Model.GetValue()
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

        // 軟性限制式：通用實作已上移 EngineBase（AddObjectiveTerm override 處理 CPLEX 就地 mutate）。

        /// <summary>
        /// 同時寫入兩個 TextWriter 的中繼器。
        /// 用途：CPLEX log 即時輸出到 Console，同時捕捉到 MemoryStream 供事後存 log 檔。
        /// </summary>
        private sealed class TeeWriter : TextWriter
        {
            private readonly TextWriter _primary;   // Console.Out（即時顯示）
            private readonly TextWriter _secondary; // StreamWriter → MemoryStream（捕捉）

            /// <summary>primary 是不該被關掉的目的地（Console.Out），secondary 才由本物件負責釋放。</summary>
            public TeeWriter(TextWriter primary, TextWriter secondary)
            {
                _primary = primary;
                _secondary = secondary;
            }

            /// <summary>沿用 primary 的編碼（TextWriter 要求實作）。</summary>
            public override System.Text.Encoding Encoding => _primary.Encoding;

            /// <summary>單字元同時寫到兩邊。</summary>
            public override void Write(char value)
            {
                _primary.Write(value);
                _secondary.Write(value);
            }

            /// <summary>字串同時寫到兩邊。</summary>
            public override void Write(string value)
            {
                _primary.Write(value);
                _secondary.Write(value);
            }

            /// <summary>整行同時寫到兩邊。</summary>
            public override void WriteLine(string value)
            {
                _primary.WriteLine(value);
                _secondary.WriteLine(value);
            }

            /// <summary>兩邊都 flush。</summary>
            public override void Flush()
            {
                _primary.Flush();
                _secondary.Flush();
            }

            /// <summary>只釋放 secondary；primary 是 Console.Out，關掉會讓整個 process 之後印不出東西。</summary>
            protected override void Dispose(bool disposing)
            {
                // _primary = Console.Out，不應 Dispose
                if (disposing) _secondary?.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
