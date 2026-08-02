using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>每件耗用工時（小時/件）。數學：MachineHours_{p,m}，值在 QTY。2D 參數（Product × Machine）。</summary>
    [OptParam]
    [OptDim<Set_Product>("Product")]
    [OptDim<Set_Machine>("Machine")]
    public sealed partial class Parameter_MachineHours { }
}
