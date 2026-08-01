using Tutorial.Constraint;
using Tutorial.Data;
using Tutorial.VariableClass;
using OptimFoundation.Core;
using OptimFoundation.Cplex;

namespace Tutorial.Model
{
    /// <summary>
    /// 模型積木：把「變數 + 目標式 + 全部限制式」組成一顆完整模型,plug 進 OptModel（solve / experiment 共用）。
    /// 各層仍是子積木——變數（Variable*）、目標式（ObjectiveFunction）、限制式（Constraint_*）——本類只負責「組裝順序」。
    /// 用法：new TutorialModel(data)，再把 CreateVariables / CreateModel 交給 OptModel，或呼 Build 一次組全。
    /// </summary>
    public class TutorialModel
    {
        private readonly Dataload _d;

        public TutorialModel(Dataload data) => _d = data;

        /// <summary>變數層（給 OptModel.AddVariables）：三種型別 X_/B_/I_，多維。</summary>
        public void CreateVariables(OptEngine engine)
        {
            engine.BuildVars<VariableX_Produce>(_d.PRODUCT, _d.DATE, _d.SHIFT);
            engine.BuildVars<VariableB_Setup>(_d.PRODUCT, _d.DATE, _d.SHIFT);
            engine.BuildVars<VariableI_Batch>(_d.PRODUCT, _d.DATE);
        }

        /// <summary>目標式 + 限制式層（給 OptModel.AddModel）：soft（SetupBudgetSoft）MUST 排在 Objective 之後。</summary>
        public void CreateModel(OptEngine engine)
        {
            new ObjectiveFunction(_d, engine).Build();

            new Constraint_Capacity(_d.PRODUCT, _d.MACHINE, _d.DATE, _d.SHIFT,
                _d.parameter_MachineHours, _d.parameter_Capacity, engine).Build();          // ≤
            new Constraint_Demand(_d.PRODUCT, _d.DATE, _d.SHIFT,
                _d.parameter_Demand, engine).Build();                                        // ≥
            new Constraint_BatchDef(_d.PRODUCT, _d.DATE, _d.SHIFT,
                _d.parameter_BatchSize, engine).Build();                                     // =
            new Constraint_SetupLink(_d.PRODUCT, _d.DATE, _d.SHIFT, _d.BigM, engine).Build();  // ≤ (Big-M)
            new Constraint_SetupBudgetSoft(_d.PRODUCT, _d.DATE, _d.SHIFT,
                _d.SetupBudget, _d.SetupPenalty, engine).Build();                            // soft 放鬆
        }

        /// <summary>一次組全（experiment / 手動建 engine 用）：變數 → 目標式 → 限制式。</summary>
        public void Build(OptEngine engine)
        {
            CreateVariables(engine);
            CreateModel(engine);
        }
    }
}
