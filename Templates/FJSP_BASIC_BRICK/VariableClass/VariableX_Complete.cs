using OptimFoundation.Modeling;
using FJSP_BASIC_BRICK.SetClass;

namespace FJSP_BASIC_BRICK.VariableClass
{
    /// <summary>作業完成時間（小時）。Model.md: Complete_{Lot,Operation}</summary>
    /// 
    [OptDim<Set_Lot>("Lot")]
    [OptDim<Set_Operation>("Operation")]
    [OptVar]
    public partial class VariableX_Complete { }
}
