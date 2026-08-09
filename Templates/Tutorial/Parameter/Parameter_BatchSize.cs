using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>每批數量（件/批）。數學：BatchSize_p，值在 QTY。1D 參數；BatchDef 用來連結整數批數與連續產量。</summary>
    [OptParam]
    [OptDim<string>("Product")]
    public sealed partial class Parameter_BatchSize { }
}
