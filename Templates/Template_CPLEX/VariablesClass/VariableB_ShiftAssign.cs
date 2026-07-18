using OptimFoundation.Modeling;
using SandBox.SetClass;

namespace SandBox.VariableClass
{
    /// <summary>員工 e 在日期 d 排入班別 g =1。Assign_{Date,Employee,Group}。</summary>
    [OptDim<Set_Date>("Date")]
    [OptDim<Set_Employee>("Employee")]
    [OptDim<Set_Group>("Group")]
    [OptVar]
    public partial class VariableB_ShiftAssign;
}
