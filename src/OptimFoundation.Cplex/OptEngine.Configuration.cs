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
    /// 為 <see cref="OptEngine"/> 套用 <see cref="CplexConfig"/> 中有值的參數，以及 <see cref="ProjectConfig"/> 的輸出選項。
    /// </summary>
    public partial class OptEngine
    {
        #region Configuration

        /// <summary>
        /// 建立空的 CPLEX 模型，套用有指定值的求解器參數，並以 [CPLEX 參數設定] 記錄每一項設定。
        /// 再次呼叫會重建空模型，清除限制式、目標式、衝突結果與已用限制式名稱；變數索引保留。
        /// </summary>
        /// <param name="cfg">必須是非 null 的 <see cref="CplexConfig"/>；否則讀取設定時會拋出 NullReferenceException。</param>
        public override void LoadConfig(ISolverConfig cfg)
        {

            Model = new ILOG.CPLEX.Cplex();
            _constraints.Clear();
            _objective = null;
            _conflictConstraints = null;
            ResetVerifyConstraints();

            CplexConfig config = cfg as CplexConfig;

            _solverLogStream?.Dispose();
            _solverLogWriter?.Dispose();
            _solverLogStream = new MemoryStream();
            _solverLogWriter = new StreamWriter(_solverLogStream) { AutoFlush = true };

            if (_projectConfig.EnableSolverLog)
            {
                _enableLog = true;
                var tee = new TeeWriter(Console.Out, _solverLogWriter);
                Model.SetOut(tee);
                Model.SetWarning(tee);
            }
            else
            {
                Model.SetOut(_solverLogWriter);
                Model.SetWarning(_solverLogWriter);
            }

            #region ProjectConfig — 輸出與 log 路由（與「solver 怎麼解」無關，先印成獨立一群）
            Logging.Info(_enableLog
                ? "[CPLEX 求解日誌設定] 導向主控台（即時）"
                : "[CPLEX 求解日誌設定] 只寫出到框架日誌檔");

            if (_projectConfig.ExportLP)
            {
                _exportLp = true;
                Logging.Info("[模型檔匯出設定] 已啟用 | 格式=lp");
            }

            if (_projectConfig.ExportMPS)
            {
                _exportMps = true;
                Logging.Info("[模型檔匯出設定] 已啟用 | 格式=mps");
            }

            if (_projectConfig.ExportSol)
            {
                _exportSol = true;
                Logging.Info("[解檔匯出設定] 已啟用 | 格式=sol");
            }
            #endregion

            #region 設定執行緒上限
            if (config.Threads.HasValue)
            {
                Model.SetParam(Param.Threads, config.Threads.Value);
                Logging.Info($"[CPLEX 參數設定] Threads={config.Threads.Value}");
            }
            #endregion

            #region 設定限制式上限
            if (config.RowRead.HasValue)
            {
#pragma warning disable CS0618 // 保留公開 API 對過時 CPLEX 參數的支援
                Model.SetParam(Param.Read.Constraints, config.RowRead.Value);
#pragma warning restore CS0618
                Logging.Info($"[CPLEX 參數設定] RowReadLim={config.RowRead.Value}");
            }
            #endregion

            #region 設定工作記憶體上限
            // MemoryLimitMb 先停用節點檔；NodeFileStrategy 稍後可覆寫。
            if (config.MemoryLimitMb.HasValue)
            {
                Model.SetParam(IntParam.WorkMem, config.MemoryLimitMb.Value);
                Model.SetParam(Param.MIP.Strategy.File, 0);
                Logging.Info($"[CPLEX 參數設定] WorkMem={config.MemoryLimitMb.Value} MB（NodeFileInd=0）");
            }
            #endregion

            #region 設定求解下限
            if (config.MipGap.HasValue)
            {
                Model.SetParam(Param.MIP.Tolerances.MIPGap, config.MipGap.Value);
                Logging.Info($"[CPLEX 參數設定] EpGap={config.MipGap.Value}");
            }
            #endregion

            #region node 選擇策略
            if (config.NodeSelect.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.NodeSelect, config.NodeSelect.Value);

                string nodeSelectDescription = config.NodeSelect.Value switch
                {
                    0 => "深度優先搜尋",
                    2 => "最佳估計值搜尋",
                    3 => "交替最佳估計值搜尋",
                    _ => "最佳界限搜尋（預設）"
                };
                Logging.Info($"[CPLEX 參數設定] {nodeSelectDescription} | NodeSelect={config.NodeSelect.Value}");
            }
            #endregion

            #region 設定 Random Seed
            if (config.Seed.HasValue)
            {
                Model.SetParam(Param.RandomSeed, config.Seed.Value);
                Logging.Info($"[CPLEX 參數設定] RandomSeed={config.Seed.Value}");
            }
            #endregion

            if (_exportLp || _exportSol)
            {
                FolderDir.Model.CreateFolder();
                FolderDir.Solution.CreateFolder();
                FolderDir.IIS.CreateFolder();
                FolderDir.Model.CreateFolder();
            }

            #region 設定容忍區間(Optimality tolerance)
            if (config.OptimalityTol.HasValue)
            {
                Model.SetParam(Param.Simplex.Tolerances.Optimality, config.OptimalityTol.Value);
                Logging.Info($"[CPLEX 參數設定] EpOpt={config.OptimalityTol.Value}");
            }
            #endregion

            #region 設定容忍區間(Feasibility tolerance)
            if (config.FeasibilityTol.HasValue)
            {
                Model.SetParam(Param.Simplex.Tolerances.Feasibility, config.FeasibilityTol.Value);
                Logging.Info($"[CPLEX 參數設定] EpRHS={config.FeasibilityTol.Value}");
            }
            #endregion

            #region 設定逾時秒數
            if (config.TimeLimit.HasValue)
            {
                Model.SetParam(Param.TimeLimit, config.TimeLimit.Value);
                Logging.Info($"[CPLEX 參數設定] TiLim={config.TimeLimit.Value} 秒");
            }
            #endregion

            #region 設定 Solution Polishing 秒數
            if (config.PolishAfterTime.HasValue)
            {
                Model.SetParam(Param.MIP.PolishAfter.Time, config.PolishAfterTime.Value);
                Logging.Info($"[CPLEX 參數設定] PolishAfterTime={config.PolishAfterTime.Value} 秒");
            }
            #endregion

            #region 設定解析模式
            if (config.Emphasis.HasValue)
            {
                Model.SetParam(Param.Emphasis.MIP, config.Emphasis.Value);

                string mipEmphasisDescription = config.Emphasis.Value switch
                {
                    1 => "強調可行解優於最佳解",
                    2 => "強調最佳解優於可行解",
                    3 => "強調路徑的最佳解",
                    4 => "強調尋找隱藏可行解",
                    _ => "平衡最佳可行解（預設）"
                };
                Logging.Info($"[CPLEX 參數設定] {mipEmphasisDescription} | MIPEmphasis={config.Emphasis.Value}");
            }
            #endregion

            #region 設定分支模式
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
                    _ => "自動選擇變數分支（預設）"
                };
                Logging.Info($"[CPLEX 參數設定] {varSelDescription} | VarSel={config.VariableSelect.Value}");
            }
            #endregion

            #region 設定演算法
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
                        algorithmDescription = "自動選擇（預設）";
                        break;
                }
                Logging.Info($"[CPLEX 參數設定] {algorithmDescription} | RootAlgorithm={config.RootAlgorithm.Value}");
            }
            #endregion

            #region 設定節點資訊儲存模式
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
                        nodeFileIndDescription = "節點資訊壓縮存放於記憶體（預設）";
                        break;
                }
                Logging.Info($"[CPLEX 參數設定] {nodeFileIndDescription} | NodeFileInd={config.NodeFileStrategy.Value}");
            }
            #endregion

            #region 前處理 / Presolve
            // 套用前處理開關；ISolverConfig.Presolve 與 PreIndicator 對應同一項設定。
            if (config.PreIndicator.HasValue)
            {
                Model.SetParam(Param.Preprocessing.Presolve, config.PreIndicator.Value);
                Logging.Info($"[CPLEX 參數設定] Presolve={(config.PreIndicator.Value ? "開" : "關")}");
            }

            if (config.Symmetry.HasValue)
            {
                Model.SetParam(Param.Preprocessing.Symmetry, config.Symmetry.Value);
                Logging.Info($"[CPLEX 參數設定] Symmetry={config.Symmetry.Value}");
            }
            #endregion

            #region 子問題（節點）演算法
            if (config.NodeAlgorithm.HasValue)
            {
                Model.SetParam(Param.NodeAlgorithm, config.NodeAlgorithm.Value);
                Logging.Info($"[CPLEX 參數設定] NodeAlg={config.NodeAlgorithm.Value}");
            }
            #endregion

            #region 啟發式投入程度（HeuristicEffort）
            if (config.HeuristicEffort.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.HeuristicEffort, config.HeuristicEffort.Value);
                Logging.Info($"[CPLEX 參數設定] HeuristicEffort={config.HeuristicEffort.Value}");
            }
            #endregion

            #region 決定論 / 計時
            if (config.ParallelMode.HasValue)
            {
                Model.SetParam(Param.Parallel, config.ParallelMode.Value);
                string parallelDescription = config.ParallelMode.Value switch
                {
                    -1 => "機會式",
                    1 => "決定論",
                    _ => "自動（預設）"
                };
                Logging.Info($"[CPLEX 參數設定] {parallelDescription} | Parallel={config.ParallelMode.Value}");
            }
            if (config.DeterministicTimeLimit.HasValue)
            {
                Model.SetParam(Param.DetTimeLimit, config.DeterministicTimeLimit.Value);
                Logging.Info($"[CPLEX 參數設定] DetTimeLimit={config.DeterministicTimeLimit.Value} ticks");
            }
            if (config.ClockType.HasValue)
            {
                Model.SetParam(Param.ClockType, config.ClockType.Value);
                string clockDescription = config.ClockType.Value == 1 ? "CPU 時間" : "牆鐘時間";
                Logging.Info($"[CPLEX 參數設定] {clockDescription} | ClockType={config.ClockType.Value}");
            }
            if (config.NumericalEmphasis.HasValue)
            {
                Model.SetParam(Param.Emphasis.Numerical, config.NumericalEmphasis.Value);
                Logging.Info($"[CPLEX 參數設定] NumericalEmphasis={config.NumericalEmphasis.Value}");
            }
            #endregion

            #region MIP 容差延伸
            if (config.IntegralityTolerance.HasValue)
            {
                Model.SetParam(Param.MIP.Tolerances.Integrality, config.IntegralityTolerance.Value);
                Logging.Info($"[CPLEX 參數設定] Integrality={config.IntegralityTolerance.Value}");
            }
            if (config.AbsoluteMipGap.HasValue)
            {
                Model.SetParam(Param.MIP.Tolerances.AbsMIPGap, config.AbsoluteMipGap.Value);
                Logging.Info($"[CPLEX 參數設定] AbsMIPGap={config.AbsoluteMipGap.Value}");
            }
            #endregion

            #region MIP limits 延伸
            if (config.NodeLimit.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.Nodes, config.NodeLimit.Value);
                Logging.Info($"[CPLEX 參數設定] Nodes={config.NodeLimit.Value}");
            }
            if (config.TreeMemoryLimitMb.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.TreeMemory, config.TreeMemoryLimitMb.Value);
                Logging.Info($"[CPLEX 參數設定] TreeMemory={config.TreeMemoryLimitMb.Value} MB");
            }
            if (config.IntegerSolutionLimit.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.Solutions, config.IntegerSolutionLimit.Value);
                Logging.Info($"[CPLEX 參數設定] Solutions={config.IntegerSolutionLimit.Value}");
            }
            #endregion

            #region MIP 搜尋策略延伸
            if (config.Probe.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.Probe, config.Probe.Value);
                Logging.Info($"[CPLEX 參數設定] Probe={config.Probe.Value}");
            }
            if (config.RinsHeuristicFrequency.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.RINSHeur, config.RinsHeuristicFrequency.Value);
                Logging.Info($"[CPLEX 參數設定] RINSHeur={config.RinsHeuristicFrequency.Value}");
            }
            if (config.MipSearch.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.Search, config.MipSearch.Value);
                Logging.Info($"[CPLEX 參數設定] Search={config.MipSearch.Value}");
            }
            if (config.DiveType.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.Dive, config.DiveType.Value);
                Logging.Info($"[CPLEX 參數設定] Dive={config.DiveType.Value}");
            }
            if (config.BranchDirection.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.Branch, config.BranchDirection.Value);
                Logging.Info($"[CPLEX 參數設定] Branch={config.BranchDirection.Value}");
            }
            #endregion

            #region MIP cuts
            if (config.CutsFactor.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.CutsFactor, config.CutsFactor.Value);
                Logging.Info($"[CPLEX 參數設定] CutsFactor={config.CutsFactor.Value}");
            }
            if (config.CutPasses.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.CutPasses, config.CutPasses.Value);
                Logging.Info($"[CPLEX 參數設定] CutPasses={config.CutPasses.Value}");
            }
            if (config.GomoryCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.Gomory, config.GomoryCuts.Value);
                Logging.Info($"[CPLEX 參數設定] GomoryCuts={config.GomoryCuts.Value}");
            }
            if (config.CoverCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.Covers, config.CoverCuts.Value);
                Logging.Info($"[CPLEX 參數設定] CoverCuts={config.CoverCuts.Value}");
            }
            if (config.CliqueCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.Cliques, config.CliqueCuts.Value);
                Logging.Info($"[CPLEX 參數設定] CliqueCuts={config.CliqueCuts.Value}");
            }
            if (config.MirCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.MIRCut, config.MirCuts.Value);
                Logging.Info($"[CPLEX 參數設定] MIRCuts={config.MirCuts.Value}");
            }
            if (config.FlowCoverCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.FlowCovers, config.FlowCoverCuts.Value);
                Logging.Info($"[CPLEX 參數設定] FlowCoverCuts={config.FlowCoverCuts.Value}");
            }
            #endregion

            #region 純 LP（Simplex / Barrier）
            if (config.SimplexIterationLimit.HasValue)
            {
                Model.SetParam(Param.Simplex.Limits.Iterations, config.SimplexIterationLimit.Value);
                Logging.Info($"[CPLEX 參數設定] SimplexIterations={config.SimplexIterationLimit.Value}");
            }
            if (config.BarrierAlgorithm.HasValue)
            {
                Model.SetParam(Param.Barrier.Algorithm, config.BarrierAlgorithm.Value);
                Logging.Info($"[CPLEX 參數設定] BarrierAlgorithm={config.BarrierAlgorithm.Value}");
            }
            #endregion

            #region 停止條件
            if (config.BarrierConvergeTol.HasValue)
            {
                Model.SetParam(Param.Barrier.ConvergeTol, config.BarrierConvergeTol.Value);
                Logging.Info($"[CPLEX 參數設定] BarrierConvergeTol={config.BarrierConvergeTol.Value}");
            }
            if (config.BarrierIterationLimit.HasValue)
            {
                Model.SetParam(Param.Barrier.Limits.Iteration, config.BarrierIterationLimit.Value);
                Logging.Info($"[CPLEX 參數設定] BarrierIterationLimit={config.BarrierIterationLimit.Value}");
            }
            if (config.BarrierQcpConvergeTol.HasValue)
            {
                Model.SetParam(Param.Barrier.QCPConvergeTol, config.BarrierQcpConvergeTol.Value);
                Logging.Info($"[CPLEX 參數設定] BarrierQcpConvergeTol={config.BarrierQcpConvergeTol.Value}");
            }
            if (config.FeasOptRelaxTolerance.HasValue)
            {
                Model.SetParam(Param.Feasopt.Tolerance, config.FeasOptRelaxTolerance.Value);
                Logging.Info($"[CPLEX 參數設定] FeasOptRelaxTolerance={config.FeasOptRelaxTolerance.Value}");
            }
            if (config.LinearizationTolerance.HasValue)
            {
                Model.SetParam(Param.MIP.Tolerances.Linearization, config.LinearizationTolerance.Value);
                Logging.Info($"[CPLEX 參數設定] LinearizationTolerance={config.LinearizationTolerance.Value}");
            }
            if (config.LowerCutoff.HasValue)
            {
                Model.SetParam(Param.MIP.Tolerances.LowerCutoff, config.LowerCutoff.Value);
                Logging.Info($"[CPLEX 參數設定] LowerCutoff={config.LowerCutoff.Value}");
            }
            if (config.LowerObjectiveStop.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.LowerObjStop, config.LowerObjectiveStop.Value);
                Logging.Info($"[CPLEX 參數設定] LowerObjectiveStop={config.LowerObjectiveStop.Value}");
            }
            if (config.NetworkFeasibilityTol.HasValue)
            {
                Model.SetParam(Param.Network.Tolerances.Feasibility, config.NetworkFeasibilityTol.Value);
                Logging.Info($"[CPLEX 參數設定] NetworkFeasibilityTol={config.NetworkFeasibilityTol.Value}");
            }
            if (config.NetworkIterationLimit.HasValue)
            {
                Model.SetParam(Param.Network.Iterations, config.NetworkIterationLimit.Value);
                Logging.Info($"[CPLEX 參數設定] NetworkIterationLimit={config.NetworkIterationLimit.Value}");
            }
            if (config.NetworkOptimalityTol.HasValue)
            {
                Model.SetParam(Param.Network.Tolerances.Optimality, config.NetworkOptimalityTol.Value);
                Logging.Info($"[CPLEX 參數設定] NetworkOptimalityTol={config.NetworkOptimalityTol.Value}");
            }
            if (config.ObjectiveDifference.HasValue)
            {
                Model.SetParam(Param.MIP.Tolerances.ObjDifference, config.ObjectiveDifference.Value);
                Logging.Info($"[CPLEX 參數設定] ObjectiveDifference={config.ObjectiveDifference.Value}");
            }
            if (config.RelativeObjectiveDifference.HasValue)
            {
                Model.SetParam(Param.MIP.Tolerances.RelObjDifference, config.RelativeObjectiveDifference.Value);
                Logging.Info($"[CPLEX 參數設定] RelativeObjectiveDifference={config.RelativeObjectiveDifference.Value}");
            }
            if (config.SiftingIterationLimit.HasValue)
            {
                Model.SetParam(Param.Sifting.Iterations, config.SiftingIterationLimit.Value);
                Logging.Info($"[CPLEX 參數設定] SiftingIterationLimit={config.SiftingIterationLimit.Value}");
            }
            if (config.SimplexLowerObjectiveLimit.HasValue)
            {
                Model.SetParam(Param.Simplex.Limits.LowerObj, config.SimplexLowerObjectiveLimit.Value);
                Logging.Info($"[CPLEX 參數設定] SimplexLowerObjectiveLimit={config.SimplexLowerObjectiveLimit.Value}");
            }
            if (config.SimplexUpperObjectiveLimit.HasValue)
            {
                Model.SetParam(Param.Simplex.Limits.UpperObj, config.SimplexUpperObjectiveLimit.Value);
                Logging.Info($"[CPLEX 參數設定] SimplexUpperObjectiveLimit={config.SimplexUpperObjectiveLimit.Value}");
            }
            if (config.UpperCutoff.HasValue)
            {
                Model.SetParam(Param.MIP.Tolerances.UpperCutoff, config.UpperCutoff.Value);
                Logging.Info($"[CPLEX 參數設定] UpperCutoff={config.UpperCutoff.Value}");
            }
            if (config.UpperObjectiveStop.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.UpperObjStop, config.UpperObjectiveStop.Value);
                Logging.Info($"[CPLEX 參數設定] UpperObjectiveStop={config.UpperObjectiveStop.Value}");
            }
            #endregion

            #region 執行資源
            if (config.AuxiliaryRootThreads.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.AuxRootThreads, config.AuxiliaryRootThreads.Value);
                Logging.Info($"[CPLEX 參數設定] AuxiliaryRootThreads={config.AuxiliaryRootThreads.Value}");
            }
            if (!string.IsNullOrWhiteSpace(config.CpuMask))
            {
                Model.SetParam(Param.CPUmask, config.CpuMask);
                Logging.Info($"[CPLEX 參數設定] CpuMask={config.CpuMask}");
            }
            if (config.MemoryEmphasis.HasValue)
            {
                Model.SetParam(Param.Emphasis.Memory, config.MemoryEmphasis.Value);
                Logging.Info($"[CPLEX 參數設定] MemoryEmphasis={config.MemoryEmphasis.Value}");
            }
            if (!string.IsNullOrWhiteSpace(config.WorkDir))
            {
                Model.SetParam(Param.WorkDir, config.WorkDir);
                Logging.Info($"[CPLEX 參數設定] WorkDir={config.WorkDir}");
            }
            #endregion

            #region 搜尋策略 · emphasis 與搜尋分支
            if (config.AdvancedStart.HasValue)
            {
                Model.SetParam(Param.Advance, config.AdvancedStart.Value);
                Logging.Info($"[CPLEX 參數設定] AdvancedStart={config.AdvancedStart.Value}");
            }
            if (config.BacktrackTolerance.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.Backtrack, config.BacktrackTolerance.Value);
                Logging.Info($"[CPLEX 參數設定] BacktrackTolerance={config.BacktrackTolerance.Value}");
            }
            if (config.BestBoundInterval.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.BBInterval, config.BestBoundInterval.Value);
                Logging.Info($"[CPLEX 參數設定] BestBoundInterval={config.BestBoundInterval.Value}");
            }
            if (config.KappaStatistics.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.KappaStats, config.KappaStatistics.Value);
                Logging.Info($"[CPLEX 參數設定] KappaStatistics={config.KappaStatistics.Value}");
            }
            if (config.OptimalityTarget.HasValue)
            {
                Model.SetParam(Param.OptimalityTarget, config.OptimalityTarget.Value);
                Logging.Info($"[CPLEX 參數設定] OptimalityTarget={config.OptimalityTarget.Value}");
            }
            if (config.PriorityOrderType.HasValue)
            {
                Model.SetParam(Param.MIP.OrderType, config.PriorityOrderType.Value);
                Logging.Info($"[CPLEX 參數設定] PriorityOrderType={config.PriorityOrderType.Value}");
            }
            if (config.SolutionType.HasValue)
            {
                Model.SetParam(Param.SolutionType, config.SolutionType.Value);
                Logging.Info($"[CPLEX 參數設定] SolutionType={config.SolutionType.Value}");
            }
            if (config.StrongBranchingCandidateLimit.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.StrongCand, config.StrongBranchingCandidateLimit.Value);
                Logging.Info($"[CPLEX 參數設定] StrongBranchingCandidateLimit={config.StrongBranchingCandidateLimit.Value}");
            }
            if (config.StrongBranchingIterationLimit.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.StrongIt, config.StrongBranchingIterationLimit.Value);
                Logging.Info($"[CPLEX 參數設定] StrongBranchingIterationLimit={config.StrongBranchingIterationLimit.Value}");
            }
            if (config.UsePriorityOrder.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.Order, config.UsePriorityOrder.Value);
                Logging.Info($"[CPLEX 參數設定] UsePriorityOrder={config.UsePriorityOrder.Value}");
            }
            #endregion

            #region 搜尋策略 · 啟發式與 solution polishing
            if (config.CardinalityLocalSearch.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.CardLs, config.CardinalityLocalSearch.Value);
                Logging.Info($"[CPLEX 參數設定] CardinalityLocalSearch={config.CardinalityLocalSearch.Value}");
            }
            if (config.FeasibilityPumpHeuristic.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.FPHeur, config.FeasibilityPumpHeuristic.Value);
                Logging.Info($"[CPLEX 參數設定] FeasibilityPumpHeuristic={config.FeasibilityPumpHeuristic.Value}");
            }
            if (config.HeuristicFrequency.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.HeuristicFreq, config.HeuristicFrequency.Value);
                Logging.Info($"[CPLEX 參數設定] HeuristicFrequency={config.HeuristicFrequency.Value}");
            }
            if (config.LocalBranchingHeuristic.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.LBHeur, config.LocalBranchingHeuristic.Value);
                Logging.Info($"[CPLEX 參數設定] LocalBranchingHeuristic={config.LocalBranchingHeuristic.Value}");
            }
            if (config.PolishAfterAbsoluteMipGap.HasValue)
            {
                Model.SetParam(Param.MIP.PolishAfter.AbsMIPGap, config.PolishAfterAbsoluteMipGap.Value);
                Logging.Info($"[CPLEX 參數設定] PolishAfterAbsoluteMipGap={config.PolishAfterAbsoluteMipGap.Value}");
            }
            if (config.PolishAfterDetTime.HasValue)
            {
                Model.SetParam(Param.MIP.PolishAfter.DetTime, config.PolishAfterDetTime.Value);
                Logging.Info($"[CPLEX 參數設定] PolishAfterDetTime={config.PolishAfterDetTime.Value}");
            }
            if (config.PolishAfterMipGap.HasValue)
            {
                Model.SetParam(Param.MIP.PolishAfter.MIPGap, config.PolishAfterMipGap.Value);
                Logging.Info($"[CPLEX 參數設定] PolishAfterMipGap={config.PolishAfterMipGap.Value}");
            }
            if (config.PolishAfterNodes.HasValue)
            {
                Model.SetParam(Param.MIP.PolishAfter.Nodes, config.PolishAfterNodes.Value);
                Logging.Info($"[CPLEX 參數設定] PolishAfterNodes={config.PolishAfterNodes.Value}");
            }
            if (config.PolishAfterSolutions.HasValue)
            {
                Model.SetParam(Param.MIP.PolishAfter.Solutions, config.PolishAfterSolutions.Value);
                Logging.Info($"[CPLEX 參數設定] PolishAfterSolutions={config.PolishAfterSolutions.Value}");
            }
            if (config.RepairTries.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.RepairTries, config.RepairTries.Value);
                Logging.Info($"[CPLEX 參數設定] RepairTries={config.RepairTries.Value}");
            }
            #endregion

            #region 搜尋策略 · 切割平面
            if (config.AggregationLimitForCut.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.AggForCut, config.AggregationLimitForCut.Value);
                Logging.Info($"[CPLEX 參數設定] AggregationLimitForCut={config.AggregationLimitForCut.Value}");
            }
            if (config.BqpCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.BQP, config.BqpCuts.Value);
                Logging.Info($"[CPLEX 參數設定] BqpCuts={config.BqpCuts.Value}");
            }
            if (config.DisjunctiveCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.Disjunctive, config.DisjunctiveCuts.Value);
                Logging.Info($"[CPLEX 參數設定] DisjunctiveCuts={config.DisjunctiveCuts.Value}");
            }
            if (config.EachCutLimit.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.EachCutLimit, config.EachCutLimit.Value);
                Logging.Info($"[CPLEX 參數設定] EachCutLimit={config.EachCutLimit.Value}");
            }
            if (config.FlowPathCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.PathCut, config.FlowPathCuts.Value);
                Logging.Info($"[CPLEX 參數設定] FlowPathCuts={config.FlowPathCuts.Value}");
            }
            if (config.GomoryCandidateLimit.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.GomoryCand, config.GomoryCandidateLimit.Value);
                Logging.Info($"[CPLEX 參數設定] GomoryCandidateLimit={config.GomoryCandidateLimit.Value}");
            }
            if (config.GomoryPassLimit.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.GomoryPass, config.GomoryPassLimit.Value);
                Logging.Info($"[CPLEX 參數設定] GomoryPassLimit={config.GomoryPassLimit.Value}");
            }
            if (config.GubCoverCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.GUBCovers, config.GubCoverCuts.Value);
                Logging.Info($"[CPLEX 參數設定] GubCoverCuts={config.GubCoverCuts.Value}");
            }
            if (config.ImpliedBoundCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.Implied, config.ImpliedBoundCuts.Value);
                Logging.Info($"[CPLEX 參數設定] ImpliedBoundCuts={config.ImpliedBoundCuts.Value}");
            }
            if (config.LiftAndProjectCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.LiftProj, config.LiftAndProjectCuts.Value);
                Logging.Info($"[CPLEX 參數設定] LiftAndProjectCuts={config.LiftAndProjectCuts.Value}");
            }
            if (config.LocalImpliedBoundCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.LocalImplied, config.LocalImpliedBoundCuts.Value);
                Logging.Info($"[CPLEX 參數設定] LocalImpliedBoundCuts={config.LocalImpliedBoundCuts.Value}");
            }
            if (config.McfCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.MCFCut, config.McfCuts.Value);
                Logging.Info($"[CPLEX 參數設定] McfCuts={config.McfCuts.Value}");
            }
            if (config.NodeCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.Nodecuts, config.NodeCuts.Value);
                Logging.Info($"[CPLEX 參數設定] NodeCuts={config.NodeCuts.Value}");
            }
            if (config.RltCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.RLT, config.RltCuts.Value);
                Logging.Info($"[CPLEX 參數設定] RltCuts={config.RltCuts.Value}");
            }
            if (config.ZeroHalfCuts.HasValue)
            {
                Model.SetParam(Param.MIP.Cuts.ZeroHalfCut, config.ZeroHalfCuts.Value);
                Logging.Info($"[CPLEX 參數設定] ZeroHalfCuts={config.ZeroHalfCuts.Value}");
            }
            #endregion

            #region 搜尋策略 · 前處理
            if (config.AggregatorFill.HasValue)
            {
                Model.SetParam(Param.Preprocessing.Fill, config.AggregatorFill.Value);
                Logging.Info($"[CPLEX 參數設定] AggregatorFill={config.AggregatorFill.Value}");
            }
            if (config.AggregatorLimit.HasValue)
            {
                Model.SetParam(Param.Preprocessing.Aggregator, config.AggregatorLimit.Value);
                Logging.Info($"[CPLEX 參數設定] AggregatorLimit={config.AggregatorLimit.Value}");
            }
            if (config.BoundStrengthening.HasValue)
            {
                Model.SetParam(Param.Preprocessing.BoundStrength, config.BoundStrengthening.Value);
                Logging.Info($"[CPLEX 參數設定] BoundStrengthening={config.BoundStrengthening.Value}");
            }
            if (config.CoefficientReduction.HasValue)
            {
                Model.SetParam(Param.Preprocessing.CoeffReduce, config.CoefficientReduction.Value);
                Logging.Info($"[CPLEX 參數設定] CoefficientReduction={config.CoefficientReduction.Value}");
            }
            if (config.DependencyCheck.HasValue)
            {
                Model.SetParam(Param.Preprocessing.Dependency, config.DependencyCheck.Value);
                Logging.Info($"[CPLEX 參數設定] DependencyCheck={config.DependencyCheck.Value}");
            }
            if (config.LpFolding.HasValue)
            {
                Model.SetParam(Param.Preprocessing.Folding, config.LpFolding.Value);
                Logging.Info($"[CPLEX 參數設定] LpFolding={config.LpFolding.Value}");
            }
            if (config.NodePresolve.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.PresolveNode, config.NodePresolve.Value);
                Logging.Info($"[CPLEX 參數設定] NodePresolve={config.NodePresolve.Value}");
            }
            if (config.PresolveDual.HasValue)
            {
                Model.SetParam(Param.Preprocessing.Dual, config.PresolveDual.Value);
                Logging.Info($"[CPLEX 參數設定] PresolveDual={config.PresolveDual.Value}");
            }
            if (config.PresolvePasses.HasValue)
            {
                Model.SetParam(Param.Preprocessing.NumPass, config.PresolvePasses.Value);
                Logging.Info($"[CPLEX 參數設定] PresolvePasses={config.PresolvePasses.Value}");
            }
            if (config.PresolveReduce.HasValue)
            {
                Model.SetParam(Param.Preprocessing.Reduce, config.PresolveReduce.Value);
                Logging.Info($"[CPLEX 參數設定] PresolveReduce={config.PresolveReduce.Value}");
            }
            if (config.PresolveReformulations.HasValue)
            {
                Model.SetParam(Param.Preprocessing.Reformulations, config.PresolveReformulations.Value);
                Logging.Info($"[CPLEX 參數設定] PresolveReformulations={config.PresolveReformulations.Value}");
            }
            if (config.RelaxedLpPresolve.HasValue)
            {
                Model.SetParam(Param.Preprocessing.Relax, config.RelaxedLpPresolve.Value);
                Logging.Info($"[CPLEX 參數設定] RelaxedLpPresolve={config.RelaxedLpPresolve.Value}");
            }
            if (config.RepeatPresolve.HasValue)
            {
                Model.SetParam(Param.Preprocessing.RepeatPresolve, config.RepeatPresolve.Value);
                Logging.Info($"[CPLEX 參數設定] RepeatPresolve={config.RepeatPresolve.Value}");
            }
            if (config.Scaling.HasValue)
            {
                Model.SetParam(Param.Read.Scale, config.Scaling.Value);
                Logging.Info($"[CPLEX 參數設定] Scaling={config.Scaling.Value}");
            }
            #endregion

            #region 搜尋策略 · root 與節點的 LP 演算法
            if (config.BarrierColumnNonzeros.HasValue)
            {
                Model.SetParam(Param.Barrier.ColNonzeros, config.BarrierColumnNonzeros.Value);
                Logging.Info($"[CPLEX 參數設定] BarrierColumnNonzeros={config.BarrierColumnNonzeros.Value}");
            }
            if (config.BarrierCorrectionLimit.HasValue)
            {
                Model.SetParam(Param.Barrier.Limits.Corrections, config.BarrierCorrectionLimit.Value);
                Logging.Info($"[CPLEX 參數設定] BarrierCorrectionLimit={config.BarrierCorrectionLimit.Value}");
            }
            if (config.BarrierCrossover.HasValue)
            {
                Model.SetParam(Param.Barrier.Crossover, config.BarrierCrossover.Value);
                Logging.Info($"[CPLEX 參數設定] BarrierCrossover={config.BarrierCrossover.Value}");
            }
            if (config.BarrierGrowthLimit.HasValue)
            {
                Model.SetParam(Param.Barrier.Limits.Growth, config.BarrierGrowthLimit.Value);
                Logging.Info($"[CPLEX 參數設定] BarrierGrowthLimit={config.BarrierGrowthLimit.Value}");
            }
            if (config.BarrierObjectiveRange.HasValue)
            {
                Model.SetParam(Param.Barrier.Limits.ObjRange, config.BarrierObjectiveRange.Value);
                Logging.Info($"[CPLEX 參數設定] BarrierObjectiveRange={config.BarrierObjectiveRange.Value}");
            }
            if (config.BarrierOrdering.HasValue)
            {
                Model.SetParam(Param.Barrier.Ordering, config.BarrierOrdering.Value);
                Logging.Info($"[CPLEX 參數設定] BarrierOrdering={config.BarrierOrdering.Value}");
            }
            if (config.BarrierStartAlgorithm.HasValue)
            {
                Model.SetParam(Param.Barrier.StartAlg, config.BarrierStartAlgorithm.Value);
                Logging.Info($"[CPLEX 參數設定] BarrierStartAlgorithm={config.BarrierStartAlgorithm.Value}");
            }
            if (config.DualSimplexPricing.HasValue)
            {
                Model.SetParam(Param.Simplex.DGradient, config.DualSimplexPricing.Value);
                Logging.Info($"[CPLEX 參數設定] DualSimplexPricing={config.DualSimplexPricing.Value}");
            }
            if (config.MarkowitzTolerance.HasValue)
            {
                Model.SetParam(Param.Simplex.Tolerances.Markowitz, config.MarkowitzTolerance.Value);
                Logging.Info($"[CPLEX 參數設定] MarkowitzTolerance={config.MarkowitzTolerance.Value}");
            }
            if (config.NetworkExtractionLevel.HasValue)
            {
                Model.SetParam(Param.Network.NetFind, config.NetworkExtractionLevel.Value);
                Logging.Info($"[CPLEX 參數設定] NetworkExtractionLevel={config.NetworkExtractionLevel.Value}");
            }
            if (config.NetworkPricing.HasValue)
            {
                Model.SetParam(Param.Network.Pricing, config.NetworkPricing.Value);
                Logging.Info($"[CPLEX 參數設定] NetworkPricing={config.NetworkPricing.Value}");
            }
            if (config.PrimalSimplexPricing.HasValue)
            {
                Model.SetParam(Param.Simplex.PGradient, config.PrimalSimplexPricing.Value);
                Logging.Info($"[CPLEX 參數設定] PrimalSimplexPricing={config.PrimalSimplexPricing.Value}");
            }
            if (config.SiftingAlgorithm.HasValue)
            {
                Model.SetParam(Param.Sifting.Algorithm, config.SiftingAlgorithm.Value);
                Logging.Info($"[CPLEX 參數設定] SiftingAlgorithm={config.SiftingAlgorithm.Value}");
            }
            if (config.SiftingFromSimplex.HasValue)
            {
                Model.SetParam(Param.Sifting.Simplex, config.SiftingFromSimplex.Value);
                Logging.Info($"[CPLEX 參數設定] SiftingFromSimplex={config.SiftingFromSimplex.Value}");
            }
            if (config.SimplexCrash.HasValue)
            {
                Model.SetParam(Param.Simplex.Crash, config.SimplexCrash.Value);
                Logging.Info($"[CPLEX 參數設定] SimplexCrash={config.SimplexCrash.Value}");
            }
            if (config.SimplexDynamicRows.HasValue)
            {
                Model.SetParam(Param.Simplex.DynamicRows, config.SimplexDynamicRows.Value);
                Logging.Info($"[CPLEX 參數設定] SimplexDynamicRows={config.SimplexDynamicRows.Value}");
            }
            if (config.SimplexPerturbationConstant.HasValue)
            {
                Model.SetParam(Param.Simplex.Perturbation.Constant, config.SimplexPerturbationConstant.Value);
                Logging.Info($"[CPLEX 參數設定] SimplexPerturbationConstant={config.SimplexPerturbationConstant.Value}");
            }
            if (config.SimplexPerturbationIndicator.HasValue)
            {
                Model.SetParam(Param.Simplex.Perturbation.Indicator, config.SimplexPerturbationIndicator.Value);
                Logging.Info($"[CPLEX 參數設定] SimplexPerturbationIndicator={config.SimplexPerturbationIndicator.Value}");
            }
            if (config.SimplexPerturbationLimit.HasValue)
            {
                Model.SetParam(Param.Simplex.Limits.Perturbation, config.SimplexPerturbationLimit.Value);
                Logging.Info($"[CPLEX 參數設定] SimplexPerturbationLimit={config.SimplexPerturbationLimit.Value}");
            }
            if (config.SimplexPricingCandidateList.HasValue)
            {
                Model.SetParam(Param.Simplex.Pricing, config.SimplexPricingCandidateList.Value);
                Logging.Info($"[CPLEX 參數設定] SimplexPricingCandidateList={config.SimplexPricingCandidateList.Value}");
            }
            if (config.SimplexRefactorFrequency.HasValue)
            {
                Model.SetParam(Param.Simplex.Refactor, config.SimplexRefactorFrequency.Value);
                Logging.Info($"[CPLEX 參數設定] SimplexRefactorFrequency={config.SimplexRefactorFrequency.Value}");
            }
            if (config.SimplexSingularityLimit.HasValue)
            {
                Model.SetParam(Param.Simplex.Limits.Singularity, config.SimplexSingularityLimit.Value);
                Logging.Info($"[CPLEX 參數設定] SimplexSingularityLimit={config.SimplexSingularityLimit.Value}");
            }
            #endregion

            #region 搜尋策略 · 特定模型類別
            if (config.BendersFeasibilityCutTol.HasValue)
            {
                Model.SetParam(Param.Benders.Tolerances.FeasibilityCut, config.BendersFeasibilityCutTol.Value);
                Logging.Info($"[CPLEX 參數設定] BendersFeasibilityCutTol={config.BendersFeasibilityCutTol.Value}");
            }
            if (config.BendersOptimalityCutTol.HasValue)
            {
                Model.SetParam(Param.Benders.Tolerances.OptimalityCut, config.BendersOptimalityCutTol.Value);
                Logging.Info($"[CPLEX 參數設定] BendersOptimalityCutTol={config.BendersOptimalityCutTol.Value}");
            }
            if (config.BendersStrategy.HasValue)
            {
                Model.SetParam(Param.Benders.Strategy, config.BendersStrategy.Value);
                Logging.Info($"[CPLEX 參數設定] BendersStrategy={config.BendersStrategy.Value}");
            }
            if (config.BendersWorkerAlgorithm.HasValue)
            {
                Model.SetParam(Param.Benders.WorkerAlgorithm, config.BendersWorkerAlgorithm.Value);
                Logging.Info($"[CPLEX 參數設定] BendersWorkerAlgorithm={config.BendersWorkerAlgorithm.Value}");
            }
            if (config.CalculateQcpDuals.HasValue)
            {
                Model.SetParam(Param.Preprocessing.QCPDuals, config.CalculateQcpDuals.Value);
                Logging.Info($"[CPLEX 參數設定] CalculateQcpDuals={config.CalculateQcpDuals.Value}");
            }
            if (config.FeasOptMode.HasValue)
            {
                Model.SetParam(Param.Feasopt.Mode, config.FeasOptMode.Value);
                Logging.Info($"[CPLEX 參數設定] FeasOptMode={config.FeasOptMode.Value}");
            }
            if (config.MiqcpStrategy.HasValue)
            {
                Model.SetParam(Param.MIP.Strategy.MIQCPStrat, config.MiqcpStrategy.Value);
                Logging.Info($"[CPLEX 參數設定] MiqcpStrategy={config.MiqcpStrategy.Value}");
            }
            if (config.QpMakePsd.HasValue)
            {
                Model.SetParam(Param.Preprocessing.QPMakePSD, config.QpMakePsd.Value);
                Logging.Info($"[CPLEX 參數設定] QpMakePsd={config.QpMakePsd.Value}");
            }
            if (config.QpToLinear.HasValue)
            {
                Model.SetParam(Param.Preprocessing.QToLin, config.QpToLinear.Value);
                Logging.Info($"[CPLEX 參數設定] QpToLinear={config.QpToLinear.Value}");
            }
            if (config.Sos1Reformulation.HasValue)
            {
                Model.SetParam(Param.Preprocessing.SOS1Reform, config.Sos1Reformulation.Value);
                Logging.Info($"[CPLEX 參數設定] Sos1Reformulation={config.Sos1Reformulation.Value}");
            }
            if (config.Sos2Reformulation.HasValue)
            {
                Model.SetParam(Param.Preprocessing.SOS2Reform, config.Sos2Reformulation.Value);
                Logging.Info($"[CPLEX 參數設定] Sos2Reformulation={config.Sos2Reformulation.Value}");
            }
            if (config.SubMipNodeAlgorithm.HasValue)
            {
                Model.SetParam(Param.MIP.SubMIP.SubAlg, config.SubMipNodeAlgorithm.Value);
                Logging.Info($"[CPLEX 參數設定] SubMipNodeAlgorithm={config.SubMipNodeAlgorithm.Value}");
            }
            if (config.SubMipNodeLimit.HasValue)
            {
                Model.SetParam(Param.MIP.SubMIP.NodeLimit, config.SubMipNodeLimit.Value);
                Logging.Info($"[CPLEX 參數設定] SubMipNodeLimit={config.SubMipNodeLimit.Value}");
            }
            if (config.SubMipRootAlgorithm.HasValue)
            {
                Model.SetParam(Param.MIP.SubMIP.StartAlg, config.SubMipRootAlgorithm.Value);
                Logging.Info($"[CPLEX 參數設定] SubMipRootAlgorithm={config.SubMipRootAlgorithm.Value}");
            }
            if (config.SubMipScaling.HasValue)
            {
                Model.SetParam(Param.MIP.SubMIP.Scale, config.SubMipScaling.Value);
                Logging.Info($"[CPLEX 參數設定] SubMipScaling={config.SubMipScaling.Value}");
            }
            #endregion

            #region 非 tuning 參數
            if (config.BarrierDisplay.HasValue)
            {
                Model.SetParam(Param.Barrier.Display, config.BarrierDisplay.Value);
                Logging.Info($"[CPLEX 參數設定] BarrierDisplay={config.BarrierDisplay.Value}");
            }
            if (config.CloneLog.HasValue)
            {
                Model.SetParam(Param.Output.CloneLog, config.CloneLog.Value);
                Logging.Info($"[CPLEX 參數設定] CloneLog={config.CloneLog.Value}");
            }
