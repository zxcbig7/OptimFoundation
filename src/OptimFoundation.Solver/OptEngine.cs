using System;
using System.Collections.Generic;
using System.Linq;
using OptimFoundation.Core;
using SolverModel = Solver.Model;
using SolverAlgo = Solver.Algorithm;

namespace OptimFoundation.Solver
{
    public class OptEngine : EngineBase<SolverModel.Modeler, SolverModel.Variable, SolverModel.LinearExpr, SolverModel.Constraint>
    {
        private bool   _exportLp;
        private bool   _exportMps;
        private bool   _exportSol;
        private bool   _enableLog;
        private string _modelName = "Model";
        private readonly string _startTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public OptEngine(SolverConfig config) : base(config) { }

        public void SetModelName(string name) => _modelName = name;

        public override void Configuration(ISolverConfig config)
        {
            Model = new SolverModel.Modeler();

            if (config is SolverConfig cfg)
            {
                _exportLp  = cfg.exportLP;
                _exportMps = cfg.exportMPS;
                _exportSol = cfg.exportSol;
                _enableLog = cfg.LogToConsole;

                if (_exportLp || _exportMps)
                    FolderDir.Model.CreateFolder();
                if (_exportSol)
                    FolderDir.Sol.CreateFolder();

                if (_exportLp)  Logging.Info("[Environment Setting] Enabled LP File (.lp) Output");
                if (_exportMps) Logging.Info("[Environment Setting] Enabled MPS File (.mps) Output");
                if (_exportSol) Logging.Info("[Environment Setting] Enabled Solution File (.sol) Output");
            }
        }

        protected override SolverModel.Variable AddVariable(string name, double lb, double ub, VarType type)
        {
            SolverModel.VarType solverType = type switch
            {
                VarType.Integer => SolverModel.VarType.Integer,
                VarType.Binary  => SolverModel.VarType.Binary,
                _               => SolverModel.VarType.Continuous
            };
            var v = Model.CreateVar(lb, ub, solverType, name);
            Variables[name] = v;
            return v;
        }

        protected override SolverModel.LinearExpr LinearExpr(IEnumerable<(double coef, SolverModel.Variable var)> terms)
        {
            var expr = new SolverModel.LinearExpr();
            foreach (var (coef, v) in terms)
                expr.Prod(coef, v);
            return expr;
        }

        protected override SolverModel.Constraint AddConstraint(string name, SolverModel.LinearExpr lhs, ConstraintSense sense, double rhs)
        {
            SolverModel.ConSense conSense = sense switch
            {
                ConstraintSense.LessEqual    => SolverModel.ConSense.LessEqual,
                ConstraintSense.Equal        => SolverModel.ConSense.Equal,
                ConstraintSense.GreaterEqual => SolverModel.ConSense.GreaterEqual,
                _                            => throw new ArgumentOutOfRangeException(nameof(sense))
            };
            return Model.AddConstraint(lhs, conSense, rhs, name);
        }

        protected override SolverModel.Constraint AddRangeConstraint(string name, SolverModel.LinearExpr expr, double lb, double ub)
        {
            Model.AddGe(expr, lb, name + "_ge");
            return Model.AddLe(expr, ub, name + "_le");
        }

        protected override void SetObjective(SolverModel.LinearExpr expr, ObjectiveSense sense)
        {
            if (sense == ObjectiveSense.Minimize)
                Model.SetMinimize(expr);
            else
                Model.SetMaximize(expr);
        }

        protected override void SetVariableBounds(SolverModel.Variable variable, double? lb, double? ub)
        {
            if (lb.HasValue) variable.LB = lb.Value;
            if (ub.HasValue) variable.UB = ub.Value;
        }

        public override void Build() => Configuration(Config);

        public override bool Solve()
        {
            if (_exportLp)
                Model.ExportLp(FolderDir.Model.GetFilePath($"{_modelName}_LP_{_startTime}.lp"));
            if (_exportMps)
                Model.ExportMps(FolderDir.Model.GetFilePath($"{_modelName}_MPS_{_startTime}.mps"));

            bool needsMilp = Model.Vars.Any(v =>
                v.Type == SolverModel.VarType.Integer || v.Type == SolverModel.VarType.Binary);

            SolverAlgo.SolveResult result;
            if (needsMilp)
            {
                var bnc = new SolverAlgo.BranchAndCut(Model)
                {
                    EnableLog   = _enableLog,
                    LogInterval = 100
                };
                if (Config is SolverConfig cfg && cfg.TimeLimit.HasValue)
                    bnc.TimeLimitSeconds = cfg.TimeLimit.Value;
                result = bnc.Solve();

                if (_enableLog)
                    Logging.Info($"[OptEngine] ObjVal={result.ObjectiveValue:G6}  " +
                                 $"BestBound={result.BestBound:G6}  " +
                                 $"MIPGap={result.MipGap * 100:F2}%");
            }
            else
            {
                result = new SolverAlgo.Simplex(Model) { EnableLog = _enableLog }.Solve();
                if (_enableLog)
                    Logging.Info($"[OptEngine] ObjVal={result.ObjectiveValue:G6}");
            }

            Model.UpdateResult(result);

            Status = result.Status switch
            {
                SolverAlgo.SolveStatus.Optimal    => SolveStatus.Optimal,
                SolverAlgo.SolveStatus.Infeasible => SolveStatus.Infeasible,
                SolverAlgo.SolveStatus.Unbounded  => SolveStatus.Unbounded,
                SolverAlgo.SolveStatus.TimedOut   => SolveStatus.TimeLimit,
                _                                  => SolveStatus.Error
            };

            // TimeLimit with a non-trivial incumbent is still usable
            bool ok = Status == SolveStatus.Optimal || Status == SolveStatus.Feasible
                   || (Status == SolveStatus.TimeLimit && !double.IsInfinity(result.ObjectiveValue) && result.ObjectiveValue != 0);

            if (ok && _exportSol)
                System.IO.File.WriteAllText(
                    FolderDir.Sol.GetFilePath($"{_modelName}_Solution_{_startTime}.sol"),
                    Model.ExportSol());

            return ok;
        }

        public override double GetObjectiveValue() => Model.objectiveVal;

        public override double GetVariableValue(string name) => Variables[name].OptVal;

        public string ExportLp(string? path = null)  => Model.ExportLp(path);
        public string ExportMps(string? path = null) => Model.ExportMps(path);

        public override void Dispose() { }
    }
}
