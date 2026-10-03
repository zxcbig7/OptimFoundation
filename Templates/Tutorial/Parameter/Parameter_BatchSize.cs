using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>BatchSize_p（件/批，QTY）；連結整數批數與連續產量。</summary>
    [OptParam]
    [OptDim<string>("Product")]
    public sealed partial class Parameter_BatchSize { }
}
