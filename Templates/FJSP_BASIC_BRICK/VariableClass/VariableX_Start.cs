using OptimFoundation.Modeling;
using FJSP_BASIC_BRICK.SetClass;

namespace FJSP_BASIC_BRICK.VariableClass
{
    /// <summary>作業開始時間（小時）。數學：Start_{l,o}。</summary>

    [OptVar]
    [OptDim<Set_Lot>("Lot")]
    [OptDim<Set_Operation>("Operation")]

    public partial class VariableX_Start { }
}