#pragma warning disable CS0618 // 保留公開 API 對過時 CPLEX 參數的支援
            if (config.ColumnRead.HasValue)
            {
                Model.SetParam(Param.Read.Variables, config.ColumnRead.Value);
                Logging.Info($"[CPLEX 參數設定] ColumnRead={config.ColumnRead.Value}");
            }
#pragma warning restore CS0618
            if (config.ConflictAlgorithm.HasValue)
            {
                Model.SetParam(Param.Conflict.Algorithm, config.ConflictAlgorithm.Value);
                Logging.Info($"[CPLEX 參數設定] ConflictAlgorithm={config.ConflictAlgorithm.Value}");
            }
            if (config.ConflictDisplay.HasValue)
            {
                Model.SetParam(Param.Conflict.Display, config.ConflictDisplay.Value);
                Logging.Info($"[CPLEX 參數設定] ConflictDisplay={config.ConflictDisplay.Value}");
            }
            if (config.DataCheck.HasValue)
            {
                Model.SetParam(Param.Read.DataCheck, config.DataCheck.Value);
                Logging.Info($"[CPLEX 參數設定] DataCheck={config.DataCheck.Value}");
            }
            if (!string.IsNullOrWhiteSpace(config.FileEncoding))
            {
                Model.SetParam(Param.Read.FileEncoding, config.FileEncoding);
                Logging.Info($"[CPLEX 參數設定] FileEncoding={config.FileEncoding}");
            }
            if (!string.IsNullOrWhiteSpace(config.IntSolFilePrefix))
            {
                Model.SetParam(Param.Output.IntSolFilePrefix, config.IntSolFilePrefix);
                Logging.Info($"[CPLEX 參數設定] IntSolFilePrefix={config.IntSolFilePrefix}");
            }
            if (config.MipDisplay.HasValue)
            {
                Model.SetParam(Param.MIP.Display, config.MipDisplay.Value);
                Logging.Info($"[CPLEX 參數設定] MipDisplay={config.MipDisplay.Value}");
            }
            if (config.MipInterval.HasValue)
            {
                Model.SetParam(Param.MIP.Interval, config.MipInterval.Value);
                Logging.Info($"[CPLEX 參數設定] MipInterval={config.MipInterval.Value}");
            }
            if (config.MpsLongNumerics.HasValue)
            {
                Model.SetParam(Param.Output.MPSLong, config.MpsLongNumerics.Value);
                Logging.Info($"[CPLEX 參數設定] MpsLongNumerics={config.MpsLongNumerics.Value}");
            }
            if (config.MultiObjectiveDisplay.HasValue)
            {
                Model.SetParam(Param.MultiObjective.Display, config.MultiObjectiveDisplay.Value);
                Logging.Info($"[CPLEX 參數設定] MultiObjectiveDisplay={config.MultiObjectiveDisplay.Value}");
            }
            if (config.NetworkDisplay.HasValue)
            {
                Model.SetParam(Param.Network.Display, config.NetworkDisplay.Value);
                Logging.Info($"[CPLEX 參數設定] NetworkDisplay={config.NetworkDisplay.Value}");
            }
