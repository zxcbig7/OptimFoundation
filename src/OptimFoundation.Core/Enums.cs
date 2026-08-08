namespace OptimFoundation.Core
{
    /// <summary>變數型別。</summary>
    public enum VarType
    {
        /// <summary>連續變數。</summary>
        Continuous,

        /// <summary>整數變數。</summary>
        Integer,

        /// <summary>二元 0/1 變數。</summary>
        Binary
    }

    /// <summary>限制式比較方向。</summary>
    public enum ConstraintSense
    {
        /// <summary>小於等於 ≤。</summary>
        LessEqual,

        /// <summary>等於 =。</summary>
        Equal,

        /// <summary>大於等於 ≥。</summary>
        GreaterEqual
    }

    /// <summary>目標式最佳化方向。</summary>
    public enum ObjectiveSense
    {
        /// <summary>最小化。軟性限制式的 penalty 以正號併入目標式。</summary>
        Minimize,

        /// <summary>最大化。軟性限制式的 penalty 以負號併入目標式。</summary>
        Maximize
    }
}
