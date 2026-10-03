using OptimFoundation.Core;
using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>Demand_{p,d}（件，QTY）：每日需求下限。</summary>
    [OptParam]
    [OptDim<string>("Product")]
    [OptDim<DateTime>("Date")]
    public sealed partial class Parameter_Demand { }
}