#pragma warning disable CS0618 // 保留公開 API 對過時 CPLEX 參數的支援
            if (config.NonzeroRead.HasValue)
            {
                Model.SetParam(Param.Read.Nonzeros, config.NonzeroRead.Value);
                Logging.Info($"[CPLEX 參數設定] NonzeroRead={config.NonzeroRead.Value}");
            }
#pragma warning restore CS0618
            if (config.ParamDisplay.HasValue)
            {
                Model.SetParam(Param.ParamDisplay, config.ParamDisplay.Value);
                Logging.Info($"[CPLEX 參數設定] ParamDisplay={config.ParamDisplay.Value}");
            }
            if (config.PopulateLimit.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.Populate, config.PopulateLimit.Value);
                Logging.Info($"[CPLEX 參數設定] PopulateLimit={config.PopulateLimit.Value}");
            }
            if (config.ProbeDetTimeLimit.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.ProbeDetTime, config.ProbeDetTimeLimit.Value);
                Logging.Info($"[CPLEX 參數設定] ProbeDetTimeLimit={config.ProbeDetTimeLimit.Value}");
            }
            if (config.ProbeTimeLimit.HasValue)
            {
                Model.SetParam(Param.MIP.Limits.ProbeTime, config.ProbeTimeLimit.Value);
                Logging.Info($"[CPLEX 參數設定] ProbeTimeLimit={config.ProbeTimeLimit.Value}");
            }
