using OptimFoundation.Modeling;

namespace Sudoku_SHC279
{
    /// <summary>固定值 1，用來要求每個數字恰好出現一次；全模型共用一個值，對應 Model.md 的 ExactlyOne。</summary>
    [OptParam]
    public sealed partial class Parameter_ExactlyOne { }
}
