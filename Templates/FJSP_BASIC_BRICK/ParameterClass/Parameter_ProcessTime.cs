using OptimFoundation.Modeling;
using FJSP_BASIC_BRICK.SetClass;

namespace FJSP_BASIC_BRICK.ParameterClass
{
    /// <summary>加工時間（小時）。數學：p_{l,o,e}，值在 QTY。成員式：每維明寫名字 + 來源 set，QTY 自動補。</summary>
    [OptParam]
    [OptDim<Set_Lot>("Lot")]
    [OptDim<Set_Operation>("Operation")]
    [OptDim<Set_Eqp>("Eqp")]
    public partial class Parameter_ProcessTime { }
}
