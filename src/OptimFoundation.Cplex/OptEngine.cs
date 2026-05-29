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

        public OptEngine(CplexConfig config) : base(config) { }
        public OptEngine() : base(new CplexConfig()) { }

        #region Configuration

        public override void Configuration(ISolverConfig cfg)
        {

            Model = new ILOG.CPLEX.Cplex();
            _constraints.Clear();
            _conflictConstraints = null;
            ResetVerifyConstraints();

            CplexConfig config = cfg as CplexConfig;

            // Solver log 路由：
            //   enableLog = true  → CPLEX 直接寫 Console（即時顯示）
            //   enableLog = false → CPLEX 輸出導入 MemoryStream 靜默捕捉（不顯示）
            _solverLogStream?.Dispose();
            _solverLogWriter?.Dispose();
            _solverLogStream = new MemoryStream();
            _solverLogWriter = new StreamWriter(_solverLogStream) { AutoFlush = true };

            if (config.enableLog == true)
            {
                _enableLog = true;
                // TeeWriter：即時寫 Console + 同時捕捉到 MemoryStream（供事後存 log 檔）
                var tee = new TeeWriter(Console.Out, _solverLogWriter);
                Model.SetOut(tee);
                Model.SetWarning(tee);
            }
            else
            {
                // 靜默捕捉，不顯示在 Console
                Model.SetOut(_solverLogWriter);
                Model.SetWarning(_solverLogWriter);
            }

            #region 設定執行緒上限
            // CPLEX求解設定 - 工作執行緒上限 (預設: 32)
            if (config.workThreads.HasValue)
            {
                Model.SetParam(IntParam.Threads, config.workThreads.Value);
            }
            #endregion

            #region 設定限制式上限
            // CPLEX求解設定 - 限制式上限 (預設: 30,000)
            if (config.rowRead.HasValue)
            {
                Model.SetParam(IntParam.RowReadLim, config.rowRead.Value);
            }
            #endregion

            #region 設定工作記憶體上限
            // CPLEX求解設定 - 工作記憶體上限: 2 GB (預設)
            // (double)(workMemory.Value / 1024), 1) GB
            if (config.workMemory.HasValue)
            {
                Model.SetParam(IntParam.WorkMem, config.workMemory.Value);
                Model.SetParam(IntParam.NodeFileInd, 0);
            }
            #endregion

            #region 設定求解下限
            // CPLEX求解設定 - 求解下限: epGap.Value * 100 % (預設: 1e-4 %)
            if (config.epGap.HasValue)
            {
                Model.SetParam(DoubleParam.EpGap, config.epGap.Value);
            }
            #endregion

            #region node 選擇策略
            // CPLEX求解設定 - node選擇策略: (預設)
            if (config.nodeSelect.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.NodeSelect, config.nodeSelect.Value);
            }
            #endregion

            #region 設定 Random Seed
            // CPLEX求解設定 - 隨機種子 (預設: 0)
            if (config.randomSeed.HasValue)
            {
                Model.SetParam(Param.RandomSeed, config.randomSeed.Value);
            }
            #endregion

            #region 是否紀錄LOG
            if (_enableLog)
                Logging.Info($"[Environment Setting] CPLEX Log → Console (real-time)");
            #endregion

            #region 是否輸出 LP 檔案
            if (config.exportLP == true)
            {
                _exportLp = true;
                Logging.Info($"[Environment Setting] Enabled LP File (.lp) Output");
            }
            #endregion

            #region 是否輸出 Model 檔案
            if (config.exportMPS == true)
            {
                _exportMps = true;
                Logging.Info($"[Environment Setting] Enabled Model File (.mps) Output");
            }
            #endregion

            #region 是否輸出 sol 檔案
            if (config.exportSol == true)
            {
                _exportSol = true;
                Logging.Info($"[Environment Setting] Enabled Solution File (.sol) Output");
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
            if (config.epOpt.HasValue)
            {
                Model.SetParam(DoubleParam.EpOpt, config.epOpt.Value);
            }
            #endregion

            #region 設定容忍區間(Feasibility tolerance)
            // CPLEX求解設定 - Feasibility tolerance (預設: 1e-06)
            if (config.epRHS.HasValue)
            {
                Model.SetParam(DoubleParam.EpRHS, config.epRHS.Value);
            }
            #endregion

            #region 設定逾時秒數
            // CPLEX求解設定 - 逾時秒數: (預設: 無限制)
            if (config.timeLimit.HasValue)
            {
                Model.SetParam(DoubleParam.TiLim, config.timeLimit.Value);
            }
            #endregion

            #region 設定 Solution Polishing 秒數
            // CPLEX求解設定 - Solution Polishing秒數: (預設: 無)
            if (config.polishAfterTime.HasValue)
            {
                Model.SetParam(DoubleParam.PolishAfterTime, config.polishAfterTime.Value);
            }
            #endregion

            #region 設定解析模式
            // CPLEX求解設定 - 解析模式: 平衡最佳可行解 (預設)
            if (config.mipEmphasis.HasValue)
            {
                Model.SetParam(IntParam.MIPEmphasis, config.mipEmphasis.Value);

                string mipEmphasisDescription = config.mipEmphasis.Value switch
                {
                    1 => "強調可行解優於最佳解",
                    2 => "強調最佳解優於可行解",
                    3 => "強調路徑的最佳解",
                    4 => "強調尋找隱藏可行解",
                    _ => "平衡最佳可行解 (預設)"
                };
                Logging.Info($"[Environment Setting] MIPEmphasis={config.mipEmphasis.Value} ({mipEmphasisDescription})");
            }
            #endregion

            #region 設定分支模式
            // CPLEX求解設定 - 分支模式: 自動選擇變數分支 (預設)
            if (config.varSel.HasValue)
            {
                Model.SetParam(IntParam.VarSel, config.varSel.Value);

                string varSelDescription = config.varSel.Value switch
                {
                    -1 => "以最小可行解選擇變數分支",
                    1 => "以最大可行解選擇變數分支",
                    2 => "以假定成本選擇分支",
                    3 => "強分支",
                    4 => "以假定降低成本選擇分支",
                    _ => "自動選擇變數分支 (預設)"
                };
                Logging.Info($"[Environment Setting] VarSel={config.varSel.Value} ({varSelDescription})");
            }
            #endregion

            #region 設定演算法
            // CPLEX求解設定 - 演算法: 自動選擇 (預設)
            if (config.algorithm.HasValue)
            {
                Model.SetParam(IntParam.RootAlgorithm, config.algorithm.Value);

                string algorithmDescription = string.Empty;
                switch (config.algorithm.Value)
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
            }
            #endregion

            #region 設定節點資訊儲存模式
            // CPLEX求解設定 - 節點資訊: 節點資訊壓縮存放於記憶體 (預設)
            if (config.nodeFileInd.HasValue)
            {
                Model.SetParam(IntParam.NodeFileInd, config.nodeFileInd.Value);

                string nodeFileIndDescription = string.Empty;
                switch (config.nodeFileInd.Value)
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
            }
            #endregion

        }


        public void SetModelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Model name cannot be null or whitespace.", nameof(name));
            _modelName = name;
        }

        #endregion

        #region EngineBase 抽象方法實作

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

        protected override ILinearNumExpr LinearExpr(IEnumerable<(double coef, INumVar var)> terms)
        {
            var expr = Model.LinearNumExpr();
            foreach (var (coef, v) in terms)
                expr.AddTerm(coef, v);
            return expr;
        }

        protected override IRange AddConstraint(string name, ILinearNumExpr lhs, ConstraintSense sense, double rhs)
        {
            IRange r = sense switch
            {
                ConstraintSense.LessEqual => Model.AddLe(lhs, rhs),
                ConstraintSense.Equal => Model.AddEq(lhs, rhs),
                ConstraintSense.GreaterEqual => Model.AddGe(lhs, rhs),
                _ => throw new ArgumentOutOfRangeException(nameof(sense))
            };
            r.Name = name;
            _constraints.Add(r);
            return r;
        }

        protected override IRange AddRangeConstraint(string name, ILinearNumExpr expr, double lb, double ub)
        {
            var r = Model.AddRange(lb, expr, ub);
            r.Name = name;
            _constraints.Add(r);
            return r;
        }

        protected override void SetObjective(ILinearNumExpr expr, Core.ObjectiveSense sense)
        {
            if (sense == Core.ObjectiveSense.Minimize)
                Model.AddMinimize(expr);
            else
                Model.AddMaximize(expr);
        }

        protected override void SetVariableBounds(INumVar variable, double? lb, double? ub)
        {
            if (lb.HasValue) variable.LB = lb.Value;
            if (ub.HasValue) variable.UB = ub.Value;
        }

        #endregion

        #region ISolverEngine 實作

        /// <summary>
        /// 初始化 CPLEX 模型並套用 Config 參數。
        /// 子類別覆寫時必須先呼叫 base.Build()。
        /// </summary>
        public override void Build() => Configuration(Config);

        protected void SetProjectName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Project name cannot be null or whitespace.", nameof(name));
            _modelName = name;
        }

        public override bool Solve()
        {
            string proj = _modelName ?? "Project";

            if (_exportLp)
                Model.ExportModel(FolderDir.Model.GetFilePath($"{proj}_LP_{_startTime}.lp"));

            if (_exportMps)
                Model.ExportModel(FolderDir.Model.GetFilePath($"{proj}_MPS_{_startTime}.mps"));

            Model.Solve();

            // CPLEX solver log 讀出（enableLog 時輸出完整 log，否則只輸出摘要）
            FlushSolverLog();

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
        /// 將 CPLEX solver log 從 MemoryStream 讀出並輸出。
        /// enableLog=true → Logging.Info 完整 log；否則不輸出（仍清空 stream 供下次使用）。
        /// </summary>
        private void FlushSolverLog()
        {
            if (_solverLogWriter == null || _solverLogStream == null) return;

            _solverLogWriter.Flush();
            _solverLogStream.Position = 0;

            if (_enableLog)
            {
                // Console 已有即時輸出，只把捕捉到的 log 另存進 log 檔（不重印到 Console）
                string log = System.Text.Encoding.UTF8.GetString(
                    _solverLogStream.GetBuffer(), 0, (int)_solverLogStream.Length);
                if (!string.IsNullOrWhiteSpace(log))
                    Logging.WriteToFile($"[CPLEX Log]{Environment.NewLine}{log}");
            }

            // 清空 stream 供下次 Solve() 使用（Benders 多輪迭代）
            _solverLogStream.SetLength(0);
            _solverLogStream.Position = 0;
        }

        public override double GetObjectiveValue() => Model.GetObjValue();

        public override double GetVariableValue(string name) => Model.GetValue(Variables[name]);

        public override void Dispose()
        {
            Model?.End();
            Model = null;
            _solverLogWriter?.Dispose();
            _solverLogStream?.Dispose();
        }

        #endregion

        #region 便捷方法（公開給子類別）

        protected INumVar CreateVar(string name, double lb = 0, double ub = double.MaxValue,
            VarType type = VarType.Continuous)
            => AddVariable(name, lb, ub, type);

        protected ILinearNumExpr Expr(IEnumerable<(double coef, INumVar var)> terms)
            => LinearExpr(terms);

        protected IRange AddLE(string name, ILinearNumExpr lhs, double rhs)
            => AddConstraint(name, lhs, ConstraintSense.LessEqual, rhs);

        protected IRange AddGE(string name, ILinearNumExpr lhs, double rhs)
            => AddConstraint(name, lhs, ConstraintSense.GreaterEqual, rhs);

        protected IRange AddEQ(string name, ILinearNumExpr lhs, double rhs)
            => AddConstraint(name, lhs, ConstraintSense.Equal, rhs);

        protected void Minimize(ILinearNumExpr expr) => SetObjective(expr, Core.ObjectiveSense.Minimize);
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
            if (_constraints.Count > 0)
            {
                Model.End(_constraints.ToArray());
                Model.Remove(_constraints.ToArray());
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

        /// <summary>
        /// 跨模型建立 >= 限制式（Benders/多線程）。
        /// 以 targetEngine pool 的變數為 LHS，建立 value ≤ Σ(lhs) 的限制式，
        /// 掛載至 sourceEngine 的 CPLEX 模型，追蹤於 this._constraints。
        /// 呼叫後會清空 targetEngine 的 pool。
        /// </summary>
        public bool CreateGreatEqualThread(double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine)
        {
            var targetTerms = targetEngine.PoolLhsTerms.ToList();
            if (targetTerms.Count == 0) return false;

            string verify = string.Join(",", targetTerms.OrderBy(t => t.var.Name).Select(t => t.var.Name));
            if (!_threadVerifyConstraints.Contains(verify))
            {
                var expr = targetEngine.Model.LinearNumExpr();
                foreach (var (coef, v) in targetTerms)
                    expr.AddTerm(coef, v);

                var constraint = sourceEngine.Model.Le(value, expr);
                constraint.Name = $"{ruleName}_{_threadRuleCount}";
                _threadConstraints.Add(constraint);   // Le() 未加入任何 model，存入 _threadConstraints
                _threadVerifyConstraints.Add(verify);
                _threadRuleCount++;
            }

            targetEngine.ClearPool();
            return true;
        }

        /// <summary>
        /// 跨模型建立 &lt;= 限制式（Benders/多線程）。
        /// 以 targetEngine pool 的變數為 LHS，建立 Σ(lhs) ≤ value 的限制式。
        /// </summary>
        public bool CreateLessEqualThread(double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine)
        {
            var targetTerms = targetEngine.PoolLhsTerms.ToList();
            if (targetTerms.Count == 0) return false;

            string verify = string.Join(",", targetTerms.OrderBy(t => t.var.Name).Select(t => t.var.Name));
            if (!_threadVerifyConstraints.Contains(verify))
            {
                var expr = targetEngine.Model.LinearNumExpr();
                foreach (var (coef, v) in targetTerms)
                    expr.AddTerm(coef, v);

                var constraint = sourceEngine.Model.Ge(value, expr);
                constraint.Name = $"{ruleName}_{_threadRuleCount}";
                _threadConstraints.Add(constraint);   // Ge() 未加入任何 model
                _threadVerifyConstraints.Add(verify);
                _threadRuleCount++;
            }

            targetEngine.ClearPool();
            return true;
        }

        /// <summary>
        /// 跨模型建立 = 限制式（Benders/多線程）。
        /// 以 targetEngine pool 的變數為 LHS，建立 Σ(lhs) = value 的限制式。
        /// </summary>
        public bool CreateEqualThread(double value, string ruleName, OptEngine targetEngine, OptEngine sourceEngine)
        {
            var targetTerms = targetEngine.PoolLhsTerms.ToList();
            if (targetTerms.Count == 0) return false;

            string verify = string.Join(",", targetTerms.OrderBy(t => t.var.Name).Select(t => t.var.Name));
            if (!_threadVerifyConstraints.Contains(verify))
            {
                var expr = targetEngine.Model.LinearNumExpr();
                foreach (var (coef, v) in targetTerms)
                    expr.AddTerm(coef, v);

                var constraint = sourceEngine.Model.Eq(value, expr);
                constraint.Name = $"{ruleName}_{_threadRuleCount}";
                _threadConstraints.Add(constraint);   // Eq() 未加入任何 model
                _threadVerifyConstraints.Add(verify);
                _threadRuleCount++;
            }

            targetEngine.ClearPool();
            return true;
        }

        /// <summary>
        /// 從 this 的 CPLEX 模型中移除兩個子引擎的 thread 限制式，並清空各自的 _threadConstraints。
        /// 用於 Benders 迭代換輪次前的清理。
        /// </summary>
        public void ResetThreadConstraint(OptEngine threadEngine1, OptEngine threadEngine2)
        {
            if (threadEngine1._threadConstraints.Count > 0)
            {
                Model.End(threadEngine1._threadConstraints.ToArray());
                Model.Remove(threadEngine1._threadConstraints.ToArray());
                threadEngine1._threadConstraints.Clear();
            }
            if (threadEngine2._threadConstraints.Count > 0)
            {
                Model.End(threadEngine2._threadConstraints.ToArray());
                Model.Remove(threadEngine2._threadConstraints.ToArray());
                threadEngine2._threadConstraints.Clear();
            }
            _threadConstraints.Clear();
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
            _conflictConstraints = RunConflictAnalysis(_modelName ?? "Project");
            return _conflictConstraints;
        }

        #endregion

        #region 分類型別解答

        /// <summary>取出所有連續變數（Float）的解值。</summary>
        public IReadOnlyDictionary<string, double> GetCVSolution()
            => Variables
                .Where(kv => kv.Value.Type == NumVarType.Float)
                .ToDictionary(kv => kv.Key, kv => Model.GetValue(kv.Value));

        /// <summary>取出所有整數變數（Int）的解值。</summary>
        public IReadOnlyDictionary<string, double> GetIVSolution()
            => Variables
                .Where(kv => kv.Value.Type == NumVarType.Int)
                .ToDictionary(kv => kv.Key, kv => Model.GetValue(kv.Value));

        /// <summary>取出所有二元變數（Bool）的解值。</summary>
        public IReadOnlyDictionary<string, double> GetBVSolution()
            => Variables
                .Where(kv => kv.Value.Type == NumVarType.Bool)
                .ToDictionary(kv => kv.Key, kv => Model.GetValue(kv.Value));

        #endregion

        #region 軟性限制式

        public override bool CreateLeSoft(double rhs, double penalty)
        {
            if (!HasPool) return false;
            var obj = Model.GetObjective();
            double p = obj.Sense == ILOG.Concert.ObjectiveSense.Maximize ? -penalty : penalty;
            var objExpr = (ILinearNumExpr)obj.Expr;
            foreach (var (coef, var) in PoolLhsTerms)
                objExpr.AddTerm(p * coef, var);
            ClearPool();
            return true;
        }

        public override bool CreateGeSoft(double rhs, double penalty)
        {
            if (!HasPool) return false;
            var obj = Model.GetObjective();
            double p = obj.Sense == ILOG.Concert.ObjectiveSense.Minimize ? -penalty : penalty;
            var objExpr = (ILinearNumExpr)obj.Expr;
            foreach (var (coef, var) in PoolLhsTerms)
                objExpr.AddTerm(p * coef, var);
            ClearPool();
            return true;
        }

        public override bool CreateEqSoft(double rhs, double penalty, string name)
        {
            if (!HasPool) return false;
            var obj = Model.GetObjective();
            double p = obj.Sense == ILOG.Concert.ObjectiveSense.Maximize ? -penalty : penalty;
            var dn = Model.NumVar(0, double.MaxValue, NumVarType.Float, $"Delta_Neg_{name}");
            var dp = Model.NumVar(0, double.MaxValue, NumVarType.Float, $"Delta_Pos_{name}");
            var lhs = Model.LinearNumExpr();
            foreach (var (coef, var) in PoolLhsTerms)
                lhs.AddTerm(coef, var);
            lhs.AddTerm(1.0, dn);
            lhs.AddTerm(-1.0, dp);
            var constr = Model.AddEq(lhs, rhs - PoolLhsConst);
            constr.Name = name;
            var objExpr = (ILinearNumExpr)obj.Expr;
            objExpr.AddTerm(p, dn);
            objExpr.AddTerm(p, dp);
            ClearPool();
            return true;
        }

        #endregion

        /// <summary>
        /// 同時寫入兩個 TextWriter 的中繼器。
        /// 用途：CPLEX log 即時輸出到 Console，同時捕捉到 MemoryStream 供事後存 log 檔。
        /// </summary>
        private sealed class TeeWriter : TextWriter
        {
            private readonly TextWriter _primary;   // Console.Out（即時顯示）
            private readonly TextWriter _secondary; // StreamWriter → MemoryStream（捕捉）

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

            protected override void Dispose(bool disposing)
            {
                // _primary = Console.Out，不應 Dispose
                if (disposing) _secondary?.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
