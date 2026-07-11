using OptimFoundation.Modeling;

namespace FJSP_BASIC.VariableClass
{
    /// <summary>作業指派到機台 =1。Model.md: Assign_{Lot,Operation,Eqp}</summary>
    [OptVar(VarType.Binary, "Lot", "Operation", "Eqp")]
    public partial class VariableB_Assign { }
}
