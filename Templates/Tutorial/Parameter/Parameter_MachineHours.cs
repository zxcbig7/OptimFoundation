using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>MachineHours_{p,m}（小時/件，QTY）：每件耗用工時。</summary>
    [OptParam]
    [OptDim<string>("Product")]
    [OptDim<string>("Machine")]
    public sealed partial class Parameter_MachineHours { }
}
