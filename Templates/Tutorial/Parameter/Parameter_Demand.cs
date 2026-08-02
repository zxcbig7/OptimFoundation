using OptimFoundation.Core;
using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>每日需求下限（件）。數學：Demand_{p,d}，值在 QTY。2D 參數（Product × Date，含 DateTime 維度）。
    /// [FullGrid]：3 產品 × 2 日本來就滿格，作為框架資料防護規格的完整性檢查活範例。</summary>
    [OptParam]
    [FullGrid]
    [OptDim<Set_Product>("Product")]
    [OptDim<Set_Date>("Date")]
    public sealed partial class Parameter_Demand { }
}
