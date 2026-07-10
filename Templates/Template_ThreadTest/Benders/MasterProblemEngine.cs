using OptimFoundation.Cplex;
using OptimFoundation.Core;
using ThreadTest.Data;
using ThreadTest.VariableClass;

namespace ThreadTest.Benders
{
    /// <summary>
    /// Benders 主問題引擎（MILP）。
    ///
    /// 主問題：
    ///   min  Σ_s fc[s] * Y[s]  +  θ
    ///   s.t. Σ_s Y[s] ≥ 1            （至少開設一個貨源）
    ///        θ ≥ 0
    ///        Y[s] ∈ {0,1}
    ///   + Benders 最佳性切割（每輪迭代追加）
    ///
    /// Benders Optimality Cut：
    ///   θ ≥ Σ_d demand[d]*u[d] + Σ_s supply[s]*v[s]*Y[s]
    ///
    ///   其中 v[s] = sub 供應約束 dual（≤0），u[d] = sub 需求約束 dual（≥0）
    ///
    /// 整理後移項（係數 -v[s] ≥ 0）：
    ///   Σ_s supply[s]*(-v[s])*Y[s]  +  θ  ≥  Σ_d demand[d]*u[d]
    /// </summary>
    public class MasterProblemEngine : OptEngine
    {
        private BendersDataload _data;
        private const string ThetaIndex = "theta";

        public MasterProblemEngine(CplexConfig config) : base(config) { }

        // ─────────────────────────────────────────
        // 初始化（只做一次）
        // ─────────────────────────────────────────

        public void InitializeVariables(BendersDataload data)
        {
            _data = data;
            BuildBVs<VariableY_Open>(data.Sources);
            BuildCVs<VariableX_Theta>(new List<string> { ThetaIndex });
        }

        public void BuildInitialModel()
        {
            // 目標式：min Σ fc[s]*Y[s] + θ
            foreach (var s in _data.Sources)
                AddLHS(_data.FixedCost[s], new VariableY_Open { Source = s });
            AddLHS(1.0, new VariableX_Theta { Index = ThetaIndex });
            CreateMinimize();

            // 至少開設一個貨源
            foreach (var s in _data.Sources)
                AddLHS(1, new VariableY_Open { Source = s });
            CreateGreatEqual(1, "AtLeastOneSource");

            // θ 的下界由 lb=0 保證（BuildCVs 預設 lb=0）
        }

        // ─────────────────────────────────────────
        // 每輪追加 Benders Optimality Cut
        // ─────────────────────────────────────────

        public void AddOptimalityCut(
            Dictionary<string, double> supplyDuals,
            Dictionary<string, double> demandDuals,
            int iteration)
        {
            // LHS：Σ_s supply[s]*(-v[s])*Y[s]  +  θ
            foreach (var s in _data.Sources)
            {
                double coef = _data.Supply[s] * (-supplyDuals[s]);
                if (Math.Abs(coef) > 1e-10)
                    AddLHS(coef, new VariableY_Open { Source = s });
            }
            AddLHS(1.0, new VariableX_Theta { Index = ThetaIndex });

            // RHS：Σ_d demand[d]*u[d]
            double rhs = _data.Dests.Sum(d => _data.Demand[d] * demandDuals[d]);
            CreateGreatEqual(rhs, $"BendersCut_{iteration}");
        }

        // ─────────────────────────────────────────
        // 結果讀取
        // ─────────────────────────────────────────

        public Dictionary<string, double> GetYValues()
            => _data.Sources.ToDictionary(
                s => s,
                s => GetVariableValue(new VariableY_Open { Source = s }.ToString()));

        public double GetThetaValue()
            => GetVariableValue(new VariableX_Theta { Index = ThetaIndex }.ToString());
    }
}