#pragma warning disable CS0618 // 保留公開 API 對過時 CPLEX 參數的支援
            if (config.QpNonzeroRead.HasValue)
            {
                Model.SetParam(Param.Read.QPNonzeros, config.QpNonzeroRead.Value);
                Logging.Info($"[CPLEX 參數設定] QpNonzeroRead={config.QpNonzeroRead.Value}");
            }
#pragma warning restore CS0618
            if (config.ReadWarningLimit.HasValue)
            {
                Model.SetParam(Param.Read.WarningLimit, config.ReadWarningLimit.Value);
                Logging.Info($"[CPLEX 參數設定] ReadWarningLimit={config.ReadWarningLimit.Value}");
            }
            if (config.Record.HasValue)
            {
                Model.SetParam(Param.Record, config.Record.Value);
                Logging.Info($"[CPLEX 參數設定] Record={config.Record.Value}");
            }
            if (config.SiftingDisplay.HasValue)
            {
                Model.SetParam(Param.Sifting.Display, config.SiftingDisplay.Value);
                Logging.Info($"[CPLEX 參數設定] SiftingDisplay={config.SiftingDisplay.Value}");
            }
            if (config.SimplexDisplay.HasValue)
            {
                Model.SetParam(Param.Simplex.Display, config.SimplexDisplay.Value);
                Logging.Info($"[CPLEX 參數設定] SimplexDisplay={config.SimplexDisplay.Value}");
            }
            if (config.SolutionPoolAbsGap.HasValue)
            {
                Model.SetParam(Param.MIP.Pool.AbsGap, config.SolutionPoolAbsGap.Value);
                Logging.Info($"[CPLEX 參數設定] SolutionPoolAbsGap={config.SolutionPoolAbsGap.Value}");
            }
            if (config.SolutionPoolCapacity.HasValue)
            {
                Model.SetParam(Param.MIP.Pool.Capacity, config.SolutionPoolCapacity.Value);
                Logging.Info($"[CPLEX 參數設定] SolutionPoolCapacity={config.SolutionPoolCapacity.Value}");
            }
            if (config.SolutionPoolIntensity.HasValue)
            {
                Model.SetParam(Param.MIP.Pool.Intensity, config.SolutionPoolIntensity.Value);
                Logging.Info($"[CPLEX 參數設定] SolutionPoolIntensity={config.SolutionPoolIntensity.Value}");
            }
            if (config.SolutionPoolRelGap.HasValue)
            {
                Model.SetParam(Param.MIP.Pool.RelGap, config.SolutionPoolRelGap.Value);
                Logging.Info($"[CPLEX 參數設定] SolutionPoolRelGap={config.SolutionPoolRelGap.Value}");
            }
            if (config.SolutionPoolReplace.HasValue)
            {
                Model.SetParam(Param.MIP.Pool.Replace, config.SolutionPoolReplace.Value);
                Logging.Info($"[CPLEX 參數設定] SolutionPoolReplace={config.SolutionPoolReplace.Value}");
            }
            if (config.TuningDetTimeLimit.HasValue)
            {
                Model.SetParam(Param.Tune.DetTimeLimit, config.TuningDetTimeLimit.Value);
                Logging.Info($"[CPLEX 參數設定] TuningDetTimeLimit={config.TuningDetTimeLimit.Value}");
            }
            if (config.TuningDisplay.HasValue)
            {
                Model.SetParam(Param.Tune.Display, config.TuningDisplay.Value);
                Logging.Info($"[CPLEX 參數設定] TuningDisplay={config.TuningDisplay.Value}");
            }
            if (config.TuningMeasure.HasValue)
            {
                Model.SetParam(Param.Tune.Measure, config.TuningMeasure.Value);
                Logging.Info($"[CPLEX 參數設定] TuningMeasure={config.TuningMeasure.Value}");
            }
            if (config.TuningRepeat.HasValue)
            {
                Model.SetParam(Param.Tune.Repeat, config.TuningRepeat.Value);
                Logging.Info($"[CPLEX 參數設定] TuningRepeat={config.TuningRepeat.Value}");
            }
            if (config.TuningTimeLimit.HasValue)
            {
                Model.SetParam(Param.Tune.TimeLimit, config.TuningTimeLimit.Value);
                Logging.Info($"[CPLEX 參數設定] TuningTimeLimit={config.TuningTimeLimit.Value}");
            }
            if (config.WriteLevel.HasValue)
            {
                Model.SetParam(Param.Output.WriteLevel, config.WriteLevel.Value);
                Logging.Info($"[CPLEX 參數設定] WriteLevel={config.WriteLevel.Value}");
            }
            #endregion

        }

        #endregion
    }
}
