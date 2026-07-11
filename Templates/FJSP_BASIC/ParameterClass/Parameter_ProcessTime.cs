using OptimFoundation.Modeling;

namespace FJSP_BASIC.ParameterClass
{
    /// <summary>加工時間（小時）。Dim = (Lot, Operation, Eqp)，值在 QTY。</summary>
    [OptParam("Lot", "Operation", "Eqp")]
    public partial class Parameter_ProcessTime { }
}
