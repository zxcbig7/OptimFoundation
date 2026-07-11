namespace OptimFoundation.Core
{
    /// <summary>變數型別。</summary>
    public enum VarType
    {
        Continuous, // 連續（實數）
        Integer, // 整數
        Binary // 二元 0/1
    }

    /// <summary>限制式比較方向。</summary>
    public enum ConstraintSense
    {
        LessEqual, // ≤
        Equal, // =
        GreaterEqual // ≥
    }

    /// <summary>目標式最佳化方向。</summary>
    public enum ObjectiveSense
    {
        Minimize, // 最小化
        Maximize // 最大化
    }
}
