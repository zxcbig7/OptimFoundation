using OptimFoundation.Modeling;

namespace Sudoku_SHC279
{
    /// <summary>每個宮涵蓋哪些格；對應 Model.md 的 BlockCell。宮的劃分規則資料化，邊長不寫在 code 裡。</summary>
    [OptParam(HasValue = false)]
    [OptDim<Set_Block>("Block")]
    [OptDim<Set_Row>("Row")]
    [OptDim<Set_Column>("Column")]
    public sealed partial class Parameter_BlockCell { }
}
