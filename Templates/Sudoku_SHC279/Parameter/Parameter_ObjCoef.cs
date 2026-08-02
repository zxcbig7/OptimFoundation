using OptimFoundation.Modeling;

namespace Sudoku_SHC279
{
    /// <summary>目標式中各數字的權重；對應 Model.md 的 ObjCoef。可行性問題故全為 0。</summary>
    [OptParam]
    [OptDim<Set_Digit>("Digit")]
    public sealed partial class Parameter_ObjCoef { }
}
